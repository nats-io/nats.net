# Publish-Subscribe Pattern

NATS implements a publish-subscribe message distribution model for one-to-many communication.
A publisher sends a message on a subject and any active subscriber listening on that subject
receives the message.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Core/PubSubPage.cs#pubsub)]

You can run multiple subscribers to the same subject, and each subscriber will receive a copy of the message.
At the same time, you can have multiple publishers sending messages to the same subject.
This is a powerful feature of NATS that enables many messaging patterns.
