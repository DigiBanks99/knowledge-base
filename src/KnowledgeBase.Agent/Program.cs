using KnowledgeBase.Agent;
using KnowledgeBase.Agent.Anthropic;
using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Telemetry;
using KnowledgeBase.Agent.Wiki;
using KnowledgeBase.Agent.Wiki.Tools;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder
    .ConfigureAgent(KnownAgents.FactFinder)
        .WithTools(toolsBuilder => toolsBuilder.WithFactTools())
    .ConfigureAgent(KnownAgents.ThreadWeaver)
        .WithTools(toolsBuilder => toolsBuilder.WithFactTools())
    .ConfigureAgent(KnownAgents.Consolidator);

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