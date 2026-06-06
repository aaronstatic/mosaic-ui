using System;

namespace Mosaic.UI
{
    public static class MosaicUI
    {
        public static ServiceRegistry Services { get; private set; }
        public static EventBus Events { get; private set; }
        public static CommandRegistry Commands { get; private set; }
        public static InputService Input { get; private set; }

        public static bool IsInitialized { get; private set; }

        public static void Initialize()
        {
            if (IsInitialized)
                return;

            Services = new ServiceRegistry();
            Events = new EventBus();
            Commands = new CommandRegistry();
            Input = new InputService(Events);
            IsInitialized = true;
        }

        public static void Shutdown()
        {
            if (!IsInitialized)
                return;

            Commands.Clear();
            Events.Clear();
            Services.Clear();
            Input.Dispose();
            Commands = null;
            Events = null;
            Services = null;
            Input = null;
            IsInitialized = false;
        }
    }
}
