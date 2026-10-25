using KnowledgeBase.Agent.Common;

namespace KnowledgeBase.Agent.Tests.Common;

public sealed class TaskResultExtensionsTests
{
    private readonly ValidationError _firstError = new("First", "The first failure.");
    private readonly ValidationError _secondError = new("Second", "The second failure.");

    [Test]
    public void EveryResultSucceeded()
    {
        Result<IReadOnlyCollection<int>, Error>[] results = [Success(1, 2), Success(), Success(3)];

        Result<IReadOnlyCollection<int>, Error> zipped = results.ZipResults();

        ItShouldSucceed();
        ItShouldFlattenTheValuesInOrder();

        void ItShouldSucceed() => zipped.IsSuccess.ShouldBeTrue();

        void ItShouldFlattenTheValuesInOrder() => zipped.Value.ShouldBe([1, 2, 3]);
    }

    [Test]
    public void SomeResultsFailed()
    {
        Result<IReadOnlyCollection<int>, Error>[] results = [Success(1), Failure(_firstError), Failure(_secondError)];

        Result<IReadOnlyCollection<int>, Error> zipped = results.ZipResults();

        ItShouldFail();
        ItShouldReportTheFirstFailure();

        void ItShouldFail() => zipped.IsFailure.ShouldBeTrue();

        void ItShouldReportTheFirstFailure() => zipped.Error.ShouldBeSameAs(_firstError);
    }

    [Test]
    public void NoResultsAreZipped()
    {
        Result<IReadOnlyCollection<int>, Error> zipped = Enumerable.Empty<Result<IReadOnlyCollection<int>, Error>>().ZipResults();

        ItShouldSucceedWithNoValues();

        void ItShouldSucceedWithNoValues() => zipped.Value.ShouldBeEmpty();
    }

    [Test]
    public async Task PendingResultsAreZipped()
    {
        TaskCompletionSource<Result<IReadOnlyCollection<int>, Error>> slow = new();
        Task<Result<IReadOnlyCollection<int>, Error>>[] results = [slow.Task, Task.FromResult(Success(2))];

        Task<Result<IReadOnlyCollection<int>, Error>> zipping = results.ZipResultsAsync();
        bool completedBeforeEveryResult = zipping.IsCompleted;
        slow.SetResult(Success(1));
        Result<IReadOnlyCollection<int>, Error> zipped = await zipping;

        ItShouldWaitForEveryResult();
        ItShouldKeepTheOrderOfTheTasks();

        void ItShouldWaitForEveryResult() => completedBeforeEveryResult.ShouldBeFalse();

        void ItShouldKeepTheOrderOfTheTasks() => zipped.Value.ShouldBe([1, 2]);
    }

    private static Result<IReadOnlyCollection<int>, Error> Success(params int[] values) =>
        Result.Success<IReadOnlyCollection<int>, Error>(values);

    private static Result<IReadOnlyCollection<int>, Error> Failure(Error error) =>
        Result.Failure<IReadOnlyCollection<int>, Error>(error);
}
