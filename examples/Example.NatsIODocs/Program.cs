var example = args.FirstOrDefault() ?? "getting-started-publish";

await (example switch
{
    "getting-started-publish" => GettingStartedPublish.RunAsync(),
    "getting-started-subscribe" => GettingStartedSubscribe.RunAsync(),
    "basics-publish" => BasicsPublish.RunAsync(),
    "basics-subscribe" => BasicsSubscribe.RunAsync(),
    _ => throw new Exception($"Unknown example: {example}"),
});
