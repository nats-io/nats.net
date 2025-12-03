using Microsoft.Extensions.Logging;

namespace NATS.Client.JetStream;

public static class NatsJSLogEvents
{
    public static readonly EventId Config = new(2001, nameof(Config));
    public static readonly EventId PublishNoResponseRetry = new(2002, nameof(PublishNoResponseRetry));
    public static readonly EventId IdleTimeout = new(2003, nameof(IdleTimeout));
    public static readonly EventId Expired = new(2004, nameof(Expired));
    public static readonly EventId ProtocolMessage = new(2005, nameof(ProtocolMessage));
    public static readonly EventId Headers = new(2006, nameof(Headers));
    public static readonly EventId PendingCount = new(2007, nameof(PendingCount));
    public static readonly EventId MessageProperty = new(2008, nameof(MessageProperty));
    public static readonly EventId PullRequest = new(2009, nameof(PullRequest));
    public static readonly EventId NewConsumer = new(2010, nameof(NewConsumer));
    public static readonly EventId DeleteOldDeliverySubject = new(2011, nameof(DeleteOldDeliverySubject));
    public static readonly EventId NewDeliverySubject = new(2012, nameof(NewDeliverySubject));
    public static readonly EventId NewConsumerCreated = new(2013, nameof(NewConsumerCreated));
    public static readonly EventId Stopping = new(2014, nameof(Stopping));
    public static readonly EventId LeadershipChange = new(2015, nameof(LeadershipChange));
    public static readonly EventId Connection = new(2016, nameof(Connection));
    public static readonly EventId RecreateConsumer = new(2017, nameof(RecreateConsumer));
    public static readonly EventId Internal = new(2018, nameof(Internal));
    public static readonly EventId Retry = new(2019, nameof(Retry));
    public static readonly EventId NoResponders = new(2020, nameof(NoResponders));
    public static readonly EventId MessageSizeExceedsMaxBytes = new(2021, nameof(MessageSizeExceedsMaxBytes));
    public static readonly EventId PinIdMismatch = new(2022, nameof(PinIdMismatch));
}
