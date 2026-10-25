namespace KnowledgeBase.Agent.Common;

internal static class TaskResultExtensions
{
    internal static Result<IReadOnlyCollection<T>, Error> ZipResults<T>(
        this IEnumerable<Result<IReadOnlyCollection<T>, Error>> results)
    {
        IEnumerable<Result<IReadOnlyCollection<T>, Error>> enumerable =
            results as Result<IReadOnlyCollection<T>, Error>[]
            ?? results.ToArray();
        foreach (Result<IReadOnlyCollection<T>, Error> result in enumerable)
        {
            if (result.IsFailure)
            {
                return Result.Failure<IReadOnlyCollection<T>, Error>(result.Error);
            }
        }

        return Result
            .Success<IReadOnlyCollection<T>, Error>(enumerable.SelectMany(r => r.Value).ToList());
    }

    internal static async Task<Result<IReadOnlyCollection<T>, Error>> ZipResultsAsync<T>(
        this IEnumerable<Task<Result<IReadOnlyCollection<T>, Error>>> results)
    {
        IEnumerable<Result<IReadOnlyCollection<T>, Error>> taskResults = await Task.WhenAll(results);

        return taskResults.ZipResults();
    }
}
