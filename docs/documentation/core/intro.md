# Core NATS

[Core NATS](https://docs.nats.io/nats-concepts/core-nats) is the base set of functionalities and qualities of service
offered by a NATS service infrastructure. Core NATS is the foundation for JetStream and other services. For the sake
of explanation, in a simplified sense you can think of Core NATS as the
[wire protocol](https://docs.nats.io/reference/reference-protocols/nats-protocol) defining a simple but powerful
pub/sub functionality and the concept of [Subject-Based Messaging](https://docs.nats.io/nats-concepts/subjects).

[Publish-Subscribe](pub-sub.md) is the message distribution model for one-to-many communication.

[Request-Reply](req-rep.md) is a common pattern in modern distributed systems. A request is sent, and the application
either waits on the response with a certain timeout, or receives a response asynchronously.

[Queue Groups](queue.md) enables the 1:N fan-out pattern of messaging ensuring that any message sent by a publisher,
reaches all subscribers that have registered.
