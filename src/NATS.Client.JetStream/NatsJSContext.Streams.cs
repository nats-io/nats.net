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
            subject: $"{Opts.Prefix}.STREAM.CREATE.{request.Name}",
            request,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async ValueTask<bool> DeleteStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamMsgDeleteResponse>(
            subject: $"{Opts.Prefix}.STREAM.DELETE.{stream}",
            request: null,
            cancellationToken);
        return response.Success;
    }

    public async ValueTask<NatsJSStream> GetStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamInfoResponse>(
            subject: $"{Opts.Prefix}.STREAM.INFO.{stream}",
            request: null,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async ValueTask<NatsJSStream> UpdateStreamAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamUpdateResponse>(
            subject: $"{Opts.Prefix}.STREAM.UPDATE.{request.Name}",
            request: request,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async IAsyncEnumerable<NatsJSStream> ListStreamsAsync(
        string? subject = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<StreamListRequest, StreamListResponse>(
            subject: $"{Opts.Prefix}.STREAM.LIST",
            request: new StreamListRequest { Offset = 0, Subject = subject! },
            cancellationToken);
        foreach (var stream in response.Streams)
            yield return new NatsJSStream(this, stream);
    }
}
