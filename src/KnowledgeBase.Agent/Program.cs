using KnowledgeBase.Agent.Anthropic;
using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Notes;
using KnowledgeBase.Agent.Telemetry;
using KnowledgeBase.Agent.Wiki;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder
    .ConfigureAgent("ContentProcessor")
    .ConfigureAgent("ThreadWeaver")
    .ConfigureAgent("Consolidator");

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

app
    .MapNotesEndpoints()
    .MapHealthEndpoints();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseOutputCache();

app.UseFileServer();

await app.RunAsync();