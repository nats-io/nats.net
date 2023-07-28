using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSManageStreams
{
    private readonly NatsJSContext _context;

    public NatsJSManageStreams(NatsJSContext context) => _context = context;

    public ValueTask<StreamInfo> CreateAsync(string stream, params string[] subjects) =>
        CreateAsync(new StreamCreateRequest { Name = stream, Subjects = subjects });

    public ValueTask<StreamInfo> CreateAsync(
        StreamConfiguration request,
        CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<StreamConfiguration, StreamInfo>(
            subject: $"{_context.Options.Prefix}.STREAM.CREATE.{request.Name}",
            request,
            cancellationToken);

    public ValueTask<StreamMsgDeleteResponse> DeleteAsync(
        string stream,
        CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<object, StreamMsgDeleteResponse>(
            subject: $"{_context.Options.Prefix}.STREAM.DELETE.{stream}",
            request: null,
            cancellationToken);

    public ValueTask<StreamInfoResponse> GetAsync(
        string stream,
        CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<object, StreamInfoResponse>(
            subject: $"{_context.Options.Prefix}.STREAM.INFO.{stream}",
            request: null,
            cancellationToken);

    public ValueTask<StreamUpdateResponse> UpdateAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<object, StreamUpdateResponse>(
            subject: $"{_context.Options.Prefix}.STREAM.UPDATE.{request.Name}",
            request: request,
            cancellationToken);

    public ValueTask<StreamListResponse> ListAsync(
        StreamListRequest request,
        CancellationToken cancellationToken = default) =>
        _context.JSRequestResponseAsync<object, StreamListResponse>(
            subject: $"{_context.Options.Prefix}.STREAM.LIST",
            request: request,
            cancellationToken);
}
