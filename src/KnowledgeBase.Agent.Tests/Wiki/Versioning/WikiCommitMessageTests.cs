using KnowledgeBase.Agent.Runs.Models;
using KnowledgeBase.Agent.Wiki.Versioning;

namespace KnowledgeBase.Agent.Tests.Wiki.Versioning;

public sealed class WikiCommitMessageTests
{
    private static readonly AgentRun Run = new(
        "20261025-093000-threadweaver-0123456789",
        KnownAgents.ThreadWeaver,
        new AgentRunTrigger(AgentRunTriggerKind.Facts, "feedback-from-susan.json"),
        AgentRunStatus.Succeeded,
        new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 25, 9, 31, 0, TimeSpan.Zero),
        "Filed Susan's feedback under people/susan.md.\n\nAlso linked it from the design review page.",
        null,
        []);

    [Test]
    public void ARunSucceeded()
    {
        string message = WikiCommitMessage.For(Run);

        ItShouldNameTheAgentAndTheSummaryLineInTheSubject();
        ItShouldIdentifyTheRunInTheBody();

        void ItShouldNameTheAgentAndTheSummaryLineInTheSubject() =>
            message.Split('\n')[0].ShouldBe("ThreadWeaver: Filed Susan's feedback under people/susan.md.");

        void ItShouldIdentifyTheRunInTheBody() => message.Split('\n')[1..].ShouldBe(
        [
            "",
            "Run: 20261025-093000-threadweaver-0123456789",
            "Status: Succeeded",
            "Trigger: Facts feedback-from-susan.json"
        ]);
    }

    [Test]
    public void ARunFailed()
    {
        AgentRun failed = Run with
        {
            Agent = KnownAgents.Editor,
            Status = AgentRunStatus.Failed,
            Summary = null,
            Error = "The model server is unreachable.\n   at Some.Stack.Trace()"
        };

        string message = WikiCommitMessage.For(failed);

        ItShouldUseTheErrorAsTheSubject();
        ItShouldRecordTheFailure();

        void ItShouldUseTheErrorAsTheSubject() =>
            message.Split('\n')[0].ShouldBe("Editor: The model server is unreachable.");

        void ItShouldRecordTheFailure() => message.ShouldContain("\nStatus: Failed\n", Case.Sensitive);
    }

    [Test]
    public void TheSummaryStartsWithAHeadingAfterBlankLines()
    {
        string message = WikiCommitMessage.For(Run with { Summary = "\n\n## Woven the feedback in\n\nDetails." });

        ItShouldUseTheHeadingText();

        void ItShouldUseTheHeadingText() => message.Split('\n')[0].ShouldBe("ThreadWeaver: Woven the feedback in");
    }

    [Test]
    public void TheSummaryLineIsLong()
    {
        string summary = string.Concat(Enumerable.Repeat("Updated the platform team page ", 5));

        string subject = WikiCommitMessage.For(Run with { Summary = summary }).Split('\n')[0];

        ItShouldBeShortenedWithAnEllipsis();

        void ItShouldBeShortenedWithAnEllipsis()
        {
            string shortened = subject["ThreadWeaver: ".Length..];
            shortened.Length.ShouldBeLessThanOrEqualTo(WikiCommitMessage.MaxSummaryLength);
            shortened.ShouldEndWith("...");
            summary.ShouldStartWith(shortened[..^3]);
        }
    }

    [Test]
    public void TheRunHasNoSummary()
    {
        string message = WikiCommitMessage.For(Run with { Summary = "   " });

        ItShouldSayTheRunFinished();

        void ItShouldSayTheRunFinished() => message.Split('\n')[0].ShouldBe("ThreadWeaver: Run finished");
    }

    [Test]
    public void AnAgentAuthorsACommit()
    {
        string author = WikiCommitMessage.Author(KnownAgents.ThreadWeaver);

        ItShouldUseTheAgentNameWithTheSharedEmail();

        void ItShouldUseTheAgentNameWithTheSharedEmail() => author.ShouldBe("ThreadWeaver <knowledge-base@localhost>");
    }
}