# Schema.Generation

Console App to generate Models for JetStream

Requires `node` (current LTS release is fine) for preparing JSON Schema

1. Copy relevant files from https://github.com/nats-io/jsm.go/tree/main/schema_source
    - `request` and `response` models have been edited to make use of `allOf` for cleaner inheritance
    - `response` models have `oneOf` with error types removed
    - in `definitions.json`: `allOf` references in 3 places have been simplified to enable cleaner model generation
2. Run `node prepare.js` to bundle the `schema_source` files into a single file in `schema`
3. Run `dotnet run` to generate models
4. Run `dotnet format ../../src/NATS.Client.JetStream` to format the generated models
