using KnowledgeBase.Agent.Review.Endpoints;

namespace KnowledgeBase.Agent.Review;

internal static class ReviewEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapReviewEndpoints()
        {
            endpoints.MapGroup("review")
                .WithTags("wiki", "review")
                .MapListFindings()
                .MapListReports()
                .MapRequestReview();

            return endpoints;
        }
    }
}
