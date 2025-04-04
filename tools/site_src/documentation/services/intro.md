# Services

[Services](https://docs.nats.io/using-nats/developer/services) is a protocol that provides first-class services support
for NATS clients and it's supported by NATS tooling. This services protocol is an agreement between clients and tooling and
doesn't require any special functionality from the NATS server or JetStream.

To be able to use Services you need to running the `nats-server`.

## Services Quick Start

[Download the latest](https://nats.io/download/) `nats-server` for your platform and run it:

```shell
$ nats-server
```

Install [NATS.Net](https://www.nuget.org/packages/NATS.Net) from Nuget.

Before we can do anything, we need a Services context:

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Services/IntroPage.cs#svc)]

Let's create our first service:

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Services/IntroPage.cs#add)]

Now that we have a service in our stream, let's see its status using the [NATS command
line client](https://github.com/nats-io/natscli) (make sure you have at least v0.1.1):

```shell
$ nats --version
0.1.4
```

```shell
$ nats micro info test
Service Information

      Service: test (Bw6eqhVYs3dbNzZecuuFOV)
  Description:
      Version: 1.0.0

Endpoints:

Statistics for 0 Endpoint(s):
```

Now we can add endpoints to our service:

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Services/IntroPage.cs#endpoint)]

We can also confirm that our endpoint is registered by using the NATS command line:

```shell
$ nats req divide42 2
11:34:03 Sending request on "divide42"
11:34:03 Received with rtt 9.5823ms
21

$ nats micro stats test
╭──────────────────────────────────────────────────────────────────────────────────────────────────────╮
│                                        test Service Statistics                                       │
├────────────────────────┬──────────┬──────────┬─────────────┬────────┬─────────────────┬──────────────┤
│ ID                     │ Endpoint │ Requests │ Queue Group │ Errors │ Processing Time │ Average Time │
├────────────────────────┼──────────┼──────────┼─────────────┼────────┼─────────────────┼──────────────┤
│ RH6q9Y6qM8em8m6lG2yN34 │ divide42 │ 1        │ q           │ 0      │ 1ms             │ 1ms          │
├────────────────────────┼──────────┼──────────┼─────────────┼────────┼─────────────────┼──────────────┤
│                        │          │ 1        │             │ 0      │ 1MS             │ 1MS          │
╰────────────────────────┴──────────┴──────────┴─────────────┴────────┴─────────────────┴──────────────╯
```

## Groups

A group is a collection of endpoints. These are optional and can provide a logical association between endpoints
as well as an optional common subject prefix for all endpoints.

You can group your endpoints optionally in different [queue groups](https://docs.nats.io/nats-concepts/core-nats/queue):

[!code-csharp[](../../../tests/NATS.Net.DocsExamples/Services/IntroPage.cs#grp)]
