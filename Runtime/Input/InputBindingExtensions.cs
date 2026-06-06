using System;
using UnityEngine.InputSystem;

namespace Mosaic.UI
{
    /// <summary>
    /// Shared implementation of <c>BindAction*</c> helpers for all controller bases
    /// (<see cref="PanelController"/>, and the world bases in Phase 2).
    /// <para>
    /// Each base's <c>protected</c> method is a one-line forward to these static methods,
    /// passing <c>this.Subscriptions</c>. This avoids duplicating identical bodies across
    /// three classes that share no common base class (<see cref="PanelController"/> is a plain C#
    /// class; <c>WorldController</c>/<c>WorldFeature</c> are MonoBehaviours).
    /// </para>
    /// <para>
    /// Validation strategy: <paramref name="callback"/> null is checked here with
    /// <see cref="ArgumentNullException"/>. Action-name resolution and "no asset assigned" are
    /// <em>not</em> re-wrapped — they propagate from <see cref="InputService"/> as-is so the
    /// house-style <see cref="InvalidOperationException"/> message (naming the action) reaches
    /// the caller unchanged.
    /// </para>
    /// </summary>
    internal static class InputBindingExtensions
    {
        // ── BindAction* ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribes <paramref name="callback"/> to the <c>started</c> phase of the named action
        /// via <see cref="MosaicUI.Input"/>, and adds the returned token to
        /// <paramref name="subscriptions"/> for automatic disposal.
        /// </summary>
        /// <param name="subscriptions">The group that owns the token (typically <c>this.Subscriptions</c>).</param>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="callback">The handler to invoke when the action starts. Must not be null.</param>
        /// <returns>
        /// The <see cref="IDisposable"/> token already added to <paramref name="subscriptions"/>;
        /// dispose it early only if needed — the group will dispose it on teardown.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        internal static IDisposable BindActionStarted(
            SubscriptionGroup subscriptions,
            string actionName,
            Action<InputAction.CallbackContext> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var token = MosaicUI.Input.SubscribeStarted(actionName, callback);
            subscriptions.Add(token);
            return token;
        }

        /// <summary>
        /// Subscribes <paramref name="callback"/> to the <c>performed</c> phase of the named action
        /// via <see cref="MosaicUI.Input"/>, and adds the returned token to
        /// <paramref name="subscriptions"/> for automatic disposal.
        /// </summary>
        /// <param name="subscriptions">The group that owns the token (typically <c>this.Subscriptions</c>).</param>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="callback">The handler to invoke when the action is performed. Must not be null.</param>
        /// <returns>
        /// The <see cref="IDisposable"/> token already added to <paramref name="subscriptions"/>;
        /// dispose it early only if needed — the group will dispose it on teardown.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        internal static IDisposable BindActionPerformed(
            SubscriptionGroup subscriptions,
            string actionName,
            Action<InputAction.CallbackContext> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var token = MosaicUI.Input.SubscribePerformed(actionName, callback);
            subscriptions.Add(token);
            return token;
        }

        /// <summary>
        /// Subscribes <paramref name="callback"/> to the <c>canceled</c> phase of the named action
        /// via <see cref="MosaicUI.Input"/>, and adds the returned token to
        /// <paramref name="subscriptions"/> for automatic disposal.
        /// </summary>
        /// <param name="subscriptions">The group that owns the token (typically <c>this.Subscriptions</c>).</param>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="callback">The handler to invoke when the action is canceled. Must not be null.</param>
        /// <returns>
        /// The <see cref="IDisposable"/> token already added to <paramref name="subscriptions"/>;
        /// dispose it early only if needed — the group will dispose it on teardown.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        internal static IDisposable BindActionCanceled(
            SubscriptionGroup subscriptions,
            string actionName,
            Action<InputAction.CallbackContext> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var token = MosaicUI.Input.SubscribeCanceled(actionName, callback);
            subscriptions.Add(token);
            return token;
        }

        // ── MapAction* ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribes the <c>performed</c> phase of the named action to invoke the named command
        /// via <see cref="MosaicUI.Commands"/> (parameterless overload), and adds the returned token
        /// to <paramref name="subscriptions"/> for automatic disposal.
        /// <para>
        /// The command identified by <paramref name="commandId"/> need not be registered at the time
        /// <c>MapAction</c> is called — this mirrors <c>BindCommand</c>. An unregistered id will throw
        /// <see cref="InvalidOperationException"/> from <see cref="CommandRegistry.Invoke"/> when the
        /// action fires (not at wiring time).
        /// </para>
        /// <para>Default phase is <c>performed</c> — the discrete "it happened" moment, the device
        /// analogue of a UI button click.</para>
        /// </summary>
        /// <param name="subscriptions">The group that owns the token (typically <c>this.Subscriptions</c>).</param>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="commandId">
        /// The command id to invoke when the action is performed. Must not be null or empty.
        /// </param>
        /// <returns>
        /// The <see cref="IDisposable"/> token already added to <paramref name="subscriptions"/>;
        /// dispose it early only if needed — the group will dispose it on teardown.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commandId"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="commandId"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        internal static IDisposable MapAction(
            SubscriptionGroup subscriptions,
            string actionName,
            string commandId)
        {
            ValidateCommandId(commandId);

            var token = MosaicUI.Input.SubscribePerformed(actionName, _ => MosaicUI.Commands.Invoke(commandId));
            subscriptions.Add(token);
            return token;
        }

        /// <summary>
        /// Subscribes the <c>performed</c> phase of the named action to invoke the named command
        /// via <see cref="MosaicUI.Commands"/> with a typed payload derived from the
        /// <see cref="InputAction.CallbackContext"/>, and adds the returned token to
        /// <paramref name="subscriptions"/> for automatic disposal.
        /// <para>
        /// The command identified by <paramref name="commandId"/> need not be registered at the time
        /// <c>MapAction</c> is called — unregistered ids throw at fire-time, not wiring-time.
        /// </para>
        /// <para>Default phase is <c>performed</c>.</para>
        /// </summary>
        /// <typeparam name="T">The payload type expected by the registered command handler.</typeparam>
        /// <param name="subscriptions">The group that owns the token (typically <c>this.Subscriptions</c>).</param>
        /// <param name="actionName">Qualified action name, e.g. <c>"Player/Jump"</c>.</param>
        /// <param name="commandId">
        /// The command id to invoke when the action is performed. Must not be null or empty.
        /// </param>
        /// <param name="payload">
        /// Factory called with the <see cref="InputAction.CallbackContext"/> at fire-time to produce
        /// the payload value. Must not be null.
        /// </param>
        /// <returns>
        /// The <see cref="IDisposable"/> token already added to <paramref name="subscriptions"/>;
        /// dispose it early only if needed — the group will dispose it on teardown.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="commandId"/> or <paramref name="payload"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="commandId"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">
        /// Propagated from <see cref="InputService"/> when no asset is assigned or the action name
        /// is not found in the asset.
        /// </exception>
        internal static IDisposable MapAction<T>(
            SubscriptionGroup subscriptions,
            string actionName,
            string commandId,
            Func<InputAction.CallbackContext, T> payload)
        {
            ValidateCommandId(commandId);
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var token = MosaicUI.Input.SubscribePerformed(actionName, ctx => MosaicUI.Commands.Invoke(commandId, payload(ctx)));
            subscriptions.Add(token);
            return token;
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Validates a command id using the same rules as <see cref="CommandRegistry"/>:
        /// must be non-null and non-empty.
        /// </summary>
        private static void ValidateCommandId(string commandId)
        {
            if (commandId == null)
                throw new ArgumentNullException(nameof(commandId));
            if (commandId.Length == 0)
                throw new ArgumentException("Command id must not be empty.", nameof(commandId));
        }
    }
}
