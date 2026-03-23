using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    public abstract class Store<TSelf> : INotifyBindablePropertyChanged, IDataSourceViewHashProvider
        where TSelf : Store<TSelf>
    {
        private long _version;

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            _version++;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(new PropertyPath(propertyName)));
            return true;
        }

        public long GetViewHashCode()
        {
            return _version;
        }

        public IDisposable Subscribe<TSlice>(Func<TSelf, TSlice> selector, Action<TSlice> callback)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            return new StoreSubscription<TSelf, TSlice>((TSelf)this, selector, callback);
        }
    }
}
