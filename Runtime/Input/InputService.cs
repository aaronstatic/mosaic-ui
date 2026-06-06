using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace Mosaic.UI
{
    /// <summary>
    /// The input source service for MosaicUI.
    /// Wraps a consumer-supplied <see cref="InputActionAsset"/>, resolves named actions and maps,
    /// exposes service-level subscriptions to action phases, reads current action values,
    /// manages map enable/disable, and broadcasts a <c>ControlSchemeChanged</c> struct on the
    /// <see cref="EventBus"/> when the active control scheme switches.
    /// <para>
    /// Accessed via <see cref="MosaicUI.Input"/> after <see cref="MosaicUI.Initialize"/>.
    /// Assign an asset via <c>SetAsset(asset)</c> before calling any resolution or subscription methods.
    /// </para>
    /// <para>
    /// Controller-level helpers (<c>BindAction</c>), the action-to-command bridge, the UI-vs-world
    /// routing gate, and per-mode map activation are all in <c>composition/input-binding</c> — not here.
    /// </para>
    /// </summary>
    public class InputService : IDisposable
    {
        private readonly EventBus _events;
        private InputActionAsset _asset;

        // Maps this service has enabled via EnableMap() — tracked so Dispose() / hot-swap can
        // disable them and leave no enabled InputSystem state behind.
        // Populated by Phase 4 (EnableMap / DisableMap).
        private readonly HashSet<string> _enabledMapNames = new HashSet<string>();

        // Handlers this service has attached to InputAction phase events via the Subscribe*() methods.
        // Each record holds enough information to detach the specific handler from the specific action.
        // Populated by Phase 3 (SubscribeStarted / SubscribePerformed / SubscribeCanceled).
        private readonly List<AttachedHandler> _attachedHandlers = new List<AttachedHandler>();

        // Guards Dispose() so a second call (and the lifetime-scoped InputUser.onChange detach below)
        // is a harmless no-op. Distinct from the _asset-null guard: the scheme hook is tied to the
        // service lifetime, not to whether an asset was ever assigned.
        private bool _disposed;

        // -----------------------------------------------------------------------------------------
        // Introspection seam (T6.3) — read-only internal; visible to Mosaic.UI.Editor + Mosaic.UI.Tests
        // via the InternalsVisibleTo grants in Runtime/AssemblyInfo.cs.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The name of the currently assigned <see cref="InputActionAsset"/>, or <c>null</c>
        /// when no asset has been assigned.
        /// </summary>
        internal string AssetName => _asset?.name;

        /// <summary>
        /// A read-only snapshot of the names of all maps currently enabled via
        /// <see cref="EnableMap"/>. Changes to the service's internal set are not reflected
        /// in previously returned snapshots.
        /// </summary>
        internal IReadOnlyCollection<string> EnabledMaps => _enabledMapNames;

        /// <summary>
        /// Alias for <see cref="ActiveControlScheme"/>, exposed under a shorter name for the
        /// introspection surface used by editor/test assemblies.
        /// </summary>
        internal string ActiveScheme => ActiveControlScheme;

        /// <summary>
        /// Initialises the service with the <see cref="EventBus"/> it publishes to.
        /// The facade passes <c>MosaicUI.Events</c>; tests can pass a throwaway bus.
        /// <para>
        /// Subscribes to <see cref="InputUser.onChange"/> for the lifetime of the service so it can
        /// track the active control scheme and broadcast <see cref="ControlSchemeChanged"/>. The hook is
        /// detached in <see cref="Dispose"/> — it is independent of any assigned asset and is therefore
        /// not touched by <c>SetAsset</c> hot-swaps.
        /// </para>
        /// </summary>
        /// <param name="events">The event bus used to publish <see cref="ControlSchemeChanged"/>.</param>
        public InputService(EventBus events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));

            // Lifetime-scoped: attached once at construction, detached once in Dispose().
            // InputUser.onChange is a static InputSystem event, so this hook observes scheme changes
            // for any paired user without requiring a PlayerInput component (Decision 4).
            InputUser.onChange += OnUserChange;
        }

        // -----------------------------------------------------------------------------------------
        // Asset assignment
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Assigns the <see cref="InputActionAsset"/> this service resolves actions and maps from.
        /// <para>
        /// If an asset is already assigned, a clean hot-swap is performed before accepting the new one:
        /// every map this service enabled is disabled and every outstanding handler it attached is
        /// detached. The new asset is stored <em>without</em> enabling anything — callers enable
        /// maps explicitly via <c>EnableMap(name)</c>.
        /// </para>
        /// </summary>
        /// <param name="asset">The asset to assign. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="asset"/> is null.</exception>
        public void SetAsset(InputActionAsset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            // If an asset is already assigned, tear down all state tied to the old one
            // before accepting the new one, so nothing from the previous asset lingers.
            if (_asset != null)
                ResetState();

            _asset = asset;
        }

        // -----------------------------------------------------------------------------------------
        // Internal resolution helpers (private — used by Phase 3 Subscribe*, Phase 4 EnableMap etc.)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves an <see cref="InputAction"/> by qualified name (e.g. <c>"Player/Jump"</c>).
        /// </summary>
        /// <param name="name">The qualified action name.</param>
        /// <returns>The resolved <see cref="InputAction"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the action name is not found in the asset.
        /// </exception>
        private InputAction ResolveAction(string name)
        {
            if (_asset == null)
                throw new InvalidOperationException(
                    "No InputActionAsset has been assigned. Call SetAsset(...) before resolving actions.");

            var action = _asset.FindAction(name, throwIfNotFound: false);
            if (action == null)
                throw new InvalidOperationException(
                    $"Action '{name}' is not found in the assigned InputActionAsset.");

            return action;
        }

        /// <summary>
        /// Resolves an <see cref="InputActionMap"/> by name (e.g. <c>"Player"</c>).
        /// </summary>
        /// <param name="name">The action map name.</param>
        /// <returns>The resolved <see cref="InputActionMap"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the map name is not found in the asset.
        /// </exception>
        private InputActionMap ResolveMap(string name)
        {
            if (_asset == null)
                throw new InvalidOperationException(
                    "No InputActionAsset has been assigned. Call SetAsset(...) before resolving actions.");

            var map = _asset.FindActionMap(name, throwIfNotFound: false);
            if (map == null)
                throw new InvalidOperationException(
                    $"Action map '{name}' is not found in the assigned InputActionAsset.");

            return map;
        }

        // -----------------------------------------------------------------------------------------
        // Phase subscriptions
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Subscribes <paramref name="callback"/> to the <c>started</c> phase of the named action.
        /// Dispose the returned token to detach the callback.
        /// </summary>
        /// <param name="name">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="callback">The handler to invoke when the action starts.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> whose <c>Dispose()</c> detaches the callback.
        /// Double-disposing is a no-op.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the action name is not found in the asset.
        /// </exception>
        public IDisposable SubscribeStarted(string name, Action<InputAction.CallbackContext> callback)
            => SubscribePhase(name, callback, InputActionPhase.Started);

        /// <summary>
        /// Subscribes <paramref name="callback"/> to the <c>performed</c> phase of the named action.
        /// Dispose the returned token to detach the callback.
        /// </summary>
        /// <param name="name">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="callback">The handler to invoke when the action is performed.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> whose <c>Dispose()</c> detaches the callback.
        /// Double-disposing is a no-op.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the action name is not found in the asset.
        /// </exception>
        public IDisposable SubscribePerformed(string name, Action<InputAction.CallbackContext> callback)
            => SubscribePhase(name, callback, InputActionPhase.Performed);

        /// <summary>
        /// Subscribes <paramref name="callback"/> to the <c>canceled</c> phase of the named action.
        /// Dispose the returned token to detach the callback.
        /// </summary>
        /// <param name="name">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="callback">The handler to invoke when the action is canceled.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> whose <c>Dispose()</c> detaches the callback.
        /// Double-disposing is a no-op.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the action name is not found in the asset.
        /// </exception>
        public IDisposable SubscribeCanceled(string name, Action<InputAction.CallbackContext> callback)
            => SubscribePhase(name, callback, InputActionPhase.Canceled);

        /// <summary>
        /// Shared implementation for all three phase-subscription methods.
        /// Attaches <paramref name="callback"/> to the given <paramref name="phase"/> event on the
        /// resolved action, records the attachment in <see cref="_attachedHandlers"/>, and returns a
        /// <see cref="CallbackDisposable"/> that BOTH detaches the callback from that exact phase event
        /// AND removes the single <see cref="AttachedHandler"/> record it created (by reference identity)
        /// so consumer-driven disposal keeps the list lean and teardown does not double-detach.
        /// </summary>
        private IDisposable SubscribePhase(
            string name,
            Action<InputAction.CallbackContext> callback,
            InputActionPhase phase)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var action = ResolveAction(name);

            // Attach to the matching InputSystem phase event.
            switch (phase)
            {
                case InputActionPhase.Started:
                    action.started += callback;
                    break;
                case InputActionPhase.Performed:
                    action.performed += callback;
                    break;
                case InputActionPhase.Canceled:
                    action.canceled += callback;
                    break;
            }

            // Record the attachment so ResetState() / Dispose() can detach leftovers.
            var record = new AttachedHandler(action, phase, callback);
            _attachedHandlers.Add(record);

            // Return a token that detaches this exact callback AND removes this exact record.
            // Capturing 'record' by reference identity ensures that if the same callback is
            // attached twice, disposing one token removes only its own record.
            return new CallbackDisposable(() =>
            {
                record.Detach();
                _attachedHandlers.Remove(record);
            });
        }

        // -----------------------------------------------------------------------------------------
        // Value read
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Returns the current value of the named action as <typeparamref name="TValue"/>.
        /// <para>
        /// Resolves the action via <see cref="ResolveAction"/> and delegates to
        /// <c>InputAction.ReadValue&lt;TValue&gt;()</c>. A wrong <typeparamref name="TValue"/>
        /// (type or size mismatch) surfaces InputSystem's own exception — this is intentional
        /// (the exception is already clear and specific) and is not wrapped here.
        /// </para>
        /// </summary>
        /// <typeparam name="TValue">The struct type to read (e.g. <see cref="UnityEngine.Vector2"/>, <c>float</c>).</typeparam>
        /// <param name="name">Qualified action name, e.g. <c>"Player/Move"</c>.</param>
        /// <returns>The current action value as <typeparamref name="TValue"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the action name is not found in the asset.
        /// </exception>
        public TValue ReadValue<TValue>(string name) where TValue : struct
        {
            var action = ResolveAction(name);
            return action.ReadValue<TValue>();
        }

        // -----------------------------------------------------------------------------------------
        // Map enable / disable lever (Phase 4)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Enables the named action map so its actions can fire.
        /// Records the map name in the internal enabled-maps set so teardown can disable it.
        /// <para>
        /// Re-enabling an already-enabled map is a harmless no-op: the InputSystem tolerates
        /// <c>Enable()</c> on an already-enabled map, and <see cref="HashSet{T}.Add"/> on a
        /// present key is also a no-op.
        /// </para>
        /// </summary>
        /// <param name="name">The action map name, e.g. <c>"Player"</c>.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the map name is not found in the asset.
        /// </exception>
        public void EnableMap(string name)
        {
            var map = ResolveMap(name);
            map.Enable();
            _enabledMapNames.Add(name);
        }

        /// <summary>
        /// Disables the named action map so its actions no longer fire.
        /// Removes the map name from the internal enabled-maps set.
        /// <para>
        /// Disabling a map that is not currently enabled is a harmless no-op: the InputSystem
        /// tolerates <c>Disable()</c> on an already-disabled map, and
        /// <see cref="HashSet{T}.Remove"/> on an absent key is also a no-op.
        /// </para>
        /// </summary>
        /// <param name="name">The action map name, e.g. <c>"Player"</c>.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no asset has been assigned or the map name is not found in the asset.
        /// </exception>
        public void DisableMap(string name)
        {
            var map = ResolveMap(name);
            map.Disable();
            _enabledMapNames.Remove(name);
        }

        // -----------------------------------------------------------------------------------------
        // Control scheme tracking (Phase 5)
        // -----------------------------------------------------------------------------------------
        //
        // Primary mechanism (implemented below): InputUser.onChange filtered to
        // InputUserChange.ControlSchemeChanged. InputUser is InputSystem's PlayerInput-free user/device
        // pairing layer; it raises ControlSchemeChanged precisely when the active scheme flips, so we do
        // not have to re-derive scheme-from-device ourselves. It is driveable headlessly via virtual
        // devices + InputSystem.Update() (Decision 4).
        //
        // Caveat (R2): InputUser.onChange only fires for *paired* users, and pairing/automatic scheme
        // switching can behave differently headlessly without a PlayerInput. The Phase 6 EditMode suite
        // (InputTestFixture) is the behavioral proof that a KBM->gamepad switch publishes exactly one
        // correctly-populated ControlSchemeChanged.
        //
        // Documented fallback (NOT implemented — switch to it only if the primary proves unreliable on
        // 1.19.0): subscribe to InputSystem.onActionChange and, when an action's active control changes,
        // derive the scheme from the triggering control's device matched against the assigned asset's
        // control-scheme device requirements (InputControlScheme.SupportsDevice / deviceRequirements).
        // That path is still poll-free (no Device.current). Keep the equality-guarded publish below
        // unchanged; only the detection source would change.

        /// <summary>
        /// The name of the input control scheme currently active (e.g. <c>"Keyboard&amp;Mouse"</c>,
        /// <c>"Gamepad"</c>), or <c>null</c> when no scheme is active (no paired user / unpaired scheme).
        /// <para>
        /// Updated event-driven from <see cref="InputUser.onChange"/> (filtered to
        /// <see cref="InputUserChange.ControlSchemeChanged"/>) — never by polling the current input device.
        /// Whenever this value actually changes, a <see cref="ControlSchemeChanged"/> is published on the
        /// injected <see cref="EventBus"/>.
        /// </para>
        /// </summary>
        public string ActiveControlScheme { get; private set; }

        /// <summary>
        /// Handler for <see cref="InputUser.onChange"/>. Lifetime-scoped (attached in the constructor,
        /// detached in <see cref="Dispose"/>); not affected by <c>SetAsset</c> hot-swaps.
        /// <para>
        /// Acts only on the <see cref="InputUserChange.ControlSchemeChanged"/> notification: reads the
        /// affected user's active control-scheme name (a nullable <c>InputControlScheme?</c> — an
        /// unpaired/null scheme yields <c>null</c>) and, when it differs from <see cref="ActiveControlScheme"/>,
        /// updates the property and publishes a <see cref="ControlSchemeChanged"/> with the captured
        /// previous and new names. Same-scheme notifications are suppressed by the equality guard.
        /// </para>
        /// </summary>
        /// <param name="user">The user whose state changed.</param>
        /// <param name="change">The kind of change; only <see cref="InputUserChange.ControlSchemeChanged"/> is handled.</param>
        /// <param name="device">The device involved in the change (may be null); unused here.</param>
        private void OnUserChange(InputUser user, InputUserChange change, InputDevice device)
        {
            if (change != InputUserChange.ControlSchemeChanged)
                return;

            // controlScheme is an InputControlScheme? — read its name when present, else null.
            var scheme = user.controlScheme;
            var newScheme = scheme.HasValue ? scheme.Value.name : null;

            // Equality guard mirroring Store.SetProperty: EqualityComparer<string>.Default.Equals is
            // ordinal and null-safe (Equals(null, null) == true), so an unchanged scheme — including
            // a no-op republish on the same device class — publishes nothing.
            if (EqualityComparer<string>.Default.Equals(ActiveControlScheme, newScheme))
                return;

            // Capture Previous BEFORE overwriting, then store the new value, then publish. Storing first
            // keeps ActiveControlScheme consistent for any subscriber that reads it (or triggers a
            // re-entrant scheme change) from within the published callback.
            var previous = ActiveControlScheme;
            ActiveControlScheme = newScheme;
            _events.Publish(new ControlSchemeChanged { Previous = previous, Current = newScheme });
        }

        // -----------------------------------------------------------------------------------------
        // Teardown helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Disables every map this service enabled, detaches every outstanding action handler,
        /// and clears the tracking collections.
        /// Called both from <see cref="SetAsset"/> (hot-swap) and <see cref="Dispose"/>.
        /// After this call, no InputSystem state associated with the previous asset remains active.
        /// </summary>
        private void ResetState()
        {
            // Disable every map this service enabled (Phase 4 populates _enabledMapNames).
            // We iterate a snapshot because we're mutating the set inside the loop.
            var mapNames = new string[_enabledMapNames.Count];
            _enabledMapNames.CopyTo(mapNames);
            foreach (var mapName in mapNames)
            {
                // Guard: the asset may have changed between Enable and this teardown.
                var map = _asset?.FindActionMap(mapName, throwIfNotFound: false);
                map?.Disable();
            }
            _enabledMapNames.Clear();

            // Detach every outstanding handler (Phase 3 populates _attachedHandlers).
            foreach (var record in _attachedHandlers)
                record.Detach();
            _attachedHandlers.Clear();
        }

        // -----------------------------------------------------------------------------------------
        // IDisposable
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Releases all InputSystem state owned by this service.
        /// Disables every map this service enabled, detaches every outstanding action handler,
        /// unsubscribes the lifetime-scoped <see cref="InputUser.onChange"/> hook, and drops the asset
        /// reference. After disposal, further device/scheme switches publish nothing.
        /// Idempotent — a second call is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Detach the lifetime-scoped scheme hook independently of the asset path: it was attached
            // in the constructor (not by SetAsset), so it must come off even if no asset was ever assigned.
            InputUser.onChange -= OnUserChange;

            if (_asset != null)
                ResetState();

            _asset = null;
        }

        // -----------------------------------------------------------------------------------------
        // Internal tracking record (used by Phase 3)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Holds the information needed to detach a single handler from an InputAction phase event.
        /// Populated by <c>SubscribeStarted</c> / <c>SubscribePerformed</c> / <c>SubscribeCanceled</c>
        /// in Phase 3 and consumed by <see cref="ResetState"/> during hot-swap / teardown.
        /// </summary>
        internal sealed class AttachedHandler
        {
            private readonly InputAction _action;
            private readonly InputActionPhase _phase;
            private readonly Action<InputAction.CallbackContext> _handler;

            public AttachedHandler(
                InputAction action,
                InputActionPhase phase,
                Action<InputAction.CallbackContext> handler)
            {
                _action = action;
                _phase = phase;
                _handler = handler;
            }

            /// <summary>Detaches the handler from the action's phase event.</summary>
            public void Detach()
            {
                switch (_phase)
                {
                    case InputActionPhase.Started:
                        _action.started -= _handler;
                        break;
                    case InputActionPhase.Performed:
                        _action.performed -= _handler;
                        break;
                    case InputActionPhase.Canceled:
                        _action.canceled -= _handler;
                        break;
                }
            }
        }
    }
}
