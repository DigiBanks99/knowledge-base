using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

internal static class LogsDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Keeps recent log entries in memory for the Logs page and writes every entry to the logs directory. Registered
        /// before the host is built, so it sees the messages logged while the app starts.
        /// </summary>
        public WebApplicationBuilder AddLogs()
        {
            builder.Services
                .AddSingleton(_ => new LogBuffer(LogBuffer.DefaultCapacity))
                .AddSingleton<LogFiles>()
                .AddSingleton<ILoggerProvider, BufferedLoggerProvider>();

            return builder;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Loads the most recent entries from the logs directory into memory, so the Logs page has history after a
        /// restart. Called once the wiki's directories exist.
        /// </summary>
        public WebApplication UseLogs()
        {
            app.Services
                .GetRequiredService<LogFiles>()
                .RestoreInto(
                    app.Services.GetRequiredService<LogBuffer>(),
                    LogBuffer.DefaultCapacity,
                    app.Services.GetRequiredService<ILogger<LogFiles>>());

            return app;
        }
    }
}
