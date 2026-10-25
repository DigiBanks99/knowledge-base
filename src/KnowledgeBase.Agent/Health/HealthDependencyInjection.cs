using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Health;

internal static class HealthDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        internal WebApplicationBuilder AddHealthChecks()
        {
            builder.AddDefaultHealthChecks();

            return builder;
        }

        private WebApplicationBuilder AddDefaultHealthChecks()
        {
            builder.Services.AddHealthChecks()
                // Add a default liveness check to ensure app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

            return builder;
        }
    }
}
