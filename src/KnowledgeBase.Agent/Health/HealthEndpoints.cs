using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Health;

internal static class HealthEndpoints
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    extension(IEndpointRouteBuilder routes)
    {
        internal IEndpointRouteBuilder MapHealthEndpoints()
        {
            routes.MapHealthChecks(HealthEndpointPath);

            routes.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
            return routes;
        }
    }
}