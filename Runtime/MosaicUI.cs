using System;

namespace Mosaic.UI
{
    public static class MosaicUI
    {
        public static ServiceRegistry Services { get; private set; }
        public static EventBus Events { get; private set; }

        public static bool IsInitialized { get; private set; }

        public static void Initialize()
        {
            if (IsInitialized)
                return;

            Services = new ServiceRegistry();
            Events = new EventBus();
            IsInitialized = true;
        }

        public static void Shutdown()
        {
            if (!IsInitialized)
                return;

            Events.Clear();
            Services.Clear();
            Events = null;
            Services = null;
            IsInitialized = false;
        }
    }
}
