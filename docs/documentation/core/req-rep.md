# Request-Reply Pattern

Request-Reply is a common pattern in modern distributed systems.
A request is sent, and the application either waits on the response with a certain timeout,
or receives a response asynchronously.

Create a service that will be responding to requests:
[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Core/ReqRepPage.cs#sub)]

Reply to a request is asynchronously received using an _inbox_ subscription
behind the scenes:
[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Core/ReqRepPage.cs#reqrep)]
