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
    public async Task ThePlebAndTheScoutWaitForTheModel()
    {
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> order = [];

        Task first = _gate.RunAsync(async _ =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
            return 0;
        }, CancellationToken.None);
        await firstStarted.Task;
        Task pleb = _gate.RunAsync(_ => Record("Pleb"), CancellationToken.None);
        Task scout = _gate.RunAsync(_ => Record("Scout"), CancellationToken.None, ModelPriority.High);
        releaseFirst.SetResult();
        await Task.WhenAll(first, pleb, scout);

        ItShouldRunTheScoutFirst();

        void ItShouldRunTheScoutFirst() => order.ShouldBe(["Scout", "Pleb"]);

        Task<int> Record(string who)
        {
            order.Add(who);
            return Task.FromResult(0);
        }
    }

    [Test]
    public async Task AWaitingRunIsCancelled()
    {
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        bool cancelledRunStarted = false;

        Task first = _gate.RunAsync(async _ =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
            return 0;
        }, CancellationToken.None);
        await firstStarted.Task;
        Task cancelled = _gate.RunAsync(_ =>
        {
            cancelledRunStarted = true;
            return Task.FromResult(0);
        }, cancellation.Token);
        await cancellation.CancelAsync();
        Exception? error = await Catch.ExceptionAsync(() => cancelled);
        releaseFirst.SetResult();
        await first;
        int next = await _gate.RunAsync(_ => Task.FromResult(2), CancellationToken.None);

        ItShouldNeverRunIt();
        ItShouldNotKeepTheModelFromTheNextRun();

        void ItShouldNeverRunIt()
        {
            error.ShouldBeAssignableTo<OperationCanceledException>();
            cancelledRunStarted.ShouldBeFalse();
        }

        void ItShouldNotKeepTheModelFromTheNextRun() => next.ShouldBe(2);
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
