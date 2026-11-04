using KnowledgeBase.Agent.Research.Queries;
using KnowledgeBase.Agent.Research.Triage;

using IResult = Microsoft.AspNetCore.Http.IResult;

namespace KnowledgeBase.Agent.Research.Endpoints;

internal static class GetResearchQuality
{
    extension(IEndpointRouteBuilder endpoints)
    {
        internal IEndpointRouteBuilder MapGetResearchQuality()
        {
            endpoints
                .MapGet("quality", Handle)
                .WithName(nameof(GetResearchQuality))
                .WithSummary("Get the research quality")
                .WithDescription("Returns, over the recent window, how often the Keeper's verdicts agree with the author's marks and how often the ThreadWeaver filed something from the research notes, labelled with the Keeper's model. A measure, not a health signal.")
                .Produces<ResearchQuality>()
                .Produces(StatusCodes.Status500InternalServerError);

            return endpoints;
        }
    }

    private static async Task<IResult> Handle(
        GetResearchQualityQueryHandler handler,
        CancellationToken cancellationToken) => await handler
        .Handle(new GetResearchQualityQuery(), cancellationToken)
        .Match(Results.Ok, error => Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError));
}
