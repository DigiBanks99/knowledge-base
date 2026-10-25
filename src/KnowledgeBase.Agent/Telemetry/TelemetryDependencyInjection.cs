using KnowledgeBase.Agent.Health;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KnowledgeBase.Agent.Telemetry;

internal static class TelemetryDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddTelemetry()
        {
            builder.ConfigureOpenTelemetry();

            return builder;
        }

        public WebApplicationBuilder ConfigureOpenTelemetry()
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation())
                .WithTracing(tracing => tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpoints.HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(HealthEndpoints.AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation());

            builder.AddOpenTelemetryExporters();

            return builder;
        }

        private WebApplicationBuilder AddOpenTelemetryExporters()
        {
            string? otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            bool useOtlpExporter = !string.IsNullOrWhiteSpace(otlpEndpoint);

            if (useOtlpExporter)
            {
                builder.Services.AddOpenTelemetry().UseOtlpExporter();
            }

            return builder;
        }
    }
}
