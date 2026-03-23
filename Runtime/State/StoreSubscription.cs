using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    internal sealed class StoreSubscription<TStore, TSlice> : IDisposable
        where TStore : Store<TStore>
    {
        private TStore _store;
        private readonly Func<TStore, TSlice> _selector;
        private readonly Action<TSlice> _callback;
        private TSlice _previousValue;

        public StoreSubscription(TStore store, Func<TStore, TSlice> selector, Action<TSlice> callback)
        {
            _store = store;
            _selector = selector;
            _callback = callback;
            _previousValue = selector(store);

            store.propertyChanged += OnPropertyChanged;
        }

        private void OnPropertyChanged(object sender, BindablePropertyChangedEventArgs e)
        {
            var currentValue = _selector(_store);
            if (!EqualityComparer<TSlice>.Default.Equals(_previousValue, currentValue))
            {
                _previousValue = currentValue;
                _callback(currentValue);
            }
        }

        public void Dispose()
        {
            if (_store != null)
            {
                _store.propertyChanged -= OnPropertyChanged;
                _store = null;
            }
        }
    }
}
