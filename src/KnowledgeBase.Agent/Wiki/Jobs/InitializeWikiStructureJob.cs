using KnowledgeBase.Agent.Wiki.Commands;

namespace KnowledgeBase.Agent.Wiki.Jobs;

internal sealed class InitializeWikiStructureJob(IServiceScopeFactory serviceScopeFactory)
 : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        InitializeWikiStructureCommandHandler commandHandler = scope.ServiceProvider
            .GetRequiredService<InitializeWikiStructureCommandHandler>();

        InitializeWikiStructureCommand command = new();
        Result<bool, Error> result = await commandHandler.Handle(command, stoppingToken);
        if (result.IsFailure)
        {
            throw new ApplicationException($"Failed to initialize wiki structure: {result.Error}");
        }
    }
}
