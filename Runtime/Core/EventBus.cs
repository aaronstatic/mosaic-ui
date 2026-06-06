using System;
using System.Collections.Generic;
using System.Linq;

namespace Mosaic.UI
{
    public class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

#if UNITY_EDITOR
        // Editor-only introspection hook for the MosaicUI debugger (event monitor pane).
        // Fired once per Publish<T>, AFTER real subscribers have been dispatched, so an editor
        // observer can never change subscriber dispatch order or interfere with live delivery.
        // The whole hook is compiled out in player builds (no field, no invoke) → zero runtime cost.
        internal event Action<Type, object> Published;
#endif

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
            // Dispatch to real subscribers (unchanged semantics). Guard the loop instead of
            // early-returning, so the editor hook below still observes zero-subscriber publishes.
            if (_subscribers.TryGetValue(typeof(T), out var handlers))
            {
                // Iterate over a copy to allow modifications during publish
                var snapshot = handlers.ToArray();
                foreach (var handler in snapshot)
                {
                    ((Action<T>)handler)(message);
                }
            }

#if UNITY_EDITOR
            // Fire the editor introspection hook exactly once, AFTER dispatch, for every publish
            // (regardless of subscriber count). Post-dispatch ordering guarantees an editor observer
            // cannot affect subscriber dispatch order or interfere with live delivery. Compiled out
            // entirely in player builds.
            Published?.Invoke(typeof(T), message);
#endif
        }

        public void Clear()
        {
            _subscribers.Clear();
        }

#if UNITY_EDITOR
        // Editor-only introspection: current handler count for an event type (0 if none).
        // Read-only; no behavior change. Compiled out in player builds.
        internal int SubscriberCount(Type type)
        {
            if (type != null && _subscribers.TryGetValue(type, out var handlers))
                return handlers.Count;
            return 0;
        }
#endif

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
