using KnowledgeBase.Agent.Review.Commands;
using KnowledgeBase.Agent.Review.Jobs;
using KnowledgeBase.Agent.Review.Queries;
using KnowledgeBase.Agent.Review.Tools;
using KnowledgeBase.Agent.Review.Watchers;

namespace KnowledgeBase.Agent.Review;

internal static class ReviewDependencyInjection
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddReview() => builder
            .AddHandlers()
            .AddJobs()
            .AddSettings()
            .AddTools()
            .AddWatchers();

        private WebApplicationBuilder AddJobs()
        {
            builder.Services
                .AddHostedService<ScheduledReviewJob>();
            return builder;
        }

        private WebApplicationBuilder AddSettings()
        {
            builder.Services
                .Configure<ReviewSettings>(builder.Configuration.GetSection(ReviewSettings.SectionName));
            return builder;
        }

        private WebApplicationBuilder AddHandlers()
        {
            builder.Services
                .AddSingleton<EditWikiCommandHandler>()
                .AddSingleton<ListFindingsQueryHandler>()
                .AddSingleton<RequestReviewWikiCommandHandler>()
                .AddSingleton<ReviewWikiCommandHandler>();
            return builder;
        }

        private WebApplicationBuilder AddTools()
        {
            builder.Services
                .AddSingleton<ReviewTools>();
            return builder;
        }

        private WebApplicationBuilder AddWatchers()
        {
            builder.Services
                .AddSingleton<OpenFindingsWatcher>()
                .AddSingleton<ReviewRequestWatcher>();
            return builder;
        }
    }
}