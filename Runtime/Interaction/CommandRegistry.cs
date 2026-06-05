using System;
using System.Collections.Generic;

namespace Mosaic.UI
{
    public class CommandRegistry
    {
        private readonly Dictionary<string, Delegate> _commands = new();

        // --- Register (parameterless) ---

        public IDisposable Register(string id, Action handler)
        {
            ValidateId(id);
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_commands.ContainsKey(id))
                throw new InvalidOperationException(
                    $"Command '{id}' is already registered. Dispose the existing registration before re-registering.");

            _commands[id] = handler;

            return new Subscription(() => _commands.Remove(id));
        }

        // --- Register<T> (typed payload) ---

        public IDisposable Register<T>(string id, Action<T> handler)
        {
            ValidateId(id);
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_commands.ContainsKey(id))
                throw new InvalidOperationException(
                    $"Command '{id}' is already registered. Dispose the existing registration before re-registering.");

            _commands[id] = handler;

            return new Subscription(() => _commands.Remove(id));
        }

        // --- Invoke (parameterless) ---

        public void Invoke(string id)
        {
            if (!_commands.TryGetValue(id, out var del))
                throw new InvalidOperationException(
                    $"Command '{id}' is not registered.");

            if (del is Action action)
            {
                action();
            }
            else
            {
                // Stored delegate is an Action<T> — arity mismatch
                var expectedType = del.GetType().GenericTypeArguments.Length > 0
                    ? del.GetType().GenericTypeArguments[0].Name
                    : del.GetType().Name;
                throw new InvalidOperationException(
                    $"Command '{id}' expects a typed payload (Action<{expectedType}>) but was invoked without a payload (parameterless Invoke).");
            }
        }

        // --- Invoke<T> (typed payload) ---

        public void Invoke<T>(string id, T payload)
        {
            if (!_commands.TryGetValue(id, out var del))
                throw new InvalidOperationException(
                    $"Command '{id}' is not registered.");

            if (del is Action<T> typedAction)
            {
                typedAction(payload);
            }
            else if (del is Action)
            {
                throw new InvalidOperationException(
                    $"Command '{id}' expects no payload (parameterless Action) but was invoked with a payload of type '{typeof(T).Name}' (typed Invoke<{typeof(T).Name}>).");
            }
            else
            {
                // Stored delegate is Action<U> where U != T
                var expectedType = del.GetType().GenericTypeArguments.Length > 0
                    ? del.GetType().GenericTypeArguments[0].Name
                    : del.GetType().Name;
                throw new InvalidOperationException(
                    $"Command '{id}' expects a payload of type '{expectedType}' but was invoked with type '{typeof(T).Name}'.");
            }
        }

        // --- Introspection ---

        public bool Has(string id)
        {
            return _commands.ContainsKey(id);
        }

        public IReadOnlyCollection<string> RegisteredIds
        {
            get
            {
                // Return a snapshot, not a live reference
                var snapshot = new string[_commands.Count];
                _commands.Keys.CopyTo(snapshot, 0);
                return snapshot;
            }
        }

        // --- Lifecycle ---

        public void Clear()
        {
            _commands.Clear();
        }

        // --- Private helpers ---

        private static void ValidateId(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (id.Length == 0)
                throw new ArgumentException("Command id must not be empty.", nameof(id));
        }

        private sealed class Subscription : IDisposable
        {
            private Action _unregister;

            public Subscription(Action unregister)
            {
                _unregister = unregister;
            }

            public void Dispose()
            {
                _unregister?.Invoke();
                _unregister = null;
            }
        }
    }
}
