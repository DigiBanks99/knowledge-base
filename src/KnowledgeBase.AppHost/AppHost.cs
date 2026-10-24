IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> rootDirectory = builder
    .AddParameter("WikiRootDirectory");
IResourceBuilder<ParameterResource> apiKey = builder
    .AddParameter("AnthropicApiKey", true);
IResourceBuilder<ParameterResource> sharedInstructions = builder
    .AddParameter("SharedInstructions", true);
IResourceBuilder<ParameterResource> wikiRules = builder
    .AddParameter("WikiRules", true);
IResourceBuilder<ParameterResource> editorModel = builder
    .AddParameter("EditorModel");
IResourceBuilder<ParameterResource> editorInstructions = builder
    .AddParameter("EditorInstructions");
IResourceBuilder<ParameterResource> factFinderModel = builder
    .AddParameter("FactFinderModel");
IResourceBuilder<ParameterResource> factFinderInstructions = builder
    .AddParameter("FactFinderInstructions");
IResourceBuilder<ParameterResource> threadWeaverModel = builder
    .AddParameter("ThreadWeaverModel");
IResourceBuilder<ParameterResource> threadWeaverInstructions = builder
    .AddParameter("ThreadWeaverInstructions");
IResourceBuilder<ParameterResource> reviewerModel = builder
    .AddParameter("ReviewerModel");
IResourceBuilder<ParameterResource> reviewerInstructions = builder
    .AddParameter("ReviewerInstructions");

builder
    .AddProject<Projects.KnowledgeBase_Agent>("knowledgebase-agent")
    .WithEnvironment("Wiki__RootDirectory", rootDirectory)
    .WithEnvironment("SharedInstructions", sharedInstructions)
    .WithEnvironment("WikiRules", wikiRules)
    .WithEnvironment("Editor__ApiKey", apiKey)
    .WithEnvironment("Editor__Model", editorModel)
    .WithEnvironment("Editor__Instructions", editorInstructions)
    .WithEnvironment("FactFinder__ApiKey", apiKey)
    .WithEnvironment("FactFinder__Model", factFinderModel)
    .WithEnvironment("FactFinder__Instructions", factFinderInstructions)
    .WithEnvironment("ThreadWeaver__ApiKey", apiKey)
    .WithEnvironment("ThreadWeaver__Model", threadWeaverModel)
    .WithEnvironment("ThreadWeaver__Instructions", threadWeaverInstructions)
    .WithEnvironment("Reviewer__ApiKey", apiKey)
    .WithEnvironment("Reviewer__Model", reviewerModel)
    .WithEnvironment("Reviewer__Instructions", reviewerInstructions);

builder.Build().Run();