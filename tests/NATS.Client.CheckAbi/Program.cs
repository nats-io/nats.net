using System.Buffers;
using NATS.Client.CheckAbiTransientLib;
using NATS.Client.Core;

// This project simulates the real-world ABI compatibility scenario:
// - AbiCheck references NATS.Net 2.7.0 (local source with type forwarders)
// - TransientLib was compiled against NATS.Net 2.6.0 (types in NATS.Client.Core)
// - At runtime, type forwarding should allow TransientLib to work with 2.7.0
var errors = new List<string>();

void Check(string name, string expected, string actual)
{
    Console.WriteLine($"  {name}: {actual}");
    if (actual != expected)
        errors.Add($"{name}: expected '{expected}', got '{actual}'");
}

Console.WriteLine("Types as seen by this project (compiled against 2.7.0):");
Check("INatsSerialize<>", "NATS.Client.Abstractions", typeof(INatsSerialize<>).Assembly.GetName().Name!);
Check("INatsDeserialize<>", "NATS.Client.Abstractions", typeof(INatsDeserialize<>).Assembly.GetName().Name!);
Check("INatsSerializer<>", "NATS.Client.Abstractions", typeof(INatsSerializer<>).Assembly.GetName().Name!);
Check("INatsSerializerRegistry", "NATS.Client.Abstractions", typeof(INatsSerializerRegistry).Assembly.GetName().Name!);
Check("INatsSerializeWithHeaders<>", "NATS.Client.Abstractions", typeof(INatsSerializeWithHeaders<>).Assembly.GetName().Name!);
Check("INatsDeserializeWithHeaders<>", "NATS.Client.Abstractions", typeof(INatsDeserializeWithHeaders<>).Assembly.GetName().Name!);
Console.WriteLine();

Console.WriteLine("Types as seen by TransientLib (compiled against 2.6.0):");
Check("INatsSerialize<> (transient)", "NATS.Client.Abstractions", MySerializer.GetSerializerInterfaceAssembly());
Console.WriteLine();

Console.WriteLine("Testing TransientLib.MySerializer (compiled against 2.6.0):");
var serializer = new MySerializer();
var buffer = new ArrayBufferWriter<byte>();
serializer.Serialize(buffer, "hello from transient dependency");
Console.WriteLine($"  Serialized to {buffer.WrittenCount} bytes");

var deserialized = serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray()));
Console.WriteLine($"  Deserialized: '{deserialized}'");
if (deserialized != "hello from transient dependency")
    errors.Add($"Round-trip failed: got '{deserialized}'");

Console.WriteLine();

if (errors.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {errors.Count} error(s):");
    foreach (var error in errors)
        Console.Error.WriteLine($"  {error}");
    return 1;
}

Console.WriteLine("SUCCESS: ABI compatibility verified with transient dependency!");
return 0;
