using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
    /// <summary>
    /// Creates a new stream if it doesn't exist or returns an existing stream with the same name.
    /// </summary>
    /// <param name="stream">Name of the stream to create. (e.g. my_events)</param>
    /// <param name="subjects">List of subjects stream will persist messages from. (e.g. events.*)</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    public ValueTask<NatsJSStream> CreateStreamAsync(string stream, string[] subjects, CancellationToken cancellationToken = default) =>
        CreateStreamAsync(new StreamCreateRequest { Name = stream, Subjects = subjects }, cancellationToken);

    /// <summary>
    /// Creates a new stream if it doesn't exist or returns an existing stream with the same name.
    /// </summary>
    /// <param name="request">Stream configuration request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
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

    /// <summary>
    /// Deletes a stream.
    /// </summary>
    /// <param name="stream">Stream name to be deleted.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Whether delete was successful or not.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
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

    /// <summary>
    /// Get stream information from the server and creates a NATS JetStream stream object <see cref="NatsJSStream"/>.
    /// </summary>
    /// <param name="stream">Name of the stream to retrieve.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
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

    /// <summary>
    /// Update a NATS JetStream stream's properties.
    /// </summary>
    /// <param name="request">Stream update request object to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The updated NATS JetStream stream object.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
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

    /// <summary>
    /// Enumerates through the streams exists on the NATS JetStream server.
    /// </summary>
    /// <param name="subject">Limit the list to streams matching this subject filter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable of stream objects. Can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <remarks>
    /// Note that paging isn't implemented. You might receive only a partial list of streams if there are a lot of them.
    /// </remarks>
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
