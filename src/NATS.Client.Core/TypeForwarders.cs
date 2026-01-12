using System.Runtime.CompilerServices;
using NATS.Client.Core;

[assembly: TypeForwardedTo(typeof(INatsSerializer<>))]
[assembly: TypeForwardedTo(typeof(INatsSerialize<>))]
[assembly: TypeForwardedTo(typeof(INatsDeserialize<>))]
[assembly: TypeForwardedTo(typeof(INatsSerializerRegistry))]
