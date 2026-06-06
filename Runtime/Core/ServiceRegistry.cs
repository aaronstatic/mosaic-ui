using System;
using System.Collections.Generic;

namespace Mosaic.UI
{
    public class ServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new();

        // Read-only introspection view for the editor debugger (Mosaic.UI.Editor).
        // No defensive copy: callers must treat this as read-only and never mutate the registry through it.
        internal IReadOnlyDictionary<Type, object> Entries => _services;

        public void Register<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            _services[typeof(T)] = service;
        }

        public T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var service))
                return (T)service;

            throw new InvalidOperationException(
                $"Service of type {typeof(T).Name} is not registered.");
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var obj))
            {
                service = (T)obj;
                return true;
            }

            service = null;
            return false;
        }

        public TStore CreateStore<TStore>() where TStore : class, new()
        {
            var store = new TStore();
            Register(store);
            return store;
        }

        public void Clear()
        {
            _services.Clear();
        }
    }
}
