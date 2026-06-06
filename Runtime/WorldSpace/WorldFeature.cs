using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mosaic.UI
{
    public abstract class WorldFeature : MonoBehaviour
    {
        public ServiceRegistry Services { get; private set; }
        public SubscriptionGroup Subscriptions { get; } = new();

        protected T GetStore<T>() where T : class => Services.Get<T>();

        public virtual void Initialize(ServiceRegistry services)
        {
            Services = services;
        }

        // ── Input helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribes <paramref name="handler"/> to the <c>started</c> phase of the named action
        /// via <see cref="MosaicUI.Input"/> and adds an auto-unhook <see cref="IDisposable"/> to
        /// <see cref="Subscriptions"/>.
        /// </summary>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="handler">The action to invoke when the action starts. Must not be null.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that detaches the handler early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        protected IDisposable BindActionStarted(string actionName, Action<InputAction.CallbackContext> handler)
            => InputBindingExtensions.BindActionStarted(Subscriptions, actionName, handler);

        /// <summary>
        /// Subscribes <paramref name="handler"/> to the <c>performed</c> phase of the named action
        /// via <see cref="MosaicUI.Input"/> and adds an auto-unhook <see cref="IDisposable"/> to
        /// <see cref="Subscriptions"/>.
        /// </summary>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="handler">The action to invoke when the action is performed. Must not be null.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that detaches the handler early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        protected IDisposable BindActionPerformed(string actionName, Action<InputAction.CallbackContext> handler)
            => InputBindingExtensions.BindActionPerformed(Subscriptions, actionName, handler);

        /// <summary>
        /// Subscribes <paramref name="handler"/> to the <c>canceled</c> phase of the named action
        /// via <see cref="MosaicUI.Input"/> and adds an auto-unhook <see cref="IDisposable"/> to
        /// <see cref="Subscriptions"/>.
        /// </summary>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="handler">The action to invoke when the action is canceled. Must not be null.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that detaches the handler early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        protected IDisposable BindActionCanceled(string actionName, Action<InputAction.CallbackContext> handler)
            => InputBindingExtensions.BindActionCanceled(Subscriptions, actionName, handler);

        /// <summary>
        /// Returns the current value of the named action as <typeparamref name="TValue"/>.
        /// Delegates to <see cref="InputService.ReadValue{TValue}(string)"/> via <see cref="MosaicUI.Input"/>.
        /// </summary>
        /// <typeparam name="TValue">The struct type to read (e.g. <see cref="UnityEngine.Vector2"/>, <c>float</c>).</typeparam>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Move"</c>.</param>
        /// <returns>The current action value as <typeparamref name="TValue"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        protected TValue ReadAction<TValue>(string actionName) where TValue : struct
            => MosaicUI.Input.ReadValue<TValue>(actionName);

        /// <summary>
        /// Subscribes the <c>performed</c> phase of the named action to invoke the named command
        /// via <see cref="MosaicUI.Commands"/> (parameterless), and adds an auto-unhook
        /// <see cref="IDisposable"/> to <see cref="Subscriptions"/>.
        /// <para>
        /// Device sibling of <c>BindCommand</c>: both reach the same <see cref="CommandRegistry"/>
        /// command identically across UI and device input. Default phase is <c>performed</c>.
        /// </para>
        /// </summary>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="commandId">The command id to invoke. Must not be null or empty.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that detaches the mapping early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        protected IDisposable MapAction(string actionName, string commandId)
            => InputBindingExtensions.MapAction(Subscriptions, actionName, commandId);

        /// <summary>
        /// Subscribes the <c>performed</c> phase of the named action to invoke the named command
        /// via <see cref="MosaicUI.Commands"/> with a typed payload, and adds an auto-unhook
        /// <see cref="IDisposable"/> to <see cref="Subscriptions"/>.
        /// <para>Default phase is <c>performed</c>.</para>
        /// </summary>
        /// <typeparam name="T">The payload type expected by the registered command handler.</typeparam>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="commandId">The command id to invoke. Must not be null or empty.</param>
        /// <param name="payload">
        /// Factory called with the <see cref="InputAction.CallbackContext"/> at fire-time to produce
        /// the payload value. Must not be null.
        /// </param>
        /// <returns>
        /// An <see cref="IDisposable"/> that detaches the mapping early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        protected IDisposable MapAction<T>(string actionName, string commandId, Func<InputAction.CallbackContext, T> payload)
            => InputBindingExtensions.MapAction(Subscriptions, actionName, commandId, payload);

        // ── Lifecycle ────────────────────────────────────────────────────────────────

        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void OnModeChanged(string modeName) { }
        protected virtual void OnDispose() { }

        public void Dispose()
        {
            // Dispose subscriptions FIRST: a BindAction* callback may reference Services/stores,
            // so the InputSystem handlers must detach before Services is nulled and before the
            // GameObject is destroyed. Mirrors PanelController.Dispose() (Subscriptions before Root/Services).
            Subscriptions.Dispose();
            OnHide();
            OnDispose();
            Services = null;
            if (gameObject != null)
                Destroy(gameObject);
        }
    }
}
