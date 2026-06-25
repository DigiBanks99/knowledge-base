using KnowledgeBase.Agent.Source.Endpoints;

namespace KnowledgeBase.Agent.Source;

internal static class SourceEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapSourceEndpoints()
        {
            endpoints
                .MapGroup("source")
                .WithTags("wiki", "source")
                .MapCaptureNoteEndpoint();

            return endpoints;
        }
    }
}
