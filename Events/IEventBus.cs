namespace DshTrayLauncher.Events;

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull;
    void Publish<TEvent>(TEvent eventData) where TEvent : notnull;
}
