using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
    public ValueTask<NatsJSStream> CreateStreamAsync(string stream, params string[] subjects) =>
        CreateStreamAsync(new StreamCreateRequest { Name = stream, Subjects = subjects });

    public async ValueTask<NatsJSStream> CreateStreamAsync(
        StreamConfiguration request,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<StreamConfiguration, StreamInfo>(
            subject: $"{Options.Prefix}.STREAM.CREATE.{request.Name}",
            request,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async ValueTask<bool> DeleteStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamMsgDeleteResponse>(
            subject: $"{Options.Prefix}.STREAM.DELETE.{stream}",
            request: null,
            cancellationToken);
        return response.Success;
    }

    public async ValueTask<NatsJSStream> GetStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamInfoResponse>(
            subject: $"{Options.Prefix}.STREAM.INFO.{stream}",
            request: null,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async ValueTask<NatsJSStream> UpdateStreamAsync(
        StreamUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamUpdateResponse>(
            subject: $"{Options.Prefix}.STREAM.UPDATE.{request.Name}",
            request: request,
            cancellationToken);
        return new NatsJSStream(this, response);
    }

    public async IAsyncEnumerable<NatsJSStream> ListStreamsAsync(
        StreamListRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await JSRequestResponseAsync<object, StreamListResponse>(
            subject: $"{Options.Prefix}.STREAM.LIST",
            request: request,
            cancellationToken);
        foreach (var stream in response.Streams)
        {
            yield return new NatsJSStream(this, stream);
        }
    }
}
