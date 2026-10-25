using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Status;

internal static class StatusDependencyInjection
{
    public const string StatusTag = "status";

    private static readonly TimeSpan CheckPeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ModelServerTimeout = TimeSpan.FromSeconds(5);

    public static bool IsStatusSignal(HealthCheckRegistration registration) => registration.Tags.Contains(StatusTag);

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddStatus()
        {
            builder.Services
                .AddSingleton<StatusMonitor>()
                .AddSingleton<IHealthCheckPublisher>(services => services.GetRequiredService<StatusMonitor>())
                .Configure<HealthCheckPublisherOptions>(options =>
                {
                    options.Delay = TimeSpan.FromSeconds(2);
                    options.Period = CheckPeriod;
                    options.Predicate = IsStatusSignal;
                })
                .AddHttpClient(ModelServerHealthCheck.HttpClientName, client => client.Timeout = ModelServerTimeout);

            builder.Services
                .AddHealthChecks()
                .AddStatusSignal<ModelServerHealthCheck>("Model server");

            return builder;
        }
    }

    extension(IHealthChecksBuilder healthChecks)
    {
        /// <summary>
        /// Adds a status signal: a health check that <c>/health</c> runs and the Status page shows. This is the one
        /// registration a new signal needs. A failing signal is degraded rather than unhealthy, so background
        /// machinery being down never fails <c>/health</c> (and with it deployments) for an API that still serves.
        /// </summary>
        public IHealthChecksBuilder AddStatusSignal<TCheck>(string name)
            where TCheck : class, IHealthCheck =>
            healthChecks.AddCheck<TCheck>(name, HealthStatus.Degraded, [StatusTag]);
    }
}
