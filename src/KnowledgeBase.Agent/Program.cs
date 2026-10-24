using KnowledgeBase.Agent;
using KnowledgeBase.Agent.Anthropic;
using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Review.Tools;
using KnowledgeBase.Agent.Telemetry;
using KnowledgeBase.Agent.Wiki;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder
    .ConfigureAgent(KnownAgents.FactFinder)
        .WithTools(toolsBuilder => toolsBuilder.WithFactTools())
    .ConfigureAgent(KnownAgents.ThreadWeaver, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithFileTools())
    .ConfigureAgent(KnownAgents.Reviewer, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithReviewTools())
    .ConfigureAgent(KnownAgents.Editor, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithEditorTools());

builder
    .AddRedisClientBuilder("cache")
    .WithOutputCache();

builder
    .AddWiki()
    .AddHealthChecks()
    .AddTelemetry();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

WebApplication app = builder.Build();

app.UseExceptionHandler();

app.UseWiki();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseOutputCache()
    .UseFileServer();

await app.RunAsync();