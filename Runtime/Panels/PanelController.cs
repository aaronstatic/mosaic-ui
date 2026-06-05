using System;
using System.Collections.Generic;
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
