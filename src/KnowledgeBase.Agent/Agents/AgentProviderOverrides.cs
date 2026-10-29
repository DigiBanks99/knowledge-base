using System.Text.Json;

namespace KnowledgeBase.Agent.Agents;

/// <summary>The providers chosen on the settings page, and the deployment's defaults they override.</summary>
internal interface IAgentProviderOverrides
{
    /// <summary>The provider the deployment configures for each known agent.</summary>
    IReadOnlyDictionary<string, ModelProvider> Defaults { get; }

    /// <summary>The agents whose provider was chosen on the settings page, with that provider.</summary>
    IReadOnlyDictionary<string, ModelProvider> Overrides { get; }

    /// <summary>Puts the agent on <paramref name="provider"/>, or back on its default when it is null.</summary>
    void Set(string agent, ModelProvider? provider);
}

/// <summary>
/// Layers the providers chosen on the settings page over the deployment's configuration as <c>{Agent}:Provider</c>, so
/// everything that reads an agent's provider (its chat client, the model server's status signal, research's token
/// counter) sees a choice the moment it is made. The choices are kept in a file in the root directory, so they survive
/// a restart.
/// </summary>
internal sealed class AgentProviderOverrides(string path, IReadOnlyDictionary<string, ModelProvider> defaults)
    : ConfigurationProvider, IConfigurationSource, IAgentProviderOverrides
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Lock _lock = new();
    private Dictionary<string, ModelProvider> _overrides = [];

    public IReadOnlyDictionary<string, ModelProvider> Defaults { get; } = defaults;

    public IReadOnlyDictionary<string, ModelProvider> Overrides
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, ModelProvider>(_overrides);
            }
        }
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override void Load()
    {
        lock (_lock)
        {
            _overrides = Read();
            Data = ToData(_overrides);
        }
    }

    public void Set(string agent, ModelProvider? provider)
    {
        lock (_lock)
        {
            Dictionary<string, ModelProvider> overrides = new(_overrides, StringComparer.OrdinalIgnoreCase);
            if (provider is { } chosen)
            {
                overrides[agent] = chosen;
            }
            else
            {
                overrides.Remove(agent);
            }

            Write(overrides);
            _overrides = overrides;
            Data = ToData(overrides);
        }

        OnReload();
    }

    /// <summary>
    /// The choices in the file. An agent that no longer exists, or a provider this version does not know, is skipped
    /// rather than failing the start: the agent then runs on its default.
    /// </summary>
    private Dictionary<string, ModelProvider> Read()
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, ModelProvider>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The agent providers in {path} are not valid JSON. Fix or delete the file.", ex);
        }

        Dictionary<string, ModelProvider> overrides = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in stored ?? [])
        {
            string? agent = KnownAgents.All.FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
            if (agent is not null
                && Enum.TryParse(value, ignoreCase: true, out ModelProvider provider)
                && Enum.IsDefined(provider))
            {
                overrides[agent] = provider;
            }
        }

        return overrides;
    }

    /// <summary>Writes beside the file and moves it into place, so a crash never leaves half a file to start from.</summary>
    private void Write(Dictionary<string, ModelProvider> overrides)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = $"{path}.tmp";
        Dictionary<string, string> stored = overrides
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => entry.Value.ToString());
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored, Json));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static Dictionary<string, string?> ToData(Dictionary<string, ModelProvider> overrides) => overrides.ToDictionary(
        entry => $"{entry.Key}:{nameof(AgentModelOptions.Provider)}",
        entry => (string?)entry.Value.ToString(),
        StringComparer.OrdinalIgnoreCase);
}
