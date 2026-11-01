using KnowledgeBase.Agent.Research.Triage;

namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// The Keeper standing in for the decision model, so triage never calls a model server. A test scripts its answers per
/// page; by default it finds every result noise.
/// </summary>
internal sealed class FakeKeeper : IKeeper
{
    /// <summary>The first call's answer for a page.</summary>
    public Func<KeeperPage, Screening> Screen { get; set; } = _ => new Screening(0.1, 0.1);

    /// <summary>The second call's answer for a kept page.</summary>
    public Func<KeeperPage, Description> Describe { get; set; } = page =>
        new Description("release", 0.8, page.Paragraphs.Count > 0 ? new Dictionary<int, double> { [1] = 0.9 } : []);

    public Task<Result<Screening, Error>> ScreenAsync(KeeperPage page, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<Screening, Error>(Screen(page)));

    public Task<Result<Description, Error>> DescribeAsync(KeeperPage page, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<Description, Error>(Describe(page)));
}
