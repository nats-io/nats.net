var builder = DistributedApplication.CreateBuilder(args);

// builder.AddProject<Projects.Example_Core_PublishModel>("Publish-Model");
// builder.AddProject<Projects.Example_Core_SubscribeModel>("Subscribe-Model");
// builder.AddProject<Projects.Example_JetStream_PullConsumer>("Pull-Consumer");
builder.AddResource(new NullResource("__none")).WithOtlpExporter();

builder.Build().Run();

internal class NullResource(string name) : Resource(name), IResourceWithEnvironment;
