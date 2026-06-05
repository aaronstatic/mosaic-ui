using System;

namespace Mosaic.UI
{
    /// <summary>
    /// A lightweight <see cref="IDisposable"/> that wraps an unhook <see cref="Action"/>.
    /// Used by all <c>Bind*</c> helpers on <see cref="PanelController"/> so that the
    /// unhook logic (e.g. <c>button.clicked -= handler</c>) is authored once and shared.
    /// Mirrors the private sealed <c>Subscription</c> class in <see cref="EventBus"/>:
    /// null-guarded and idempotent on double-dispose.
    /// </summary>
    internal sealed class CallbackDisposable : IDisposable
    {
        private Action _unhook;

        public CallbackDisposable(Action unhook)
        {
            _unhook = unhook;
        }

        public void Dispose()
        {
            _unhook?.Invoke();
            _unhook = null;
        }
    }
}
