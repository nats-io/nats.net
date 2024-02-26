{ pkgs ? import <nixpkgs> {} }:
pkgs.mkShell {
  name = "Nats Sandbox Shell";
  buildInputs = with pkgs; [
    natscli
    nats-server
    nats-top
    parallel
    man
  ];

  shellHook = ''
    echo "Nats Sandbox shell started."
    export RESPONSE='{ "Body": "Message {{Count}} @ {{Time}}" }'
    alias server="nats-server --net localhost --js"
    alias respond='nats reply bar.> "$RESPONSE"'
    alias create-stream='nats stream create s1 --subjects="bar.>" --storage=memory --replicas=1 --defaults'

    alias aspire="dotnet run --project ./Example.Aspire.AppHost/Example.Aspire.AppHost.csproj"
    alias pub="dotnet run --project ./Example.Core.PublishModel/Example.Core.PublishModel.csproj"
    alias consumer="dotnet run --project ./Example.JetStream.PullConsumer/Example.JetStream.PullConsumer.csproj -- "

    echo "server - run nats server with jetstream"
    echo "create-stream - run stream named s1 on subject bar.>"
    echo "respond - run a responder on subject bar.>"
    echo "pub - run Example.Core.PublishModel"
    echo "consumer - run Example.JetStream.PullConsumer"
  '';
}
