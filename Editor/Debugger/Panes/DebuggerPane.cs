using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    /// <summary>
    /// Abstract base for all MosaicUI Debugger panes.
    /// BuildUI() is called once during Initialize(); Root is reused across attach/detach cycles.
    /// Panes must support the sequence: Initialize → Attach → Detach → Attach → Detach → ...
    /// without leaking subscriptions, throwing, or requiring recreation.
    /// </summary>
    public abstract class DebuggerPane
    {
        /// <summary>Tab label shown in the debugger's tab strip.</summary>
        public abstract string Title { get; }

        /// <summary>Root element built once in BuildUI(); displayed in the content host.</summary>
        public VisualElement Root { get; protected set; }

        /// <summary>Back-reference to the owning window; available after Initialize().</summary>
        protected MosaicDebuggerWindow Window { get; private set; }

        /// <summary>
        /// Called by the window once, before first display.
        /// Sets the Window back-ref then calls BuildUI() to construct Root.
        /// </summary>
        public void Initialize(MosaicDebuggerWindow window)
        {
            Window = window;
            BuildUI();
        }

        /// <summary>
        /// Build Root once. Root is reused across attach/detach cycles; do not recreate it in
        /// Attach/Detach. Show/hide the empty-state vs. live-content children here if needed,
        /// or do it in Attach/Detach after Root is built.
        /// </summary>
        protected abstract void BuildUI();

        /// <summary>
        /// Called when a live MosaicUI session becomes available (IsInitialized flipped true).
        /// Subscribe to stores, hooks, schedulers, etc. here.
        /// MUST be idempotent: detach before re-attaching to avoid double-subscriptions.
        /// </summary>
        public virtual void Attach() { }

        /// <summary>
        /// Called on play-exit, ExitingPlayMode, or window close (OnDisable).
        /// Unsubscribe everything acquired in Attach(). Null-guard all facade access.
        /// After Detach(), Root must still be displayable in its empty/placeholder state.
        /// </summary>
        public virtual void Detach() { }

        /// <summary>
        /// Called to repopulate on demand (e.g. poll tick) or after a state change.
        /// Phases 3-5 implement this; Phase 2 stubs leave it as no-op.
        /// </summary>
        public virtual void Refresh() { }
    }
}
