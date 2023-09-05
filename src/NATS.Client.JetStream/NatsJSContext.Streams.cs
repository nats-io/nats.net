using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
    public ValueTask<NatsJSStream> CreateStreamAsync(string stream, string[] subjects, CancellationToken cancellationToken = default) =>
        CreateStreamAsync(new StreamCreateRequest { Name = stream, Subjects = subjects }, cancellationToken);

    public async ValueTask<NatsJSStream> CreateStreamAsync(
        StreamConfiguration request,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<StreamConfiguration, StreamInfo>(
            subject: $"{Opts.ApiPrefix}.STREAM.CREATE.{request.Name}",
            request,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async ValueTask<bool> DeleteStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamMsgDeleteResponse>(
            subject: $"{Opts.ApiPrefix}.STREAM.DELETE.{stream}",
            request: null,
            cancellationToken);
        return response.Success;
    }

    public async ValueTask<NatsJSStream> GetStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamInfoResponse>(
            subject: $"{Opts.ApiPrefix}.STREAM.INFO.{stream}",
            request: null,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async ValueTask<NatsJSStream> UpdateStreamAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamUpdateResponse>(
            subject: $"{Opts.ApiPrefix}.STREAM.UPDATE.{request.Name}",
            request: request,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public ValueTask<StreamListResponse> ListStreamsAsync(
        StreamListRequest request,
        CancellationToken cancellationToken = default) =>
        JSRequestResponseAsync<object, StreamListResponse>(
            subject: $"{Opts.ApiPrefix}.STREAM.LIST",
            request: request,
            cancellationToken);
}
