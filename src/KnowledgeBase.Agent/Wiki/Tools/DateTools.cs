using System.ComponentModel;

namespace KnowledgeBase.Agent.Wiki.Tools;

internal sealed class DateTools(TimeProvider timeProvider)
{
    [Description("""Returns the current date and time in ISO 8601 format. Proactively use this to get the current date and time in a standard format.""")]
    public string GetCurrentDateTimeIso() => timeProvider.GetUtcNow().ToString("o");
}
