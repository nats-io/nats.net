var example = args.FirstOrDefault() ?? "basics-publish";
var timeout = TimeSpan.FromSeconds(10);

var task = example switch
{
    "getting-started-publish" => GettingStartedPublish.RunAsync(),
    "getting-started-subscribe" => GettingStartedSubscribe.RunAsync(),
    "basics-publish" => BasicsPublish.RunAsync(),
    "basics-subscribe" => BasicsSubscribe.RunAsync(),
    "subjects-single-wildcard" => SubjectsSingleWildcard.RunAsync(),
    "subjects-multi-wildcard" => SubjectsMultiWildcard.RunAsync(),
    "subjects-monitoring" => SubjectsMonitoring.RunAsync(),
    "queue-groups-basic" => QueueGroupsBasic.RunAsync(),
    "queue-groups-dynamic-scaling" => QueueGroupsDynamicScaling.RunAsync(),
    "queue-groups-request-reply" => QueueGroupsRequestReply.RunAsync(),
    "queue-groups-mixed-subscribers" => QueueGroupsMixedSubscribers.RunAsync(),
    "request-reply-basic" => RequestReplyBasic.RunAsync(),
    "request-reply-timeout" => RequestReplyTimeout.RunAsync(),
    "request-reply-multiple-responders" => RequestReplyMultipleResponders.RunAsync(),
    "request-reply-no-responders" => RequestReplyNoResponders.RunAsync(),
    "request-reply-headers" => RequestReplyHeaders.RunAsync(),
    "request-reply-calculator" => RequestReplyCalculator.RunAsync(),
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
