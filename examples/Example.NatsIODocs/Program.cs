var example = args.FirstOrDefault() ?? "getting-started-publish";
var timeout = TimeSpan.FromSeconds(5);

var task = example switch
{
    "getting-started-publish" => GettingStartedPublish.RunAsync(),
    "getting-started-subscribe" => GettingStartedSubscribe.RunAsync(),
    "basics-publish" => BasicsPublish.RunAsync(),
    "basics-subscribe" => BasicsSubscribe.RunAsync(),
    _ => throw new Exception($"Unknown example: {example}"),
};

if (await Task.WhenAny(task, Task.Delay(timeout)) == task)
{
    await task;
    Console.WriteLine($"[harness] '{example}' completed");
}
else
{
    Console.WriteLine($"[harness] '{example}' still running after {timeout.TotalSeconds}s, exiting");
}
