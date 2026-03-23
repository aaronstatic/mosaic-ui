using System;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    public abstract class PanelController : IDisposable
    {
        public VisualElement Root { get; internal set; }
        public ServiceRegistry Services { get; internal set; }
        public SubscriptionGroup Subscriptions { get; } = new();

        protected T GetStore<T>() where T : class
        {
            return Services.Get<T>();
        }

        /// <summary>
        /// Called after Root and Services are set. Set up data bindings and query child elements here.
        /// </summary>
        public virtual void OnBind() { }

        /// <summary>
        /// Called when the panel becomes visible.
        /// </summary>
        public virtual void OnShow() { }

        /// <summary>
        /// Called when the panel is hidden (but not yet disposed).
        /// </summary>
        public virtual void OnHide() { }

        /// <summary>
        /// Called when the mode changes while this panel remains active.
        /// </summary>
        public virtual void OnModeChanged(string modeName) { }

        /// <summary>
        /// Override for custom cleanup. Called during Dispose.
        /// </summary>
        protected virtual void OnDispose() { }

        public void Dispose()
        {
            Subscriptions.Dispose();
            OnDispose();
            Root = null;
            Services = null;
        }
    }
}
