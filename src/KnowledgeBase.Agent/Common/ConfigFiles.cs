namespace KnowledgeBase.Agent.Common;

/// <summary>
/// Loads configuration values from markdown files, for long texts such as agent instructions that are awkward to pass
/// as environment variables. The file name without <c>.md</c> is the key, with <c>.</c> separating sections, so
/// <c>FactFinder.Instructions.md</c> sets <c>FactFinder:Instructions</c>.
/// </summary>
/// <remarks>
/// The files come from the private configuration repository, a submodule at <c>config/</c>. They are copied into the
/// build output (and so into the container image); a local run reads them from the submodule instead, through the
/// <c>ConfigDirectory</c> setting, so edits apply on the next restart without a rebuild.
/// </remarks>
internal static class ConfigFiles
{
    public const string ConfigDirectoryKey = "ConfigDirectory";

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddConfigFiles()
        {
            string directory = builder.Configuration.GetValue<string>(ConfigDirectoryKey)
                ?? Path.Combine(AppContext.BaseDirectory, "config");
            if (!Directory.Exists(directory))
            {
                return builder;
            }

            Dictionary<string, string?> values = Directory
                .EnumerateFiles(directory, "*.md")
                .Where(path => !Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    path => Path.GetFileNameWithoutExtension(path).Replace('.', ':'),
                    path => (string?)File.ReadAllText(path));
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }
    }
}
