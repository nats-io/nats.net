using NATS.Client.CheckAbiTransientLib;
using NATS.Client.Core;

// This project references an older NATS.Net NuGet (same as TransientLib).
// It serves as a control: all types should resolve to NATS.Client.Core.
var coreVersion = typeof(INatsSerialize<>).Assembly.GetName().Version;
Console.WriteLine($"=== CheckAbiOld: running against NATS.Client.Core v{coreVersion} NuGet ===");
Console.WriteLine();

Console.WriteLine($"Types as seen by this project:");
Console.WriteLine($"  INatsSerialize<>: {typeof(INatsSerialize<>).Assembly.GetName().Name}");
Console.WriteLine($"  INatsDeserialize<>: {typeof(INatsDeserialize<>).Assembly.GetName().Name}");
Console.WriteLine($"  INatsSerializer<>: {typeof(INatsSerializer<>).Assembly.GetName().Name}");
Console.WriteLine($"  INatsSerializerRegistry: {typeof(INatsSerializerRegistry).Assembly.GetName().Name}");
Console.WriteLine();

Console.WriteLine($"Types as seen by TransientLib:");
Console.WriteLine($"  INatsSerialize<> (transient): {MySerializer.GetSerializerInterfaceAssembly()}");
Console.WriteLine($"  INatsSerialize<> version (transient): {MySerializer.GetSerializerInterfaceAssemblyVersion()}");
Console.WriteLine();

Console.WriteLine("Assembly versions at runtime:");
Console.WriteLine($"  INatsSerialize<> assembly version: {typeof(INatsSerialize<>).Assembly.GetName().Version}");
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a.GetName().Name?.Contains("NATS") == true)
    .OrderBy(a => a.GetName().Name))
{
    Console.WriteLine($"  {asm.GetName().Name} v{asm.GetName().Version} [{asm.Location}]");
}
