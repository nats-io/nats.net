using System.ComponentModel.DataAnnotations;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class JSContext
{
    private readonly NatsConnection _nats;
    private readonly JSOptions _options;

    public JSContext(NatsConnection nats, JSOptions options)
    {
        _nats = nats;
        _options = options;
    }

    public async ValueTask<JSStream> CreateStream(Action<StreamCreateRequest> request)
    {
        var requestObj = new StreamCreateRequest();
        request(requestObj);

        Validator.ValidateObject(requestObj, new ValidationContext(requestObj));

        var response =
            await _nats.RequestAsync<StreamCreateRequest, StreamCreateResponse>(
                $"{_options.Prefix}.STREAM.CREATE.{requestObj.Name}",
                requestObj);

        // TODO: Better error handling
        if (response?.Data == null)
            throw new NatsJetStreamException("No response received");

        return new JSStream(response.Value.Data);
    }
}

public class NatsJetStreamException : NatsException
{
    public NatsJetStreamException(string message)
        : base(message)
    {
    }

    public NatsJetStreamException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

public record JSOptions
{
    public string Prefix { get; init; } = "$JS.API";
}

public class JSStream
{
    public JSStream(StreamCreateResponse response)
    {
        Response = response;
    }

    public StreamCreateResponse Response { get; }
}
