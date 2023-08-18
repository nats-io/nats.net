using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

internal sealed class NatsReadProtocolProcessor : IAsyncDisposable
{
    private readonly NatsConnection _connection;
    private readonly SocketReader _socketReader;
    private readonly Task _readLoop;
    private readonly TaskCompletionSource _waitForInfoSignal;
    private readonly TaskCompletionSource _waitForPongOrErrorSignal;  // wait for initial connection
    private readonly Task _infoParsed; // wait for an upgrade
    private readonly ConcurrentQueue<AsyncPingCommand> _pingCommands; // wait for pong
    private readonly ILogger<NatsReadProtocolProcessor> _logger;
    private readonly bool _trace;
    private int _disposed;

    public NatsReadProtocolProcessor(ISocketConnection socketConnection, NatsConnection connection, TaskCompletionSource waitForInfoSignal, TaskCompletionSource waitForPongOrErrorSignal, Task infoParsed)
    {
        _connection = connection;
        _logger = connection.Opts.LoggerFactory.CreateLogger<NatsReadProtocolProcessor>();
        _trace = _logger.IsEnabled(LogLevel.Trace);
        _waitForInfoSignal = waitForInfoSignal;
        _waitForPongOrErrorSignal = waitForPongOrErrorSignal;
        _infoParsed = infoParsed;
        _pingCommands = new ConcurrentQueue<AsyncPingCommand>();
        _socketReader = new SocketReader(socketConnection, connection.Opts.ReaderBufferSize, connection.Counter, connection.Opts.LoggerFactory);
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

        var serverInfo = JsonSerializer.Deserialize(ref jsonReader, JsonContext.Default.ServerInfo)
                         ?? throw new NatsException("Can not parse ServerInfo.");
        return serverInfo;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#+ok-err
    // -ERR <error message>
    private static string ParseError(in ReadOnlySequence<byte> errorSlice)
    {
        // SKip `-ERR `
        return Encoding.UTF8.GetString(errorSlice.Slice(5));
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
                        var (subject, sid, payloadLength, replyTo) = ParseMessageHeader(msgHeader);

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

                            await _connection.PublishToClientHandlersAsync(subject, replyTo, sid, null, ReadOnlySequence<byte>.Empty).ConfigureAwait(false);
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

                            await _connection.PublishToClientHandlersAsync(subject, replyTo, sid, null, payloadSlice).ConfigureAwait(false);
                        }
                    }
                    else if (code == ServerOpCodes.HMsg)
                    {
                        // https://docs.nats.io/reference/reference-protocols/nats-protocol#hmsg
                        // HMSG <subject> <sid> [reply-to] <#header bytes> <#total bytes>\r\n[headers]\r\n\r\n[payload]\r\n

                        // Find the end of 'HMSG' first message line
                        var positionBeforeNatsHeader = buffer.PositionOf((byte)'\n');
                        if (positionBeforeNatsHeader == null)
                        {
                            _socketReader.AdvanceTo(buffer.Start);
                            buffer = await _socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                            positionBeforeNatsHeader = buffer.PositionOf((byte)'\n')!;
                        }

                        var msgHeader = buffer.Slice(0, positionBeforeNatsHeader.Value);

                        if (_trace)
                        {
                            _logger.LogTrace("HMSG trace dump: {MsgHeader}", msgHeader.Dump());
                        }

                        var (subject, sid, replyTo, headersLength, totalLength) = ParseHMessageHeader(msgHeader);

                        if (_trace)
                        {
                            _logger.LogTrace("HMSG trace parsed: {Subject} {Sid} {ReplyTo} {HeadersLength} {TotalLength}", subject, sid, replyTo, headersLength, totalLength);
                        }

                        var payloadLength = totalLength - headersLength;
                        Debug.Assert(payloadLength >= 0, "Protocol error: illogical header and total lengths");

                        var headerBegin = buffer.GetPosition(1, positionBeforeNatsHeader.Value);
                        var totalSlice = buffer.Slice(headerBegin);

                        // Read rest of the message if it's not already in the buffer
                        if (totalSlice.Length < totalLength + 2)
                        {
                            _socketReader.AdvanceTo(headerBegin);

                            // Read headers + payload + \r\n
                            var size = totalLength - (int)totalSlice.Length + 2;
                            buffer = await _socketReader.ReadAtLeastAsync(size).ConfigureAwait(false);
                            totalSlice = buffer.Slice(0, totalLength);
                        }
                        else
                        {
                            totalSlice = totalSlice.Slice(0, totalLength);
                        }

                        // Prepare buffer for the next message by removing 'headers + payload + \r\n' from it
                        buffer = buffer.Slice(buffer.GetPosition(2, totalSlice.End));

                        var headerSlice = totalSlice.Slice(0, headersLength);
                        var payloadSlice = totalSlice.Slice(headersLength, payloadLength);

                        await _connection.PublishToClientHandlersAsync(subject, replyTo, sid, headerSlice, payloadSlice)
                            .ConfigureAwait(false);
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
                _connection.WritableServerInfo = serverInfo;
                _logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                _waitForInfoSignal.TrySetResult();
                await _infoParsed.ConfigureAwait(false);
                return newBuffer.Slice(newBuffer.GetPosition(1, newPosition!.Value));
            }
            else
            {
                var serverInfo = ParseInfo(buffer);
                _connection.WritableServerInfo = serverInfo;
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
    private (string subject, int sid, int payloadLength, string? replyTo) ParseMessageHeader(ReadOnlySpan<byte> msgHeader)
    {
        msgHeader = msgHeader.Slice(4);
        msgHeader.Split(out var subjectBytes, out msgHeader);
        msgHeader.Split(out var sidBytes, out msgHeader);
        msgHeader.Split(out var replyToOrSizeBytes, out msgHeader);

        var subject = Encoding.ASCII.GetString(subjectBytes);

        if (msgHeader.Length == 0)
        {
            var sid = GetInt32(sidBytes);
            var size = GetInt32(replyToOrSizeBytes);
            return (subject, sid, size, null);
        }
        else
        {
            var replyToBytes = replyToOrSizeBytes;
            var bytesSlice = msgHeader;

            var sid = GetInt32(sidBytes);
            var payloadLength = GetInt32(bytesSlice);
            var replyTo = Encoding.ASCII.GetString(replyToBytes);
            return (subject, sid, payloadLength, replyTo);
        }
    }

    private (string subject, int sid, int payloadLength, string? replyTo) ParseMessageHeader(in ReadOnlySequence<byte> msgHeader)
    {
        if (msgHeader.IsSingleSegment)
        {
            return ParseMessageHeader(msgHeader.FirstSpan);
        }

        // header parsing use Slice frequently so ReadOnlySequence is high cost, should use Span.
        // msgheader is not too long, ok to use stackalloc in most cases.
        const int maxAlloc = 256;
        var msgHeaderLength = (int)msgHeader.Length;
        if (msgHeaderLength <= maxAlloc)
        {
            Span<byte> buffer = stackalloc byte[msgHeaderLength];
            msgHeader.CopyTo(buffer);
            return ParseMessageHeader(buffer);
        }
        else
        {
            return ParseMessageHeader(msgHeader.ToSpan());
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#hmsg
    // HMSG <subject> <sid> [reply-to] <#header bytes> <#total bytes>\r\n[headers]\r\n\r\n[payload]\r\n
    private (string subject, int sid, string? replyTo, int headersLength, int totalLength) ParseHMessageHeader(ReadOnlySpan<byte> msgHeader)
    {
        // 'HMSG' literal
        msgHeader.Split(out _, out msgHeader);

        msgHeader.Split(out var subjectBytes, out msgHeader);
        msgHeader.Split(out var sidBytes, out msgHeader);
        msgHeader.Split(out var replyToOrHeaderLenBytes, out msgHeader);
        msgHeader.Split(out var headerLenOrTotalLenBytes, out msgHeader);

        var subject = Encoding.ASCII.GetString(subjectBytes);
        var sid = GetInt32(sidBytes);

        // We don't have the optional reply-to field
        if (msgHeader.Length == 0)
        {
            var headersLength = GetInt32(replyToOrHeaderLenBytes);
            var totalLen = GetInt32(headerLenOrTotalLenBytes);
            return (subject, sid, null, headersLength, totalLen);
        }

        // There is more data because of the reply-to field
        else
        {
            var replyToBytes = replyToOrHeaderLenBytes;
            var replyTo = Encoding.ASCII.GetString(replyToBytes);

            var headerLen = GetInt32(headerLenOrTotalLenBytes);

            var lastBytes = msgHeader;
            var totalLen = GetInt32(lastBytes);

            return (subject, sid, replyTo, headerLen, totalLen);
        }
    }

    private (string subject, int sid, string? replyTo, int headersLength, int totalLength) ParseHMessageHeader(in ReadOnlySequence<byte> msgHeader)
    {
        if (msgHeader.IsSingleSegment)
        {
            return ParseHMessageHeader(msgHeader.FirstSpan);
        }

        // header parsing use Slice frequently so ReadOnlySequence is high cost, should use Span.
        // msgheader is not too long, ok to use stackalloc in most cases.
        const int maxAlloc = 256;
        var msgHeaderLength = (int)msgHeader.Length;
        if (msgHeaderLength <= maxAlloc)
        {
            Span<byte> buffer = stackalloc byte[msgHeaderLength];
            msgHeader.CopyTo(buffer);
            return ParseHMessageHeader(buffer);
        }
        else
        {
            return ParseHMessageHeader(msgHeader.ToSpan());
        }
    }

    internal static class ServerOpCodes
    {
        // All sent by server commands as int(first 4 characters(includes space, newline)).
        public const int Info = 1330007625;  // Encoding.ASCII.GetBytes("INFO") |> MemoryMarshal.Read<int>
        public const int Msg = 541545293;    // Encoding.ASCII.GetBytes("MSG ") |> MemoryMarshal.Read<int>
        public const int HMsg = 1196641608;  // Encoding.ASCII.GetBytes("HMSG") |> MemoryMarshal.Read<int>
        public const int Ping = 1196312912;  // Encoding.ASCII.GetBytes("PING") |> MemoryMarshal.Read<int>
        public const int Pong = 1196314448;  // Encoding.ASCII.GetBytes("PONG") |> MemoryMarshal.Read<int>
        public const int Ok = 223039275;     // Encoding.ASCII.GetBytes("+OK\r") |> MemoryMarshal.Read<int>
        public const int Error = 1381123373; // Encoding.ASCII.GetBytes("-ERR") |> MemoryMarshal.Read<int>
    }
}
