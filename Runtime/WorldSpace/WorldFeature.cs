using UnityEngine;

namespace Mosaic.UI
{
    public abstract class WorldFeature : MonoBehaviour
    {
        public ServiceRegistry Services { get; private set; }

        protected T GetStore<T>() where T : class => Services.Get<T>();

        public virtual void Initialize(ServiceRegistry services)
        {
            Services = services;
        }

        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void OnModeChanged(string modeName) { }
        protected virtual void OnDispose() { }

        public void Dispose()
        {
            OnHide();
            OnDispose();
            Services = null;
            if (gameObject != null)
                Destroy(gameObject);
        }
    }
}
