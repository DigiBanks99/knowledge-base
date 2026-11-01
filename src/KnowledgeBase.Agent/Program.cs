using System.Text.Json.Serialization;

using KnowledgeBase.Agent;
using KnowledgeBase.Agent.Agents;
using KnowledgeBase.Agent.Auth;
using KnowledgeBase.Agent.Common;
using KnowledgeBase.Agent.Facts.Tools;
using KnowledgeBase.Agent.Files.Tools;
using KnowledgeBase.Agent.Health;
using KnowledgeBase.Agent.Research.Tools;
using KnowledgeBase.Agent.Review.Tools;
using KnowledgeBase.Agent.Telemetry;
using KnowledgeBase.Agent.Wiki;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder
    .AddConfigFiles()
    .AddAgentProviders()
    .ConfigureAgent(KnownAgents.FactFinder)
        .WithTools(toolsBuilder => toolsBuilder.WithFactTools())
    .ConfigureAgent(KnownAgents.ThreadWeaver, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithFileTools())
    .ConfigureAgent(KnownAgents.Reviewer, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithReviewTools())
    .ConfigureAgent(KnownAgents.Editor, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithEditorTools())
    .ConfigureAgent(KnownAgents.Librarian, includeWikiRules: true)
        .WithTools(toolsBuilder => toolsBuilder.WithReadFileTools())
    .ConfigureAgent(KnownAgents.Pleb, roleOnly: true)
        .WithTools(toolsBuilder => toolsBuilder.WithPlebTools())
    .ConfigureAgent(KnownAgents.Clerk, roleOnly: true)
        .WithTools(toolsBuilder => toolsBuilder.WithClerkTools());

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder
    .AddRedisClientBuilder("cache")
    .WithOutputCache();

builder
    .AddWiki()
    .AddHealthChecks()
    .AddTelemetry();

builder.AddAuth();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseAuth();
app.MapAuthEndpoints();

RouteGroupBuilder api = app
    .UseWiki()
    .RequireSignedInUser();
api.MapAgentsEndpoints();
api.MapFallback(() => Results.NotFound());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseOutputCache()
    .UseFileServer();

// In production the built UI is served from wwwroot; client-side routes fall back to its index page.
app.MapFallbackToFile("index.html");

await app.RunAsync();
