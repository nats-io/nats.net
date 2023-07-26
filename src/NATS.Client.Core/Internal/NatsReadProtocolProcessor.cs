using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
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
    private readonly Task _fillPipe;
    private readonly Task _readLoop;
    private readonly TaskCompletionSource _waitForInfoSignal;
    private readonly TaskCompletionSource _waitForPongOrErrorSignal; // wait for initial connection
    private readonly Task _infoParsed; // wait for an upgrade
    private readonly ConcurrentQueue<AsyncPingCommand> _pingCommands; // wait for pong
    private readonly ILogger<NatsReadProtocolProcessor> _logger;
    private readonly int _minimumBufferSize;
    private readonly ISocketConnection _socketConnection;
    private readonly ConnectionStatsCounter _counter;
    private readonly Pipe _pipe;
    private int _disposed;

    public NatsReadProtocolProcessor(
        ISocketConnection socketConnection,
        NatsConnection connection,
        TaskCompletionSource waitForInfoSignal,
        TaskCompletionSource waitForPongOrErrorSignal,
        Task infoParsed)
    {
        _connection = connection;
        _logger = connection.Options.LoggerFactory.CreateLogger<NatsReadProtocolProcessor>();
        _logger.IsEnabled(LogLevel.Trace);
        _waitForInfoSignal = waitForInfoSignal;
        _waitForPongOrErrorSignal = waitForPongOrErrorSignal;
        _infoParsed = infoParsed;
        _pingCommands = new ConcurrentQueue<AsyncPingCommand>();
        _socketConnection = socketConnection;
        _minimumBufferSize = connection.Options.ReaderBufferSize;
        _counter = connection.Counter;
        _pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: _minimumBufferSize,
            resumeWriterThreshold: _minimumBufferSize / 2));
        _fillPipe = Task.Run(FillPipeAsync);
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
            _pipe.Reader.CancelPendingRead();
            await _fillPipe.ConfigureAwait(false); // wait for pipe writer
            await _readLoop.ConfigureAwait(false); // wait for pipe reader
            foreach (var item in _pingCommands)
            {
                item.SetCanceled();
            }

            _waitForInfoSignal.TrySetCanceled();
            _waitForPongOrErrorSignal.TrySetCanceled();
            await _socketConnection.DisposeAsync().ConfigureAwait(false);
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

        Span<byte> buf = stackalloc byte[Math.Min((int) sequence.Length, 10)];
        sequence.Slice(buf.Length).CopyTo(buf);
        return GetInt32(buf);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#info
    // INFO {["option_name":option_value],...}
    private static ServerInfo ParseInfo(in ReadOnlySequence<byte> buffer)
    {
        // skip `INFO`
        var jsonReader = new Utf8JsonReader(buffer.Slice(5));

        var serverInfo = JsonSerializer.Deserialize<ServerInfo>(ref jsonReader) ?? throw new NatsException("Can not parse ServerInfo.");
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
        var i = span.IndexOf((byte) ' ');
        if (i == -1)
        {
            left = span;
            right = default;
            return;
        }

        left = span.Slice(0, i);
        right = span.Slice(i + 1);
    }

    private async Task FillPipeAsync()
    {
        var writer = _pipe.Writer;
        try
        {
            while (true)
            {
                var memory = writer.GetMemory(_minimumBufferSize / 2);
                var read = await _socketConnection.ReceiveAsync(memory).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new SocketClosedException(null);
                }

                writer.Advance(read);
                Interlocked.Add(ref _counter.ReceivedBytes, read);

                // Make the data available to the PipeReader.
                var result = await writer.FlushAsync().ConfigureAwait(false);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or SocketClosedException)
        {
            _socketConnection.SignalDisconnected(ex);
            _waitForInfoSignal.TrySetException(ex);
        }
        catch (Exception ex)
        {
            _socketConnection.SignalDisconnected(ex);
            _waitForInfoSignal.TrySetException(ex);
            _logger.LogError(ex, "Error occured during read loop.");
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync()
    {
        var reader = _pipe.Reader;
        try
        {
            while (true)
            {
                // when read buffer is complete, ReadFully.
                var result = await reader.ReadAtLeastAsync(4).ConfigureAwait(false);
                if (result.IsCompleted || result.IsCanceled)
                    return;
                var buffer = result.Buffer;
                var code = buffer.First.Length >= 4 ? GetCode(buffer.First.Span) : GetCode(buffer);

                Interlocked.Increment(ref _connection.Counter.ReceivedMessages);

                // Optimize for Msg parsing, Inline async code
                if (code == ServerOpCodes.Msg)
                {
                    // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
                    // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]

                    // Try to find before \n
                    var positionBeforePayload = buffer.PositionOf((byte) '\n');
                    while (positionBeforePayload == null)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        result = await reader.ReadAsync().ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        positionBeforePayload = buffer.PositionOf((byte) '\n');
                    }

                    var msgHeader = buffer.Slice(0, positionBeforePayload.Value);
                    var (subject, sid, payloadLength, replyTo) = ParseMessageHeader(msgHeader);
                    reader.AdvanceTo(buffer.GetPosition(1, positionBeforePayload.Value));

                    if (payloadLength == 0)
                    {
                        // payload is empty.
                        result = await reader.ReadAtLeastAsync(2).ConfigureAwait(false); // \r\n
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        reader.AdvanceTo(buffer.GetPosition(2));

                        await _connection.PublishToClientHandlersAsync(subject, replyTo, sid, null, ReadOnlySequence<byte>.Empty).ConfigureAwait(false);
                    }
                    else
                    {
                        result = await reader.ReadAtLeastAsync(payloadLength + 2)
                            .ConfigureAwait(false); // payload + \r\n
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        var payloadSlice = buffer.Slice(0, payloadLength);
                        await _connection.PublishToClientHandlersAsync(subject, replyTo, sid, null, payloadSlice).ConfigureAwait(false);
                        reader.AdvanceTo(buffer.GetPosition(payloadLength + 2));
                    }
                }
                else if (code == ServerOpCodes.HMsg)
                {
                    // https://docs.nats.io/reference/reference-protocols/nats-protocol#hmsg
                    // HMSG <subject> <sid> [reply-to] <#header bytes> <#total bytes>\r\n[headers]\r\n\r\n[payload]\r\n

                    // Find the end of 'HMSG' first message line
                    var positionBeforeNatsHeader = buffer.PositionOf((byte) '\n');
                    while (positionBeforeNatsHeader == null)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        result = await reader.ReadAsync().ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        positionBeforeNatsHeader = buffer.PositionOf((byte) '\n');
                    }

                    var msgHeader = buffer.Slice(0, positionBeforeNatsHeader.Value);
                    var (subject, sid, replyTo, headersLength, totalLength) = ParseHMessageHeader(msgHeader);
                    var payloadLength = totalLength - headersLength;
                    Debug.Assert(payloadLength >= 0, "Protocol error: illogical header and total lengths");
                    reader.AdvanceTo(buffer.GetPosition(1, positionBeforeNatsHeader.Value));

                    result = await reader.ReadAtLeastAsync(totalLength + 2).ConfigureAwait(false); // payload + \r\n
                    if (result.IsCompleted || result.IsCanceled)
                        return;
                    buffer = result.Buffer;

                    var versionLength = CommandConstants.NatsHeaders10NewLine.Length;
                    var versionSlice = buffer.Slice(0, versionLength);
                    if (!versionSlice.ToSpan().SequenceEqual(CommandConstants.NatsHeaders10NewLine))
                    {
                        throw new NatsException("Protocol error: header version mismatch");
                    }

                    var headerSlice = buffer.Slice(versionLength, headersLength - versionLength);
                    var payloadSlice = buffer.Slice(headersLength, payloadLength);

                    await _connection.PublishToClientHandlersAsync(subject, replyTo, sid, headerSlice, payloadSlice).ConfigureAwait(false);
                    reader.AdvanceTo(buffer.GetPosition(totalLength + 2));
                }
                else if (code == ServerOpCodes.Ping)
                {
                    const int PingSize = 6; // PING\r\n
                    await _connection.PostPongAsync().ConfigureAwait(false); // return pong to server

                    if (buffer.Length < PingSize)
                    {
                        result = await reader.ReadAtLeastAsync(PingSize).ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                    }

                    reader.AdvanceTo(buffer.GetPosition(PingSize));
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

                    if (buffer.Length < PongSize)
                    {
                        result = await reader.ReadAtLeastAsync(PongSize).ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                    }

                    reader.AdvanceTo(buffer.GetPosition(PongSize));
                }
                else if (code == ServerOpCodes.Error)
                {
                    // try to get \n.
                    var position = buffer.PositionOf((byte) '\n');
                    while (position == null)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        result = await reader.ReadAsync().ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        position = buffer.PositionOf((byte) '\n');
                    }

                    var error = ParseError(buffer.Slice(0, buffer.GetOffset(position.Value) - 1));
                    _logger.LogError(error);
                    _waitForPongOrErrorSignal.TrySetException(new NatsException(error));
                    reader.AdvanceTo(buffer.GetPosition(1, position.Value));
                }
                else if (code == ServerOpCodes.Ok)
                {
                    const int OkSize = 5; // +OK\r\n

                    if (buffer.Length < OkSize)
                    {
                        result = await reader.ReadAtLeastAsync(OkSize).ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                    }

                    reader.AdvanceTo(buffer.GetPosition(OkSize));
                }
                else if (code == ServerOpCodes.Info)
                {
                    // try to get \n.
                    var position = buffer.PositionOf((byte) '\n');
                    while (position == null)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        result = await reader.ReadAsync().ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        position = buffer.PositionOf((byte) '\n');
                    }

                    var serverInfo = ParseInfo(buffer);
                    _connection.ServerInfo = serverInfo;
                    _logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                    _waitForInfoSignal.TrySetResult();
                    await _infoParsed.ConfigureAwait(false);
                    reader.AdvanceTo(buffer.GetPosition(1, position.Value));
                }
                else
                {
                    // reaches invalid line, log warn and try to get newline and go to nextloop.
                    _logger.LogWarning("reaches invalid line.");
                    Interlocked.Decrement(ref _connection.Counter.ReceivedMessages);

                    // try to get \n.
                    var position = buffer.PositionOf((byte) '\n');
                    while (position == null)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        result = await reader.ReadAsync().ConfigureAwait(false);
                        if (result.IsCompleted || result.IsCanceled)
                            return;
                        buffer = result.Buffer;
                        position = buffer.PositionOf((byte) '\n');
                    }

                    reader.AdvanceTo(buffer.GetPosition(1, position.Value));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occured during read loop.");
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
    // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]
    private (string subject, int sid, int payloadLength, string? replyTo) ParseMessageHeader(
        ReadOnlySpan<byte> msgHeader)
    {
        msgHeader = msgHeader.Slice(4);
        Split(msgHeader, out var subjectBytes, out msgHeader);
        Split(msgHeader, out var sidBytes, out msgHeader);
        Split(msgHeader, out var replyToOrSizeBytes, out msgHeader);

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

    private (string subject, int sid, int payloadLength, string? replyTo) ParseMessageHeader(
        in ReadOnlySequence<byte> msgHeader)
    {
        if (msgHeader.IsSingleSegment)
        {
            return ParseMessageHeader(msgHeader.FirstSpan);
        }

        // header parsing use Slice frequently so ReadOnlySequence is high cost, should use Span.
        // msgheader is not too long, ok to use stackalloc in most cases.
        const int maxAlloc = 256;
        var msgHeaderLength = (int) msgHeader.Length;
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
    private (string subject, int sid, string? replyTo, int headersLength, int totalLength) ParseHMessageHeader(
        ReadOnlySpan<byte> msgHeader)
    {
        // 'HMSG' literal
        Split(msgHeader, out _, out msgHeader);

        Split(msgHeader, out var subjectBytes, out msgHeader);
        Split(msgHeader, out var sidBytes, out msgHeader);
        Split(msgHeader, out var replyToOrHeaderLenBytes, out msgHeader);
        Split(msgHeader, out var headerLenOrTotalLenBytes, out msgHeader);

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

    private (string subject, int sid, string? replyTo, int headersLength, int totalLength) ParseHMessageHeader(
        in ReadOnlySequence<byte> msgHeader)
    {
        if (msgHeader.IsSingleSegment)
        {
            return ParseHMessageHeader(msgHeader.FirstSpan);
        }

        // header parsing use Slice frequently so ReadOnlySequence is high cost, should use Span.
        // msgheader is not too long, ok to use stackalloc in most cases.
        const int maxAlloc = 256;
        var msgHeaderLength = (int) msgHeader.Length;
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
        public const int Info = 1330007625; // Encoding.ASCII.GetBytes("INFO") |> MemoryMarshal.Read<int>
        public const int Msg = 541545293; // Encoding.ASCII.GetBytes("MSG ") |> MemoryMarshal.Read<int>
        public const int HMsg = 1196641608; // Encoding.ASCII.GetBytes("HMSG") |> MemoryMarshal.Read<int>
        public const int Ping = 1196312912; // Encoding.ASCII.GetBytes("PING") |> MemoryMarshal.Read<int>
        public const int Pong = 1196314448; // Encoding.ASCII.GetBytes("PONG") |> MemoryMarshal.Read<int>
        public const int Ok = 223039275; // Encoding.ASCII.GetBytes("+OK\r") |> MemoryMarshal.Read<int>
        public const int Error = 1381123373; // Encoding.ASCII.GetBytes("-ERR") |> MemoryMarshal.Read<int>
    }
}
