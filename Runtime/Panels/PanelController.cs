using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    public abstract class PanelController : IDisposable
    {
        public VisualElement Root { get; internal set; }
        public ServiceRegistry Services { get; internal set; }
        public SubscriptionGroup Subscriptions { get; } = new();

        /// <summary>
        /// Convenience accessor for the global command registry (<see cref="MosaicUI.Commands"/>).
        /// Use <c>Commands.Invoke(id)</c> from interaction handlers, or
        /// <c>Subscriptions.Add(Commands.Register(id, handler))</c> for panel-scoped registration.
        /// </summary>
        protected CommandRegistry Commands => MosaicUI.Commands;

        protected T GetStore<T>() where T : class
        {
            return Services.Get<T>();
        }

        // ── Interaction helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the <see cref="Button"/> named <paramref name="elementName"/> from
        /// <see cref="Root"/>, wires <paramref name="handler"/> to its <c>clicked</c> event,
        /// and adds an auto-unhook <see cref="IDisposable"/> to <see cref="Subscriptions"/>.
        /// </summary>
        /// <param name="elementName">The USS name of the button element.</param>
        /// <param name="handler">The action to invoke when the button is clicked.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unwires the handler early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no <see cref="Button"/> with the given name is found in <see cref="Root"/>.
        /// </exception>
        protected IDisposable BindClick(string elementName, Action handler)
        {
            var button = Root.Q<Button>(elementName);
            if (button == null)
                throw new InvalidOperationException(
                    $"[{GetType().Name}] BindClick: no Button named '{elementName}' found in the panel root.");

            return BindClick(button, handler);
        }

        /// <summary>
        /// Wires <paramref name="handler"/> to the <paramref name="button"/>'s <c>clicked</c>
        /// event and adds an auto-unhook <see cref="IDisposable"/> to <see cref="Subscriptions"/>.
        /// </summary>
        /// <param name="button">The button element (must not be null).</param>
        /// <param name="handler">The action to invoke when the button is clicked.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unwires the handler early if needed;
        /// it is already added to <see cref="Subscriptions"/> so manual disposal is not required.
        /// </returns>
        protected IDisposable BindClick(Button button, Action handler)
        {
            if (button == null)
                throw new ArgumentNullException(nameof(button));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            button.clicked += handler;
            var disposable = new CallbackDisposable(() => button.clicked -= handler);
            Subscriptions.Add(disposable);
            return disposable;
        }

        /// <summary>
        /// Two-way binds a UI control to a store value.
        /// <list type="bullet">
        ///   <item><description>Resolves a <see cref="BaseField{TValue}"/> named
        ///   <paramref name="elementName"/> from <see cref="Root"/>.</description></item>
        ///   <item><description>Pushes the initial store value to the field via
        ///   <c>SetValueWithoutNotify</c> (does not trigger a change event).</description></item>
        ///   <item><description>Registers a <see cref="ChangeEvent{TValue}"/> callback that
        ///   writes the new value to the store via <paramref name="setter"/>, guarded by an
        ///   equality check (<see cref="EqualityComparer{T}.Default"/>) to prevent feedback loops.
        ///   </description></item>
        ///   <item><description>Adds the unregister disposable to
        ///   <see cref="Subscriptions"/> automatically.</description></item>
        /// </list>
        /// <para><b>v1 limitation:</b> This overload does <em>not</em> push future external store
        /// changes back to the field. If you need that, add a separate subscription:
        /// <code>Subscriptions.Add(store.Subscribe(s => s.Value, v => field.SetValueWithoutNotify(v)));</code>
        /// </para>
        /// </summary>
        /// <typeparam name="TValue">The value type of the control (e.g. <c>string</c>, <c>float</c>, <c>bool</c>).</typeparam>
        /// <param name="elementName">The USS name of the field element.</param>
        /// <param name="getter">Returns the current store value to push to the control.</param>
        /// <param name="setter">Called with the new value when the user changes the control.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unregisters the change callback early if needed;
        /// already added to <see cref="Subscriptions"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no <see cref="BaseField{TValue}"/> with the given name is found in <see cref="Root"/>.
        /// </exception>
        protected IDisposable BindValue<TValue>(string elementName, Func<TValue> getter, Action<TValue> setter)
        {
            var field = Root.Q<BaseField<TValue>>(elementName);
            if (field == null)
                throw new InvalidOperationException(
                    $"[{GetType().Name}] BindValue<{typeof(TValue).Name}>: no BaseField<{typeof(TValue).Name}> named '{elementName}' found in the panel root.");

            return BindValue(field, getter, setter);
        }

        /// <summary>
        /// Two-way binds a UI control to a store value (by-element overload).
        /// See <see cref="BindValue{TValue}(string,Func{TValue},Action{TValue})"/> for full details.
        /// <para><b>v1 limitation:</b> Does not push future external store changes back to the field.
        /// Use a separate <c>store.Subscribe(..., v => field.SetValueWithoutNotify(v))</c> if needed.</para>
        /// </summary>
        /// <typeparam name="TValue">The value type of the control.</typeparam>
        /// <param name="field">The field element (must not be null).</param>
        /// <param name="getter">Returns the current store value to push to the control.</param>
        /// <param name="setter">Called with the new value when the user changes the control.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unregisters the change callback early if needed;
        /// already added to <see cref="Subscriptions"/>.
        /// </returns>
        protected IDisposable BindValue<TValue>(BaseField<TValue> field, Func<TValue> getter, Action<TValue> setter)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (getter == null)
                throw new ArgumentNullException(nameof(getter));
            if (setter == null)
                throw new ArgumentNullException(nameof(setter));

            // Initial store → field push (does not fire a ChangeEvent).
            field.SetValueWithoutNotify(getter());

            // Capture the callback as a named delegate so that the same instance is passed to
            // both RegisterCallback and UnregisterCallback (delegate equality is used for removal).
            EventCallback<ChangeEvent<TValue>> onChange = evt =>
            {
                // Guard: skip if the incoming value is already equal to the store value.
                // Uses EqualityComparer<TValue>.Default — exactly as Store.SetProperty does —
                // to prevent redundant store writes and infinite ping-pong.
                if (EqualityComparer<TValue>.Default.Equals(evt.newValue, getter()))
                    return;

                setter(evt.newValue);
            };

            field.RegisterCallback(onChange);

            var disposable = new CallbackDisposable(() => field.UnregisterCallback(onChange));
            Subscriptions.Add(disposable);
            return disposable;
        }

        /// <summary>
        /// Wires a <see cref="Button"/> to invoke a named command via
        /// <see cref="MosaicUI.Commands"/> when clicked.
        /// The click handler and its unhook are auto-added to <see cref="Subscriptions"/>.
        /// </summary>
        /// <param name="elementName">The USS name of the button element.</param>
        /// <param name="commandId">The command id to invoke (must be registered before the button is clicked).</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unwires the handler early if needed;
        /// already added to <see cref="Subscriptions"/>.
        /// </returns>
        protected IDisposable BindCommand(string elementName, string commandId)
        {
            return BindClick(elementName, () => Commands.Invoke(commandId));
        }

        /// <summary>
        /// Wires a <see cref="Button"/> to invoke a named command with a typed payload via
        /// <see cref="MosaicUI.Commands"/> when clicked.
        /// The click handler and its unhook are auto-added to <see cref="Subscriptions"/>.
        /// </summary>
        /// <typeparam name="T">The payload type expected by the registered command handler.</typeparam>
        /// <param name="elementName">The USS name of the button element.</param>
        /// <param name="commandId">The command id to invoke.</param>
        /// <param name="payload">Factory called at click-time to produce the payload value.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that unwires the handler early if needed;
        /// already added to <see cref="Subscriptions"/>.
        /// </returns>
        protected IDisposable BindCommand<T>(string elementName, string commandId, Func<T> payload)
        {
            return BindClick(elementName, () => Commands.Invoke(commandId, payload()));
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
        /// This is the device sibling of <see cref="BindCommand(string,string)"/>: where
        /// <c>BindCommand</c> routes a UI button click to a command, <c>MapAction</c> routes a
        /// device action (keyboard, gamepad) to the <b>same</b> command — both call
        /// <see cref="CommandRegistry.Invoke(string)"/> identically. Default phase is
        /// <c>performed</c>.
        /// </para>
        /// </summary>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="commandId">
        /// The command id to invoke when the action is performed. Must not be null or empty.
        /// </param>
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
        /// <param name="commandId">
        /// The command id to invoke when the action is performed. Must not be null or empty.
        /// </param>
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
