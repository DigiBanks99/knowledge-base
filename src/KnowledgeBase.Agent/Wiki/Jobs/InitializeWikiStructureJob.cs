using KnowledgeBase.Agent.Wiki.Commands;

namespace KnowledgeBase.Agent.Wiki;

internal sealed class InitializeWikiStructureJob(IServiceScopeFactory serviceScopeFactory)
 : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope();
        InitializeWikiStructureCommandHandler commandHandler = scope.ServiceProvider
            .GetRequiredService<InitializeWikiStructureCommandHandler>();

        InitializeWikiStructureCommand command = new();
        Result<bool, Error> result = commandHandler.Handle(command);
        if (result.IsFailure)
        {
            throw new ApplicationException($"Failed to initialize wiki structure: {result.Error}");
        }
    }
}