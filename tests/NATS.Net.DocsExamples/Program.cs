#pragma warning disable SA1515
#pragma warning disable SA1512

if (args.Length > 0)
{
    if (args[0] == "demo-sub")
    {
        await new NATS.Net.DocsExamples.IndexPageSub().Run();
    }
    else if (args[0] == "demo-pub")
    {
        await new NATS.Net.DocsExamples.IndexPagePub().Run();
    }
}

await new NATS.Net.DocsExamples.IntroPage().Run();
await new NATS.Net.DocsExamples.Core.IntroPage().Run();
await new NATS.Net.DocsExamples.Core.PubSubPage().Run();
await new NATS.Net.DocsExamples.Core.QueuePage().Run();
await new NATS.Net.DocsExamples.Core.ReqRepPage().Run();
await new NATS.Net.DocsExamples.JetStream.IntroPage().Run();
await new NATS.Net.DocsExamples.JetStream.ManagingPage().Run();
await new NATS.Net.DocsExamples.JetStream.ConsumerPage().Run();
await new NATS.Net.DocsExamples.KeyValueStore.IntroPage().Run();
await new NATS.Net.DocsExamples.ObjectStore.IntroPage().Run();
await new NATS.Net.DocsExamples.Services.IntroPage().Run();
await new NATS.Net.DocsExamples.Advanced.SecurityPage().Run();
await new NATS.Net.DocsExamples.Advanced.SerializationPage().Run();

Console.WriteLine("Bye");
