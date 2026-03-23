using UnityEngine;

namespace Mosaic.UI
{
    public abstract class WorldController : MonoBehaviour
    {
        [SerializeField] private int priority;

        public int Priority => priority;
        public ServiceRegistry Services { get; private set; }

        protected T GetStore<T>() where T : class => Services.Get<T>();

        public virtual void Initialize(ServiceRegistry services)
        {
            Services = services;
        }

        public virtual void OnActivated() { }
        public virtual void OnDeactivated() { }
        public virtual void OnModeChanged(string modeName) { }
        protected virtual void OnDispose() { }

        public void Dispose()
        {
            OnDeactivated();
            OnDispose();
            Services = null;
            if (gameObject != null)
                Destroy(gameObject);
        }
    }
}
