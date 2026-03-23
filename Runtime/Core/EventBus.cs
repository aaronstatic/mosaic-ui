using System;
using System.Collections.Generic;
using System.Linq;

namespace Mosaic.UI
{
    public class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var type = typeof(T);

            if (!_subscribers.TryGetValue(type, out var handlers))
            {
                handlers = new List<Delegate>();
                _subscribers[type] = handlers;
            }

            handlers.Add(handler);

            return new Subscription(() =>
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                    _subscribers.Remove(type);
            });
        }

        public void Publish<T>(T message)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var handlers))
                return;

            // Iterate over a copy to allow modifications during publish
            var snapshot = handlers.ToArray();
            foreach (var handler in snapshot)
            {
                ((Action<T>)handler)(message);
            }
        }

        public void Clear()
        {
            _subscribers.Clear();
        }

        private sealed class Subscription : IDisposable
        {
            private Action _unsubscribe;

            public Subscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                _unsubscribe?.Invoke();
                _unsubscribe = null;
            }
        }
    }

    public class SubscriptionGroup : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = new();

        public void Add(IDisposable subscription)
        {
            if (subscription == null)
                throw new ArgumentNullException(nameof(subscription));

            _subscriptions.Add(subscription);
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
