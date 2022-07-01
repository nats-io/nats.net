# BlazorWasm

Blazor WebAssembly demo with NATS request/reply

## Pre-requisites

Start `nats-server` with a WebSocket listener on port 4280

**With nats-server**

```bash
nats-server -c nats-server.conf
```

**With docker**


```bash
docker run --rm \
    --name nats-server \
    -p 4222:4222 \
    -p 4280:4280 \
    -v "$(pwd)/nats-server.conf:/etc/nats/nats-server.conf" \
    nats
```

## Run the Project

```bash
dotnet run --project Server
```

Navigate to http://localhost:5000/fetchdata
