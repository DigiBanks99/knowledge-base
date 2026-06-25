IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> rootDirectory = builder
    .AddParameter("WikiRootDirectory");
IResourceBuilder<ParameterResource> apiKey = builder
    .AddParameter("AnthropicApiKey", true);
IResourceBuilder<ParameterResource> contentProcessorModel = builder
    .AddParameter("AnthropicModel");
IResourceBuilder<ParameterResource> contentProcessorInstructions = builder
    .AddParameter("ContentProcessorInstructions");
IResourceBuilder<ParameterResource> threadWeaverModel = builder
    .AddParameter("ThreadWeaverModel");
IResourceBuilder<ParameterResource> threadWeaverInstructions = builder
    .AddParameter("ThreadWeaverInstructions");
IResourceBuilder<ParameterResource> consolidatorModel = builder
    .AddParameter("ConsolidatorModel");
IResourceBuilder<ParameterResource> consolidatorInstructions = builder
    .AddParameter("ConsolidatorInstructions");

IResourceBuilder<RedisResource> cache = builder.AddRedis("cache");

IResourceBuilder<ProjectResource> knowledgeBase = builder
    .AddProject<Projects.KnowledgeBase_Agent>("knowledgebase-agent")
    .WithReference(cache)
    .WaitFor(cache)
    .WithEnvironment("Wiki__RootDirectory", rootDirectory)
    .WithEnvironment("ContentProcessor__ApiKey", apiKey)
    .WithEnvironment("ContentProcessor__Model", contentProcessorModel)
    .WithEnvironment("ContentProcessor__Instructions", contentProcessorInstructions)
    .WithEnvironment("ThreadWeaver__ApiKey", apiKey)
    .WithEnvironment("ThreadWeaver__Model", threadWeaverModel)
    .WithEnvironment("ThreadWeaver__Instructions", threadWeaverInstructions)
    .WithEnvironment("Consolidator__ApiKey", apiKey)
    .WithEnvironment("Consolidator__Model", consolidatorModel)
    .WithEnvironment("Consolidator__Instructions", consolidatorInstructions);

builder.Build().Run();
