using System.Runtime.InteropServices;

using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.JavaScript;
using Aspire.Hosting.Publishing;

const int ApiPort = 7321;
const int RedisPort = 7322;
const int DashboardPort = 7323;
const int DashboardOtlpPort = 7324;
const string ContainerWikiRoot = "/data";
const string ContainerWikiBackupKeyDirectory = "/run/wiki-backup";

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

string[] agents = ["FactFinder", "ThreadWeaver", "Reviewer", "Editor", "Librarian", "Pleb"];

IResourceBuilder<ParameterResource> rootDirectory = builder
    .AddParameter("WikiRootDirectory");
IResourceBuilder<ParameterResource> apiKey = builder
    .AddParameter("AnthropicApiKey", true);

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

// An optional private repository the wiki's history is pushed to over SSH, and the folder on the host holding its
// deploy key and pinned host keys. Neither is secret: the key itself never leaves that folder.
IResourceBuilder<ParameterResource> wikiBackupRemote = builder
    .AddParameter("WikiBackupRemote");
IResourceBuilder<ParameterResource> wikiBackupKeyDirectory = builder
    .AddParameter("WikiBackupKeyDirectory");

// Deployed with Docker Compose to a single machine, where a Cloudflare tunnel forwards to the API on loopback. Nothing
// is published beyond loopback; the dashboard is reached over an SSH tunnel.
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(dashboard => dashboard
        .PublishAsDockerComposeService((_, service) =>
        {
            service.Ports = [$"127.0.0.1:{DashboardPort}:18888", $"127.0.0.1:{DashboardOtlpPort}:18889"];
            service.Restart = "unless-stopped";
        }));

IResourceBuilder<RedisResource> cache = builder
    .AddRedis("cache")
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Ports = [$"127.0.0.1:{RedisPort}:6379"];
        service.Restart = "unless-stopped";
    });

IResourceBuilder<ProjectResource> api = builder
    .AddProject<Projects.KnowledgeBase_Agent>("knowledgebase-agent")
    .WithReference(cache)
    .WaitFor(cache)
    .WithEnvironment("Wiki__RootDirectory", rootDirectory)
    .WithEnvironment("Authentication__TenantId", entraTenantId)
    .WithEnvironment("Authentication__ClientId", entraClientId)
    .WithEnvironment("Authentication__ClientSecret", entraClientSecret)
    .WithEnvironment("OpenAICompatible__Endpoint", openAICompatibleEndpoint)
    .WithEnvironment("OpenAICompatible__ApiKey", openAICompatibleApiKey)
    .WithEnvironment("Wiki__Backup__Remote", wikiBackupRemote)
    .WithEnvironment("Wiki__Backup__KeyDirectory", wikiBackupKeyDirectory);

foreach (string agent in agents)
{
    api
        .WithEnvironment($"{agent}__Provider", builder.AddParameter($"{agent}Provider"))
        .WithEnvironment($"{agent}__ApiKey", apiKey)
        .WithEnvironment($"{agent}__Model", builder.AddParameter($"{agent}Model"))
        .WithEnvironment($"{agent}__EnableThinking", builder.AddParameter($"{agent}EnableThinking"));
}

// The instructions and wiki rules are markdown files in the private configuration submodule. A deployment builds them
// into the image; a local run reads them from the submodule so edits apply when the API restarts. Without access to
// the submodule, the scaffold command copies bare templates in its place.
if (builder.ExecutionContext.IsRunMode)
{
    string repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
    string configDirectory = Path.Combine(repositoryRoot, "config");
    api
        .WithEnvironment("ConfigDirectory", configDirectory)
        .WithScaffoldConfigCommand(Path.Combine(repositoryRoot, "config.template"), configDirectory);
}

// The API shares the host's network so it reaches a model server listening on the host's loopback (such as
// llama-server) without opening that server or the firewall to containers. Redis and the dashboard are therefore
// addressed through their loopback ports, and the wiki root directory on the host is mounted into the container.
api.PublishAsDockerComposeService((resource, service) =>
{
    service.NetworkMode = "host";
    service.Networks.Clear();
    service.Expose.Clear();
    service.Restart = "unless-stopped";
    service.Volumes.Add(new Volume
    {
        Name = "wiki",
        Type = "bind",
        Source = rootDirectory.AsEnvironmentPlaceholder(resource),
        Target = ContainerWikiRoot,
    });

    service.Environment.Remove("HTTP_PORTS");
    service.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{ApiPort}";
    service.Environment["Wiki__RootDirectory"] = ContainerWikiRoot;
    service.Environment["ConnectionStrings__cache"] =
        $"127.0.0.1:{RedisPort},password={cache.Resource.PasswordParameter!.AsEnvironmentPlaceholder(resource)}";
    service.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://127.0.0.1:{DashboardOtlpPort}";

    // The deploy key folder is mounted read-only, so nothing in the container can change the key or the pinned host
    // keys. Compose refuses a bind mount whose folder does not exist, and backup is optional, so the mount is only
    // added when a folder is configured (aspire deploy reads the parameters from its own configuration).
    service.Environment.Remove("Wiki__Backup__KeyDirectory");
    if (!string.IsNullOrWhiteSpace(builder.Configuration[$"Parameters:{wikiBackupKeyDirectory.Resource.Name}"]))
    {
        service.Volumes.Add(new Volume
        {
            Name = "wiki-backup-key",
            Type = "bind",
            Source = wikiBackupKeyDirectory.AsEnvironmentPlaceholder(resource),
            Target = ContainerWikiBackupKeyDirectory,
            ReadOnly = true,
        });
        service.Environment["Wiki__Backup__KeyDirectory"] = ContainerWikiBackupKeyDirectory;
    }
});

// A fixed port, so the local sign-in redirect address (http://localhost:5173/signin-oidc) can be registered in Entra.
IResourceBuilder<ViteAppResource> web = builder
    .AddViteApp("knowledgebase-web", "../KnowledgeBase.Web")
    .WithPnpm()
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

// The images are built on the machine that runs them (the deployment VM's runner), so they target its architecture
// rather than Aspire's default of linux/amd64, which cannot run on an ARM64 host. The API is experimental.
#pragma warning disable ASPIREPIPELINES003
ContainerTargetPlatform hostPlatform = RuntimeInformation.OSArchitecture == Architecture.Arm64
    ? ContainerTargetPlatform.LinuxArm64
    : ContainerTargetPlatform.LinuxAmd64;
api.WithContainerBuildOptions(context => context.TargetPlatform = hostPlatform);
web.WithContainerBuildOptions(context => context.TargetPlatform = hostPlatform);
#pragma warning restore ASPIREPIPELINES003

builder.Build().Run();
