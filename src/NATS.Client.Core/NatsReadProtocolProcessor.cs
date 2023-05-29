using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

internal sealed class NatsReadProtocolProcessor : IAsyncDisposable
{
    private readonly ISocketConnection _socketConnection;
    private readonly NatsConnection _connection;
    private readonly SocketReader _socketReader;
    private readonly Task _readLoop;
    private readonly TaskCompletionSource _waitForInfoSignal;
    private readonly TaskCompletionSource _waitForPongOrErrorSignal;  // wait for initial connection
    private readonly Task _infoParsed; // wait for an upgrade
    private readonly ConcurrentQueue<AsyncPingCommand> _pingCommands; // wait for pong
    private readonly ILogger<NatsReadProtocolProcessor> _logger;
    private readonly bool _isEnabledTraceLogging;
    private int _disposed;

    public NatsReadProtocolProcessor(ISocketConnection socketConnection, NatsConnection connection, TaskCompletionSource waitForInfoSignal, TaskCompletionSource waitForPongOrErrorSignal, Task infoParsed)
    {
        _socketConnection = socketConnection;
        _connection = connection;
        _logger = connection.Options.LoggerFactory.CreateLogger<NatsReadProtocolProcessor>();
        _isEnabledTraceLogging = _logger.IsEnabled(LogLevel.Trace);
        _waitForInfoSignal = waitForInfoSignal;
        _waitForPongOrErrorSignal = waitForPongOrErrorSignal;
        _infoParsed = infoParsed;
        _pingCommands = new ConcurrentQueue<AsyncPingCommand>();
        _socketReader = new SocketReader(socketConnection, connection.Options.ReaderBufferSize, connection.Counter, connection.Options.LoggerFactory);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public bool TryEnqueuePing(AsyncPingCommand ping)
    {
        if (_disposed != 0)
            return false;
        _pingCommands.Enqueue(ping);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposed) == 1)
        {
            await _readLoop.ConfigureAwait(false); // wait for drain buffer.
            foreach (var item in _pingCommands)
            {
                item.SetCanceled();
            }

            _waitForInfoSignal.TrySetCanceled();
            _waitForPongOrErrorSignal.TrySetCanceled();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCode(ReadOnlySpan<byte> span)
    {
        return Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference<byte>(span));
    }

    private static int GetCode(in ReadOnlySequence<byte> sequence)
    {
        Span<byte> buf = stackalloc byte[4];
        sequence.Slice(0, 4).CopyTo(buf);
        return GetCode(buf);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetInt32(ReadOnlySpan<byte> span)
    {
        if (!Utf8Parser.TryParse(span, out int value, out var consumed))
        {
            throw new Exception(); // throw...
        }

        return value;
    }

    private static int GetInt32(in ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment || sequence.FirstSpan.Length <= 10)
        {
            return GetInt32(sequence.FirstSpan);
        }

        Span<byte> buf = stackalloc byte[Math.Min((int)sequence.Length, 10)];
        sequence.Slice(buf.Length).CopyTo(buf);
        return GetInt32(buf);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#info
    // INFO {["option_name":option_value],...}
    private static ServerInfo ParseInfo(in ReadOnlySequence<byte> buffer)
    {
        // skip `INFO`
        var jsonReader = new Utf8JsonReader(buffer.Slice(5));

        var serverInfo = JsonSerializer.Deserialize<ServerInfo>(ref jsonReader);
        if (serverInfo == null)
            throw new NatsException("Can not parse ServerInfo.");
        return serverInfo;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#+ok-err
    // -ERR <error message>
    private static string ParseError(in ReadOnlySequence<byte> errorSlice)
    {
        // SKip `-ERR `
        return Encoding.UTF8.GetString(errorSlice.Slice(5));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Split(ReadOnlySpan<byte> span, out ReadOnlySpan<byte> left, out ReadOnlySpan<byte> right)
    {
        var i = span.IndexOf((byte)' ');
        if (i == -1)
        {
            left = span;
            right = default;
            return;
        }

        left = span.Slice(0, i);
        right = span.Slice(i + 1);
    }

    private async Task ReadLoopAsync()
    {
        while (true)
        {
            try
            {
                // when read buffer is complete, ReadFully.
                var buffer = await _socketReader.ReadAtLeastAsync(4).ConfigureAwait(false);

                // parse messages from buffer without additional socket read
                // Note: in this loop, use socketReader.Read "must" requires socketReader.AdvanceTo
                //       because buffer-sequence and reader's sequence state is not synced to optimize performance.
                while (buffer.Length > 0)
                {
                    var first = buffer.First;

                    int code;
                    if (first.Length >= 4)
                    {
                        code = GetCode(first.Span);
                    }
                    else
                    {
                        if (buffer.Length < 4)
                        {
                            // try get additional buffer to require read Code
                            _socketReader.AdvanceTo(buffer.Start);
                            buffer = await _socketReader.ReadAtLeastAsync(4 - (int)buffer.Length).ConfigureAwait(false);
                        }

                        code = GetCode(buffer);
                    }

                    Interlocked.Increment(ref _connection.Counter.ReceivedMessages);

                    // Optimize for Msg parsing, Inline async code
                    if (code == ServerOpCodes.Msg)
                    {
                        // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
                        // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]

                        // Try to find before \n
                        var positionBeforePayload = buffer.PositionOf((byte)'\n');
                        if (positionBeforePayload == null)
                        {
                            _socketReader.AdvanceTo(buffer.Start);
                            buffer = await _socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                            positionBeforePayload = buffer.PositionOf((byte)'\n')!;
                        }

                        var msgHeader = buffer.Slice(0, positionBeforePayload.Value);
                        var (subject, subscriptionId, payloadLength, replyTo, reponseId) = ParseMessageHeader(msgHeader);

                        if (payloadLength == 0)
                        {
                            // payload is empty.
                            var payloadBegin = buffer.GetPosition(1, positionBeforePayload.Value);
                            var payloadSlice = buffer.Slice(payloadBegin);
                            if (payloadSlice.Length < 2)
                            {
                                _socketReader.AdvanceTo(payloadBegin);
                                buffer = await _socketReader.ReadAtLeastAsync(2).ConfigureAwait(false); // \r\n
                                buffer = buffer.Slice(2);
                            }
                            else
                            {
                                buffer = buffer.Slice(buffer.GetPosition(3, positionBeforePayload.Value));
                            }

                            // TODO: Clean-up MSG handling
                            // publish to registered handlers.
                            // if (replyTo != null)
                            // {
                            //     _connection.PublishToRequestHandler(subscriptionId, replyTo.Value, buffer);
                            // }
                            // else if (reponseId != null)
                            // {
                            //     _connection.PublishToResponseHandler(reponseId.Value, buffer);
                            // }
                            // else
                            {
                                await _connection.PublishToClientHandlersAsync(subject, replyTo?.Key, subscriptionId, ReadOnlySequence<byte>.Empty).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            var payloadBegin = buffer.GetPosition(1, positionBeforePayload.Value);
                            var payloadSlice = buffer.Slice(payloadBegin);

                            // slice required \r\n
                            if (payloadSlice.Length < (payloadLength + 2))
                            {
                                _socketReader.AdvanceTo(payloadBegin);
                                buffer = await _socketReader.ReadAtLeastAsync(payloadLength - (int)payloadSlice.Length + 2).ConfigureAwait(false); // payload + \r\n
                                payloadSlice = buffer.Slice(0, payloadLength);
                            }
                            else
                            {
                                payloadSlice = payloadSlice.Slice(0, payloadLength);
                            }

                            buffer = buffer.Slice(buffer.GetPosition(2, payloadSlice.End)); // payload + \r\n

                            // publish to registered handlers.
                            // if (replyTo != null)
                            // {
                            //     _connection.PublishToRequestHandler(subscriptionId, replyTo.Value, payloadSlice);
                            // }
                            // else if (reponseId != null)
                            // {
                            //     _connection.PublishToResponseHandler(reponseId.Value, payloadSlice);
                            // }
                            // else
                            {
                                await _connection.PublishToClientHandlersAsync(subject, replyTo?.Key, subscriptionId, payloadSlice).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        buffer = await DispatchCommandAsync(code, buffer).ConfigureAwait(false);
                    }
                }

                // Length == 0, AdvanceTo End.
                _socketReader.AdvanceTo(buffer.End);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketClosedException e)
            {
                _waitForInfoSignal.TrySetException(e);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occured during read loop.");
                continue;
            }
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<ReadOnlySequence<byte>> DispatchCommandAsync(int code, ReadOnlySequence<byte> buffer)
    {
        var length = (int)buffer.Length;

        if (code == ServerOpCodes.Ping)
        {
            const int PingSize = 6; // PING\r\n

            await _connection.PostPongAsync().ConfigureAwait(false); // return pong to server

            if (length < PingSize)
            {
                _socketReader.AdvanceTo(buffer.Start);
                var readResult = await _socketReader.ReadAtLeastAsync(PingSize - length).ConfigureAwait(false);
                return readResult.Slice(PingSize);
            }

            return buffer.Slice(PingSize);
        }
        else if (code == ServerOpCodes.Pong)
        {
            const int PongSize = 6; // PONG\r\n

            _connection.ResetPongCount(); // reset count for PingTimer
            _waitForPongOrErrorSignal.TrySetResult(); // set for initial connect

            if (_pingCommands.TryDequeue(out var pingCommand))
            {
                var start = pingCommand.WriteTime;
                var elapsed = DateTimeOffset.UtcNow - start;
                pingCommand.SetResult(elapsed ?? TimeSpan.Zero);
            }

            if (length < PongSize)
            {
                _socketReader.AdvanceTo(buffer.Start);
                var readResult = await _socketReader.ReadAtLeastAsync(PongSize - length).ConfigureAwait(false);
                return readResult.Slice(PongSize);
            }

            return buffer.Slice(PongSize);
        }
        else if (code == ServerOpCodes.Error)
        {
            // try to get \n.
            var position = buffer.PositionOf((byte)'\n');
            if (position == null)
            {
                _socketReader.AdvanceTo(buffer.Start);
                var newBuffer = await _socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                var newPosition = newBuffer.PositionOf((byte)'\n');
                var error = ParseError(newBuffer.Slice(0, buffer.GetOffset(newPosition!.Value) - 1));
                _logger.LogError(error);
                _waitForPongOrErrorSignal.TrySetException(new NatsException(error));
                return newBuffer.Slice(newBuffer.GetPosition(1, newPosition!.Value));
            }
            else
            {
                var error = ParseError(buffer.Slice(0, buffer.GetOffset(position.Value) - 1));
                _logger.LogError(error);
                _waitForPongOrErrorSignal.TrySetException(new NatsException(error));
                return buffer.Slice(buffer.GetPosition(1, position.Value));
            }
        }
        else if (code == ServerOpCodes.Ok)
        {
            const int OkSize = 5; // +OK\r\n

            if (length < OkSize)
            {
                _socketReader.AdvanceTo(buffer.Start);
                var readResult = await _socketReader.ReadAtLeastAsync(OkSize - length).ConfigureAwait(false);
                return readResult.Slice(OkSize);
            }

            return buffer.Slice(OkSize);
        }
        else if (code == ServerOpCodes.Info)
        {
            // try to get \n.
            var position = buffer.PositionOf((byte)'\n');

            if (position == null)
            {
                _socketReader.AdvanceTo(buffer.Start);
                var newBuffer = await _socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                var newPosition = newBuffer.PositionOf((byte)'\n');

                var serverInfo = ParseInfo(newBuffer);
                _connection.ServerInfo = serverInfo;
                _logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                _waitForInfoSignal.TrySetResult();
                await _infoParsed.ConfigureAwait(false);
                return newBuffer.Slice(newBuffer.GetPosition(1, newPosition!.Value));
            }
            else
            {
                var serverInfo = ParseInfo(buffer);
                _connection.ServerInfo = serverInfo;
                _logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                _waitForInfoSignal.TrySetResult();
                await _infoParsed.ConfigureAwait(false);
                return buffer.Slice(buffer.GetPosition(1, position.Value));
            }
        }
        else
        {
            // reaches invalid line, log warn and try to get newline and go to nextloop.
            _logger.LogWarning("reaches invalid line.");
            Interlocked.Decrement(ref _connection.Counter.ReceivedMessages);

            var position = buffer.PositionOf((byte)'\n');
            if (position == null)
            {
                _socketReader.AdvanceTo(buffer.Start);
                var newBuffer = await _socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                var newPosition = newBuffer.PositionOf((byte)'\n');
                return newBuffer.Slice(newBuffer.GetPosition(1, newPosition!.Value));
            }
            else
            {
                return buffer.Slice(buffer.GetPosition(1, position.Value));
            }
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
    // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]
    private (string subject, int subscriptionId, int payloadLength, NatsKey? replyTo, int? responseId) ParseMessageHeader(ReadOnlySpan<byte> msgHeader)
    {
        msgHeader = msgHeader.Slice(4);
        Split(msgHeader, out var subjectBytes, out msgHeader);
        Split(msgHeader, out var sid, out msgHeader);
        Split(msgHeader, out var replyToOrBytes, out msgHeader);

        var subject = Encoding.ASCII.GetString(subjectBytes);

        if (msgHeader.Length == 0)
        {
            int? responseId = null;

            // Parse: _INBOX.RANDOM-GUID.ID
            if (subjectBytes.StartsWith(_connection.InboxPrefix.Span))
            {
                var lastIndex = subjectBytes.LastIndexOf((byte)'.');
                if (lastIndex != -1)
                {
                    if (Utf8Parser.TryParse(subjectBytes.Slice(lastIndex + 1), out int id, out _))
                    {
                        responseId = id;
                    }
                }
            }

            var subscriptionId = GetInt32(sid);
            var payloadLength = GetInt32(replyToOrBytes);
            return (subject, subscriptionId, payloadLength, null, responseId);
        }
        else
        {
            var replyTo = replyToOrBytes;
            var bytesSlice = msgHeader;

            var subscriptionId = GetInt32(sid);
            var payloadLength = GetInt32(bytesSlice);
            var replyToKey = new NatsKey(Encoding.ASCII.GetString(replyTo));
            return (subject, subscriptionId, payloadLength, replyToKey, null);
        }
    }

    private (string subject, int subscriptionId, int payloadLength, NatsKey? replyTo, int? responseId) ParseMessageHeader(in ReadOnlySequence<byte> msgHeader)
    {
        if (msgHeader.IsSingleSegment)
        {
            return ParseMessageHeader(msgHeader.FirstSpan);
        }

        // header parsing use Slice frequently so ReadOnlySequence is high cost, should use Span.
        // msgheader is not too long, ok to use stackalloc.
        // TODO: Fix possible stack overflow
        Span<byte> buffer = stackalloc byte[(int)msgHeader.Length];
        msgHeader.CopyTo(buffer);
        return ParseMessageHeader(buffer);
    }

    internal static class ServerOpCodes
    {
        // All sent by server commands as int(first 4 characters(includes space, newline)).
        public const int Info = 1330007625;  // Encoding.ASCII.GetBytes("INFO") |> MemoryMarshal.Read<int>
        public const int Msg = 541545293;    // Encoding.ASCII.GetBytes("MSG ") |> MemoryMarshal.Read<int>
        public const int Ping = 1196312912;  // Encoding.ASCII.GetBytes("PING") |> MemoryMarshal.Read<int>
        public const int Pong = 1196314448;  // Encoding.ASCII.GetBytes("PONG") |> MemoryMarshal.Read<int>
        public const int Ok = 223039275;     // Encoding.ASCII.GetBytes("+OK\r") |> MemoryMarshal.Read<int>
        public const int Error = 1381123373; // Encoding.ASCII.GetBytes("-ERR") |> MemoryMarshal.Read<int>
    }
}
