namespace Aspire.Hosting;

internal static class ConfigScaffolding
{
    extension<T>(IResourceBuilder<T> builder) where T : IResource
    {
        /// <summary>
        /// Adds a "Scaffold config" command that copies the bare instruction templates into the config directory, so a
        /// clone without access to the private configuration repository can run. Existing files are never overwritten.
        /// </summary>
        public IResourceBuilder<T> WithScaffoldConfigCommand(string templateDirectory, string configDirectory) =>
            builder.WithCommand(
                name: "scaffold-config",
                displayName: "Scaffold config",
                executeCommand: _ => Task.FromResult(Scaffold(templateDirectory, configDirectory)),
                commandOptions: new CommandOptions
                {
                    Description = "Copy the bare instruction templates into config/ without overwriting existing files.",
                    IconName = "DocumentAdd",
                    // The API cannot start without instructions, so the command must work while it is down.
                    UpdateState = _ => ResourceCommandState.Enabled,
                });
    }

    private static ExecuteCommandResult Scaffold(string templateDirectory, string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);

        List<string> created = [];
        foreach (string template in Directory.EnumerateFiles(templateDirectory, "*.md"))
        {
            string target = Path.Combine(configDirectory, Path.GetFileName(template));
            if (File.Exists(target))
            {
                continue;
            }

            File.Copy(template, target);
            created.Add(Path.GetFileName(template));
        }

        return created.Count == 0
            ? CommandResults.Success("Every config file already exists; nothing was changed.")
            : CommandResults.Success($"Created {string.Join(", ", created)} in {configDirectory}. Restart the API to load them.");
    }
}