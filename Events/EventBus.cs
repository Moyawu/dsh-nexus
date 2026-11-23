using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DshTrayLauncher.Events;

public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull
    {
        var type = typeof(TEvent);
        var list = _subscribers.GetOrAdd(type, _ => new List<Delegate>());
        
        lock (list)
        {
            list.Add(handler);
        }

        return new SubscriptionToken(this, type, handler);
    }

    public void Publish<TEvent>(TEvent eventData) where TEvent : notnull
    {
        if (_subscribers.TryGetValue(typeof(TEvent), out var list))
        {
            List<Delegate> targets;
            lock (list)
            {
                targets = new List<Delegate>(list);
            }

            foreach (var handler in targets)
            {
                ((Action<TEvent>)handler)(eventData);
            }
        }
    }

    private class SubscriptionToken : IDisposable
    {
        private readonly EventBus _bus;
        private readonly Type _type;
        private readonly Delegate _handler;

        public SubscriptionToken(EventBus bus, Type type, Delegate handler)
        {
            _bus = bus;
            _type = type;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_bus._subscribers.TryGetValue(_type, out var list))
            {
                lock (list)
                {
                    list.Remove(_handler);
                }
            }
        }
    }
}
