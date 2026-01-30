using System.Buffers;
using NATS.Client.CheckAbiTransientLib;
using NATS.Client.Core;

// This project simulates the real-world ABI compatibility scenario:
// - AbiCheck references NATS.Net 2.7.0 (local source with type forwarders)
// - TransientLib was compiled against NATS.Net 2.6.0 (types in NATS.Client.Core)
// - At runtime, type forwarding should allow TransientLib to work with 2.7.0
Console.WriteLine("ABI Compatibility Check (Transient Dependency Simulation)");
Console.WriteLine("==========================================================");
Console.WriteLine();

// Check where THIS project sees the types (should be Abstractions since we use 2.7.0)
Console.WriteLine("Types as seen by this project (compiled against 2.7.0):");
Console.WriteLine($"  INatsSerialize<> assembly: {typeof(INatsSerialize<>).Assembly.GetName().Name}");
Console.WriteLine($"  INatsDeserialize<> assembly: {typeof(INatsDeserialize<>).Assembly.GetName().Name}");
Console.WriteLine($"  INatsSerializer<> assembly: {typeof(INatsSerializer<>).Assembly.GetName().Name}");
Console.WriteLine($"  INatsSerializerRegistry assembly: {typeof(INatsSerializerRegistry).Assembly.GetName().Name}");
Console.WriteLine();

// Check where TransientLib sees the types (compiled against 2.6.0, but should resolve via forwarding)
Console.WriteLine("Types as seen by TransientLib (compiled against 2.6.0):");
Console.WriteLine($"  INatsSerialize<> assembly: {MySerializer.GetSerializerInterfaceAssembly()}");
Console.WriteLine();

// Use the serializer from TransientLib (compiled against 2.6.0)
Console.WriteLine("Testing TransientLib.MySerializer (compiled against 2.6.0):");
var serializer = new MySerializer();
var buffer = new ArrayBufferWriter<byte>();
serializer.Serialize(buffer, "hello from transient dependency");
Console.WriteLine($"  Serialized to {buffer.WrittenCount} bytes");

var deserialized = serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray()));
Console.WriteLine($"  Deserialized: '{deserialized}'");

Console.WriteLine();
Console.WriteLine("SUCCESS: ABI compatibility verified with transient dependency!");
