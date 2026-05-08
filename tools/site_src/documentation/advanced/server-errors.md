# Server Errors

The NATS server sends `-ERR` protocol messages to a client when something goes wrong on the server side: a permission denial, an authentication problem, a client-level limit, and so on. The `ServerError` event on `NatsConnection` raises one of these messages every time the server emits one, so an application can observe them without scraping logs.

## Subscribing to the Event

Hook the event the same way as any other connection event. The raw error text is on `args.Error`:

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/ServerErrorsPage.cs#server-error)]

## Classifying the Error

`args.Kind` is parsed from the raw text and exposes the common server errors as a [`NatsServerErrorKind`](xref:NATS.Client.Core.NatsServerErrorKind) enum value. Anything the parser does not recognise stays as `NatsServerErrorKind.Unknown`; `args.Error` always carries the original text.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/ServerErrorsPage.cs#server-error-kind)]

The recognised kinds are:

- `PermissionsViolation` - subscribe or publish denied for the connected user
- `AuthorizationViolation` - authorization failure during connect
- `AuthenticationExpired` - user authentication expired
- `AuthenticationRevoked` - user authentication revoked
- `AccountAuthenticationExpired` - account authentication expired
- `StaleConnection` - server timed out a stalled connection
- `MaximumConnectionsExceeded` - server-wide connection limit reached
- `MaximumAccountConnectionsExceeded` - per-account connection limit reached
- `MaximumSubscriptionsExceeded` - per-connection subscription limit reached

The event fires for every `-ERR` line, including messages whose `Kind` is `Unknown`. Match on `args.Error` if the application needs to handle a server error not covered by the enum.
