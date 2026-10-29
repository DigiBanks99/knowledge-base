namespace KnowledgeBase.Agent.Agents;

/// <summary>What each provider needs, and which model it runs, read from the live configuration.</summary>
internal static class AgentProviders
{
    public static ModelProvider[] All { get; } = Enum.GetValues<ModelProvider>();

    /// <summary>The provider <paramref name="agent"/> runs on now, including a choice made on the settings page.</summary>
    public static ModelProvider Current(IConfiguration configuration, string agent) =>
        Parse(configuration[$"{agent}:{nameof(AgentModelOptions.Provider)}"]);

    /// <summary>A configured provider name, or Anthropic (as <see cref="AgentModelOptions"/> defaults) when it is unset.</summary>
    public static ModelProvider Parse(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ModelProvider provider) && Enum.IsDefined(provider)
            ? provider
            : ModelProvider.Anthropic;

    public static string Model(IConfiguration configuration, ModelProvider provider) => provider switch
    {
        ModelProvider.Anthropic => AnthropicSettings(configuration).Model,
        ModelProvider.OpenAICompatible => OpenAICompatibleSettings(configuration).Model,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown model provider.")
    };

    /// <summary>
    /// Why agents cannot run on <paramref name="provider"/>, or null when they can. Each provider is configured once,
    /// for every agent on it.
    /// </summary>
    public static string? Problem(IConfiguration configuration, ModelProvider provider) => provider switch
    {
        ModelProvider.Anthropic when string.IsNullOrWhiteSpace(AnthropicSettings(configuration).ApiKey) =>
            $"The Anthropic provider requires '{AnthropicOptions.SectionName}:{nameof(AnthropicOptions.ApiKey)}'.",
        ModelProvider.OpenAICompatible when OpenAICompatibleSettings(configuration).Endpoint is null =>
            $"The OpenAICompatible provider requires '{OpenAICompatibleOptions.SectionName}:{nameof(OpenAICompatibleOptions.Endpoint)}'.",
        ModelProvider.Anthropic or ModelProvider.OpenAICompatible => null,
        _ => $"'{provider}' is not a model provider."
    };

    public static AnthropicOptions AnthropicSettings(IConfiguration configuration) => configuration
        .GetSection(AnthropicOptions.SectionName)
        .Get<AnthropicOptions>() ?? new AnthropicOptions();

    public static OpenAICompatibleOptions OpenAICompatibleSettings(IConfiguration configuration) => configuration
        .GetSection(OpenAICompatibleOptions.SectionName)
        .Get<OpenAICompatibleOptions>() ?? new OpenAICompatibleOptions();
}
