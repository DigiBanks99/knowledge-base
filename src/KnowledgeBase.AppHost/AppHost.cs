using Aspire.Hosting.JavaScript;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string[] agents = ["FactFinder", "ThreadWeaver", "Reviewer", "Editor", "Librarian"];

IResourceBuilder<ParameterResource> rootDirectory = builder
    .AddParameter("WikiRootDirectory");
IResourceBuilder<ParameterResource> apiKey = builder
    .AddParameter("AnthropicApiKey", true);
IResourceBuilder<ParameterResource> sharedInstructions = builder
    .AddParameter("SharedInstructions", true);
IResourceBuilder<ParameterResource> wikiRules = builder
    .AddParameter("WikiRules", true);

// Sign-in with Entra ID (the app registration's directory, application id and a client secret).
IResourceBuilder<ParameterResource> entraTenantId = builder
    .AddParameter("EntraTenantId");
IResourceBuilder<ParameterResource> entraClientId = builder
    .AddParameter("EntraClientId");
IResourceBuilder<ParameterResource> entraClientSecret = builder
    .AddParameter("EntraClientSecret", secret: true);

// An OpenAI-compatible server (such as llama.cpp's llama-server) for agents whose provider is OpenAICompatible.
IResourceBuilder<ParameterResource> openAICompatibleEndpoint = builder
    .AddParameter("OpenAICompatibleEndpoint");
IResourceBuilder<ParameterResource> openAICompatibleApiKey = builder
    .AddParameter("OpenAICompatibleApiKey", secret: true);

IResourceBuilder<RedisResource> cache = builder
    .AddRedis("cache");

IResourceBuilder<ProjectResource> api = builder
    .AddProject<Projects.KnowledgeBase_Agent>("knowledgebase-agent")
    .WithReference(cache)
    .WaitFor(cache)
    .WithEnvironment("Wiki__RootDirectory", rootDirectory)
    .WithEnvironment("SharedInstructions", sharedInstructions)
    .WithEnvironment("WikiRules", wikiRules)
    .WithEnvironment("Authentication__TenantId", entraTenantId)
    .WithEnvironment("Authentication__ClientId", entraClientId)
    .WithEnvironment("Authentication__ClientSecret", entraClientSecret)
    .WithEnvironment("OpenAICompatible__Endpoint", openAICompatibleEndpoint)
    .WithEnvironment("OpenAICompatible__ApiKey", openAICompatibleApiKey);

foreach (string agent in agents)
{
    api
        .WithEnvironment($"{agent}__Provider", builder.AddParameter($"{agent}Provider"))
        .WithEnvironment($"{agent}__ApiKey", apiKey)
        .WithEnvironment($"{agent}__Model", builder.AddParameter($"{agent}Model"))
        .WithEnvironment($"{agent}__Instructions", builder.AddParameter($"{agent}Instructions"));
}

// A fixed port, so the local sign-in redirect address (http://localhost:5173/signin-oidc) can be registered in Entra.
IResourceBuilder<ViteAppResource> web = builder
    .AddViteApp("knowledgebase-web", "../KnowledgeBase.Web")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();