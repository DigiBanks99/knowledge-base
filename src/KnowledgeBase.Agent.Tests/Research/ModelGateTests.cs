using KnowledgeBase.Agent.Research;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Tests.Research;

public sealed class ModelGateTests : IDisposable
{
    private readonly ModelGate _gate = new(Options.Create(new ResearchSettings { ModelConcurrency = 1 }));

    public void Dispose() => _gate.Dispose();

    [Test]
    public async Task TwoRunsAskForTheModelAtOnce()
    {
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondStartedWhileFirstRan = false;
        bool firstRunning = false;

        Task<int> first = _gate.RunAsync(async _ =>
        {
            firstRunning = true;
            firstStarted.SetResult();
            await releaseFirst.Task;
            firstRunning = false;
            return 1;
        }, CancellationToken.None);
        await firstStarted.Task;
        Task<int> second = _gate.RunAsync(_ =>
        {
            secondStartedWhileFirstRan = firstRunning;
            return Task.FromResult(2);
        }, CancellationToken.None);
        bool secondWaited = !second.IsCompleted;
        releaseFirst.SetResult();
        int[] results = await Task.WhenAll(first, second);

        ItShouldMakeTheSecondWait();
        ItShouldNeverRunThemTogether();
        ItShouldRunBoth();

        void ItShouldMakeTheSecondWait() => secondWaited.ShouldBeTrue();

        void ItShouldNeverRunThemTogether() => secondStartedWhileFirstRan.ShouldBeFalse();

        void ItShouldRunBoth() => results.ShouldBe([1, 2]);
    }

    [Test]
    public async Task ARunFails()
    {
        Exception? exception = await Catch.ExceptionAsync(() => _gate.RunAsync<int>(_ => throw new InvalidOperationException("Boom."), CancellationToken.None));
        int next = await _gate.RunAsync(_ => Task.FromResult(2), CancellationToken.None);

        ItShouldPassTheFailureOn();
        ItShouldFreeTheModelForTheNextRun();

        void ItShouldPassTheFailureOn() => exception.ShouldBeOfType<InvalidOperationException>();

        void ItShouldFreeTheModelForTheNextRun() => next.ShouldBe(2);
    }
}
