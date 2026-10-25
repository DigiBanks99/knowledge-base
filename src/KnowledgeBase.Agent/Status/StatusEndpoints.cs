namespace KnowledgeBase.Agent.Status;

internal static class StatusEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapStatusEndpoints()
        {
            endpoints
                .MapGet("status", GetStatus)
                .WithTags("status")
                .WithName(nameof(GetStatus))
                .WithSummary("Get the status")
                .WithDescription("Returns whether each piece of background machinery, such as the model server, is healthy.")
                .Produces<StatusReport>();

            return endpoints;
        }
    }

    private static Task<StatusReport> GetStatus(StatusMonitor monitor, CancellationToken cancellationToken) =>
        monitor.GetAsync(cancellationToken);
}
