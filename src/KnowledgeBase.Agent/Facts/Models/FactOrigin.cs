using System.Text.Json.Serialization;

namespace KnowledgeBase.Agent.Facts.Models;

/// <summary>
/// Where a fact document came from: a note the author wrote, or a research note of third-party claims gathered from
/// the web.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FactOrigin>))]
public enum FactOrigin
{
    Author,
    Research
}