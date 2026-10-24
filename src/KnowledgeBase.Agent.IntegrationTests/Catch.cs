namespace KnowledgeBase.Agent.IntegrationTests;

/// <summary>
/// Captures what an action throws, so a test can act first and leave the <c>ItShould...</c> checks to assert on the
/// outcome.
/// </summary>
internal static class Catch
{
    public static async Task<Exception?> ExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}