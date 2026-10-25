namespace KnowledgeBase.Agent.Events;

/// <summary>
/// Tells the UI's event stream that an area changed when the change is not a file, such as a status signal.
/// </summary>
internal interface IChangePublisher
{
    void Publish(string area);
}