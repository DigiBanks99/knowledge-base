using Microsoft.Extensions.Logging;

namespace KnowledgeBase.Agent.Diagnostics;

internal static class LogsDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Keeps recent log entries in memory for the Logs page. Registered before the host is built, so it sees the
        /// messages logged while the app starts.
        /// </summary>
        public WebApplicationBuilder AddLogs()
        {
            builder.Services
                .AddSingleton(_ => new LogBuffer(LogBuffer.DefaultCapacity))
                .AddSingleton<ILoggerProvider, BufferedLoggerProvider>();

            return builder;
        }
    }
}
