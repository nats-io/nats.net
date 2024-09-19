using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
    /// <summary>
    /// Creates a new stream if it doesn't exist or returns an existing stream with the same name.
    /// </summary>
    /// <param name="config">Stream configuration request to be sent to NATS JetStream server.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The stream name in <paramref name="config"/> is invalid.</exception>
    /// <exception cref="ArgumentNullException">The name in <paramref name="config"/> is <c>null</c>.</exception>
    public async ValueTask<INatsJSStream> CreateStreamAsync(
        StreamConfig config,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(config.Name, nameof(config.Name));

        // keep caller's config intact.
        config = config.ShallowCopy();

        // If we have a mirror and an external domain, convert to ext.APIPrefix.
        if (config.Mirror != null && !string.IsNullOrEmpty(config.Mirror.Domain))
        {
            config.Mirror = config.Mirror.ShallowCopy();
            ConvertDomain(config.Mirror);
        }

        // Check sources for the same.
        if (config.Sources != null && config.Sources.Count > 0)
        {
            ICollection<StreamSource>? sources = [];
            foreach (var ss in config.Sources)
            {
                if (!string.IsNullOrEmpty(ss.Domain))
                {
                    var remappedDomainSource = ss.ShallowCopy();
                    ConvertDomain(remappedDomainSource);
                    sources.Add(remappedDomainSource);
                }
                else
                {
                    sources.Add(ss);
                }
            }

            config.Sources = sources;
        }

        var response = await JSRequestResponseAsync<StreamConfig, StreamInfo>(
            subject: $"{Opts.Prefix}.STREAM.CREATE.{config.Name}",
            config,
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
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<bool> DeleteStreamAsync(
        string stream,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<object, StreamMsgDeleteResponse>(
            subject: $"{Opts.Prefix}.STREAM.DELETE.{stream}",
            request: null,
            cancellationToken);
        return response.Success;
    }

    /// <summary>
    /// Purges all of the (or filtered) data in a stream, leaves the stream.
    /// </summary>
    /// <param name="stream">Stream name to be purged.</param>
    /// <param name="request">Purge request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Purge response</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<StreamPurgeResponse> PurgeStreamAsync(
        string stream,
        StreamPurgeRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<StreamPurgeRequest, StreamPurgeResponse>(
            subject: $"{Opts.Prefix}.STREAM.PURGE.{stream}",
            request: request,
            cancellationToken);
        return response;
    }

    /// <summary>
    /// Deletes a message from a stream.
    /// </summary>
    /// <param name="stream">Stream name to delete message from.</param>
    /// <param name="request">Delete message request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Delete message response</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<StreamMsgDeleteResponse> DeleteMessageAsync(
        string stream,
        StreamMsgDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<StreamMsgDeleteRequest, StreamMsgDeleteResponse>(
            subject: $"{Opts.Prefix}.STREAM.MSG.DELETE.{stream}",
            request: request,
            cancellationToken);
        return response;
    }

    /// <summary>
    /// Get stream information from the server and creates a NATS JetStream stream object <see cref="NatsJSStream"/>.
    /// </summary>
    /// <param name="stream">Name of the stream to retrieve.</param>
    /// <param name="request">Stream info request options</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The NATS JetStream stream object which can be used to manage the stream.</returns>
    /// <exception cref="NatsJSException">There was an issue retrieving the response.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="ArgumentException">The <paramref name="stream"/> name is invalid.</exception>
    /// <exception cref="ArgumentNullException">The <paramref name="stream"/> name is <c>null</c>.</exception>
    public async ValueTask<INatsJSStream> GetStreamAsync(
        string stream,
        StreamInfoRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(stream);
        var response = await JSRequestResponseAsync<StreamInfoRequest, StreamInfoResponse>(
            subject: $"{Opts.Prefix}.STREAM.INFO.{stream}",
            request: request,
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
    /// <exception cref="ArgumentException">The stream name in <paramref name="request"/> is invalid.</exception>
    /// <exception cref="ArgumentNullException">The name in <paramref name="request"/> is <c>null</c>.</exception>
    public async ValueTask<NatsJSStream> UpdateStreamAsync(
        StreamConfig request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidStreamName(request.Name, nameof(request.Name));
        var response = await JSRequestResponseAsync<StreamConfig, StreamUpdateResponse>(
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
    public async IAsyncEnumerable<INatsJSStream> ListStreamsAsync(
        string? subject = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await JSRequestResponseAsync<StreamListRequest, StreamListResponse>(
                subject: $"{Opts.Prefix}.STREAM.LIST",
                request: new StreamListRequest
                {
                    Offset = offset,
                    Subject = subject,
                },
                cancellationToken);

            if (response.Streams.Count == 0)
                yield break;

            foreach (var stream in response.Streams)
                yield return new NatsJSStream(this, stream);

            offset += response.Streams.Count;
        }
    }

    /// <summary>
    /// List stream names.
    /// </summary>
    /// <param name="subject">Limit the list to streams matching this subject filter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>Async enumerable list of stream names to be used in a <c>await foreach</c> loop.</returns>
    public async IAsyncEnumerable<string> ListStreamNamesAsync(string? subject = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await JSRequestResponseAsync<StreamNamesRequest, StreamNamesResponse>(
                subject: $"{Opts.Prefix}.STREAM.NAMES",
                request: new StreamNamesRequest
                {
                    Subject = subject,
                    Offset = offset,
                },
                cancellationToken);

            if (response.Streams == null || response.Streams.Count == 0)
            {
                yield break;
            }

            foreach (var stream in response.Streams)
                yield return stream;

            offset += response.Streams.Count;
        }
    }
}
