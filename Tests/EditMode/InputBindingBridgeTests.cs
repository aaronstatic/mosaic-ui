using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// EditMode contract tests for the <c>BindAction*</c> / <c>ReadAction</c> helpers on
    /// <see cref="PanelController"/> (Phase 1, T1.3).
    ///
    /// <para><b>Combining two patterns:</b></para>
    /// <list type="bullet">
    ///   <item><description>
    ///     The bare <c>TestPanelController</c> pattern from <see cref="PanelControllerBindTests"/>:
    ///     a minimal subclass that exposes the <c>protected</c> helpers as public pass-throughs,
    ///     with <c>Root</c> set to a hand-built <see cref="VisualElement"/> tree.
    ///   </description></item>
    ///   <item><description>
    ///     The <see cref="InputTestFixture"/> pattern from <see cref="InputServiceTests"/>:
    ///     a code-built <see cref="InputActionAsset"/> with a <c>Player</c> map / <c>Jump</c>
    ///     (button) + <c>Move</c> (Vector2) actions; <see cref="MosaicUI.Initialize"/> and
    ///     <c>MosaicUI.Input.SetAsset</c> called in a <b>distinctly-named</b>
    ///     <c>[SetUp]</c> after <c>base.Setup()</c> so the InputSystem reset is not hidden.
    ///   </description></item>
    /// </list>
    ///
    /// <para><b>Project-memory gotchas baked in:</b></para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>[SetUp]</c> / <c>[TearDown]</c> are named <see cref="SetUpFixture"/> /
    ///     <see cref="TearDownFixture"/> — distinct from the base <c>Setup()</c> /
    ///     <c>TearDown()</c> — so the InputSystem reset is never hidden.
    ///   </description></item>
    ///   <item><description>
    ///     Collection counts are asserted directly (<c>Assert.That(x.Count, Is.EqualTo(n))</c>),
    ///     never via NUnit <c>Has.Count</c> (throws on array-backed collections).
    ///   </description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class InputBindingBridgeTests : InputTestFixture
    {
        // ── Test harness ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal concrete subclass that exposes the <c>protected</c> input helpers as public
        /// pass-throughs and allows the test to supply a <see cref="VisualElement"/> root.
        /// </summary>
        private class TestPanelController : PanelController
        {
            public IDisposable PublicBindActionStarted(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionStarted(actionName, handler);

            public IDisposable PublicBindActionPerformed(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionPerformed(actionName, handler);

            public IDisposable PublicBindActionCanceled(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionCanceled(actionName, handler);

            public TValue PublicReadAction<TValue>(string actionName) where TValue : struct
                => ReadAction<TValue>(actionName);

            public IDisposable PublicMapAction(string actionName, string commandId)
                => MapAction(actionName, commandId);

            public IDisposable PublicMapAction<T>(string actionName, string commandId, Func<InputAction.CallbackContext, T> payload)
                => MapAction(actionName, commandId, payload);
        }

        /// <summary>
        /// Minimal concrete <see cref="WorldController"/> (a MonoBehaviour) that exposes the
        /// <c>protected</c> input helpers as public pass-throughs so the world-base contract
        /// (Phase 2, T2.4) can be exercised. Created via
        /// <c>new GameObject().AddComponent&lt;TestWorldController&gt;()</c>.
        /// </summary>
        private class TestWorldController : WorldController
        {
            public IDisposable PublicBindActionPerformed(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionPerformed(actionName, handler);

            public IDisposable PublicBindActionStarted(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionStarted(actionName, handler);

            public IDisposable PublicBindActionCanceled(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionCanceled(actionName, handler);

            public TValue PublicReadAction<TValue>(string actionName) where TValue : struct
                => ReadAction<TValue>(actionName);
        }

        /// <summary>
        /// Minimal concrete <see cref="WorldFeature"/> (a MonoBehaviour) that exposes the
        /// <c>protected</c> input helpers as public pass-throughs so the world-base contract
        /// (Phase 2, T2.4) can be exercised.
        /// </summary>
        private class TestWorldFeature : WorldFeature
        {
            public IDisposable PublicBindActionPerformed(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionPerformed(actionName, handler);

            public IDisposable PublicBindActionStarted(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionStarted(actionName, handler);

            public IDisposable PublicBindActionCanceled(string actionName, Action<InputAction.CallbackContext> handler)
                => BindActionCanceled(actionName, handler);

            public TValue PublicReadAction<TValue>(string actionName) where TValue : struct
                => ReadAction<TValue>(actionName);
        }

        /// <summary>
        /// A test probe <see cref="IDisposable"/> that records whether it was disposed and runs a
        /// supplied callback at dispose time. Added to a controller's <c>Subscriptions</c> group to
        /// prove the disposal CONTRACT (e.g. that <c>Subscriptions.Dispose()</c> runs while
        /// <c>Services</c> is still non-null — i.e. before <c>Services = null</c>).
        /// </summary>
        private sealed class ProbeDisposable : IDisposable
        {
            private readonly Action _onDispose;
            public bool Disposed { get; private set; }

            public ProbeDisposable(Action onDispose = null)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                Disposed = true;
                _onDispose?.Invoke();
            }
        }

        // ── Fixture state ────────────────────────────────────────────────────────────

        private InputActionAsset _asset;
        private TestPanelController _controller;
        private Keyboard _keyboard;

        /// <summary>
        /// GameObjects created for world-base tests (MonoBehaviour controllers). Tracked so they can
        /// be torn down with <c>DestroyImmediate</c> in teardown — the world bases' own <c>Dispose()</c>
        /// calls <c>Destroy(gameObject)</c>, which is deferred (and logs a warning) in EditMode, so the
        /// tests deliberately avoid relying on it and clean up the GameObjects here instead.
        /// </summary>
        private readonly List<GameObject> _worldObjects = new();

        // ── SetUp / TearDown ─────────────────────────────────────────────────────────

        /// <summary>
        /// Named distinctly from <see cref="InputTestFixture.Setup"/> so BOTH run:
        /// NUnit calls the base <c>Setup()</c> first (resetting the InputSystem), then this.
        /// Builds the code asset, wires <see cref="MosaicUI.Input"/>, adds a virtual keyboard,
        /// and enables the <c>Player</c> map so actions can fire.
        /// </summary>
        [SetUp]
        public void SetUpFixture()
        {
            // Build the fixture asset:
            //   Map:    "Player"
            //   Action: "Jump"  — Button, bound to <Keyboard>/space
            //   Action: "Move"  — Value/Vector2, bound to <Keyboard>/wasd composite
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();

            var playerMap = _asset.AddActionMap("Player");

            var jumpAction = playerMap.AddAction("Jump", InputActionType.Button);
            jumpAction.AddBinding("<Keyboard>/space");

            var moveAction = playerMap.AddAction("Move", InputActionType.Value);
            // Use a 2D vector composite (WASD) bound to keyboard for both directions.
            var composite = moveAction.AddCompositeBinding("2DVector");
            composite.With("Up",    "<Keyboard>/w");
            composite.With("Down",  "<Keyboard>/s");
            composite.With("Left",  "<Keyboard>/a");
            composite.With("Right", "<Keyboard>/d");

            // Initialize MosaicUI and assign the asset AFTER base.Setup() has reset the InputSystem,
            // so the InputUser.onChange hook on InputService is attached to the clean state.
            MosaicUI.Initialize();
            MosaicUI.Input.SetAsset(_asset);
            MosaicUI.Input.EnableMap("Player");

            // Add a virtual keyboard device so we can drive actions in tests.
            _keyboard = InputSystem.AddDevice<Keyboard>();

            // Build the panel controller with a minimal Root so Bind* helpers can resolve elements.
            _controller = new TestPanelController
            {
                Root = new VisualElement { name = "root" }
            };
        }

        /// <summary>
        /// Named distinctly from <see cref="InputTestFixture.TearDown"/> so BOTH run:
        /// this derived teardown runs first (disposing the controller, shutting down MosaicUI,
        /// destroying the asset), then the base <c>TearDown()</c> restores the InputSystem.
        /// </summary>
        [TearDown]
        public void TearDownFixture()
        {
            _controller?.Dispose();
            _controller = null;

            // Tear down any world-base GameObjects created during the test. DestroyImmediate is the
            // EditMode-safe destructor (the world bases' own Dispose() uses Destroy(), which is
            // deferred + warns in edit mode). Null entries are skipped (already destroyed).
            foreach (var go in _worldObjects)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            _worldObjects.Clear();

            MosaicUI.Shutdown();

            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset);
                _asset = null;
            }

            // _keyboard is managed by InputTestFixture (added via InputSystem.AddDevice — the base
            // TearDown resets the InputSystem which removes all devices). No explicit removal needed.
            _keyboard = null;
        }

        // ── T1.3 Tests — BindActionPerformed fires when driven ────────────────────────

        [Test]
        public void BindActionPerformed_firesWhenActionDriven()
        {
            int callCount = 0;
            _controller.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            // Drive the action: press (starts+performs the button), then release (cancels).
            Press(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "BindActionPerformed callback must fire exactly once when the action is performed.");
        }

        [Test]
        public void BindActionStarted_firesOnStarted()
        {
            int callCount = 0;
            _controller.PublicBindActionStarted("Player/Jump", _ => callCount++);

            // A button fires 'started' immediately on press.
            Press(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "BindActionStarted callback must fire exactly once when the action starts.");
        }

        [Test]
        public void BindActionCanceled_firesOnRelease()
        {
            int callCount = 0;
            _controller.PublicBindActionCanceled("Player/Jump", _ => callCount++);

            // Press to start/perform, then release to cancel.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "BindActionCanceled callback must fire exactly once when the action is canceled.");
        }

        // ── T1.3 Tests — Dispose stops callbacks (leak-fix guarantee) ─────────────────

        [Test]
        public void DisposeSubscriptions_stopsActionCallback()
        {
            int callCount = 0;
            _controller.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            // Confirm it fires before dispose.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "Expected 1 callback before dispose.");

            // Dispose all subscriptions (the dispose-stops-firing / leak-fix guarantee).
            _controller.Subscriptions.Dispose();

            // Drive the action again — callback must NOT fire.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "BindAction callback must NOT fire after Subscriptions.Dispose() (no leaked InputSystem handler).");
        }

        [Test]
        public void DisposeToken_stopsCallback_earlyDispose()
        {
            int callCount = 0;
            var token = _controller.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            // Confirm it fires once.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1));

            // Early-dispose the individual token (not via Subscriptions.Dispose).
            token.Dispose();

            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "Callback must not fire after the individual token is disposed early.");
        }

        // ── T1.3 Tests — Bad action name throws ──────────────────────────────────────

        [Test]
        public void BindAction_badActionName_throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => _controller.PublicBindActionPerformed("Player/DoesNotExist", _ => { }),
                "A bad action name must throw InvalidOperationException (propagated from InputService).");
        }

        [Test]
        public void BindAction_nullCallback_throwsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => _controller.PublicBindActionPerformed("Player/Jump", null),
                "A null callback must throw ArgumentNullException.");
        }

        // ── T1.3 Tests — ReadAction returns driven value ──────────────────────────────

        [Test]
        public void ReadAction_returnsDrivenValue()
        {
            // Drive the Move composite: hold 'd' for rightward Vector2.
            // Use Press (sets the key held) so the composite reads during Update.
            Press(_keyboard.dKey);

            var result = _controller.PublicReadAction<Vector2>("Player/Move");

            // X should be positive (right), Y should be ~ 0.
            Assert.That(result.x, Is.GreaterThan(0f),
                "ReadAction<Vector2> x component must be positive when 'd' is held (rightward input).");

            Release(_keyboard.dKey);
        }

        // ── T1.3 Tests — Token is in Subscriptions group ─────────────────────────────

        [Test]
        public void BindActionPerformed_addsTokenToSubscriptions_disposeSucceeds()
        {
            _controller.PublicBindActionPerformed("Player/Jump", _ => { });

            // The token was added to Subscriptions — Dispose must complete without error.
            Assert.DoesNotThrow(() => _controller.Subscriptions.Dispose(),
                "Subscriptions.Dispose() must succeed after BindActionPerformed adds a token.");
        }

        [Test]
        public void BindActionPerformed_returnsNonNullToken()
        {
            var token = _controller.PublicBindActionPerformed("Player/Jump", _ => { });

            Assert.That(token, Is.Not.Null,
                "BindActionPerformed must return a non-null IDisposable token.");
        }

        // ── T2.4 Factories — world-base controllers (MonoBehaviours) ──────────────────

        /// <summary>
        /// Creates a <see cref="TestWorldController"/> on a tracked GameObject and calls
        /// <c>Initialize(MosaicUI.Services)</c> so <c>Services</c> is non-null (needed by the
        /// ordering probe). The GameObject is registered for <c>DestroyImmediate</c> in teardown.
        /// </summary>
        private TestWorldController NewWorldController()
        {
            var go = new GameObject("twc");
            _worldObjects.Add(go);
            var controller = go.AddComponent<TestWorldController>();
            controller.Initialize(MosaicUI.Services);
            return controller;
        }

        /// <summary>
        /// Creates a <see cref="TestWorldFeature"/> on a tracked GameObject and calls
        /// <c>Initialize(MosaicUI.Services)</c>. The GameObject is registered for
        /// <c>DestroyImmediate</c> in teardown.
        /// </summary>
        private TestWorldFeature NewWorldFeature()
        {
            var go = new GameObject("twf");
            _worldObjects.Add(go);
            var feature = go.AddComponent<TestWorldFeature>();
            feature.Initialize(MosaicUI.Services);
            return feature;
        }

        // ── T2.4 Tests — WorldController BindAction fires + auto-disposes ──────────────

        [Test]
        public void WorldController_BindActionPerformed_firesWhenActionDriven()
        {
            var controller = NewWorldController();

            int callCount = 0;
            controller.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            Press(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "WorldController BindActionPerformed callback must fire once when the action is performed.");
        }

        [Test]
        public void WorldController_DisposeSubscriptions_stopsActionCallback()
        {
            var controller = NewWorldController();

            int callCount = 0;
            controller.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            // Fires before dispose.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "Expected 1 callback before dispose.");

            // Dispose ONLY the Subscriptions group (NOT the full Dispose(), which would call
            // Destroy(gameObject) — deferred/noisy in EditMode). This proves the leak-fix: the
            // InputSystem handler is detached and the action no longer reaches the controller.
            controller.Subscriptions.Dispose();

            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "WorldController BindAction callback must NOT fire after Subscriptions.Dispose() (no leaked handler).");
        }

        [Test]
        public void WorldController_SubscriptionsDispose_disposesProbeToken()
        {
            var controller = NewWorldController();

            var probe = new ProbeDisposable();
            controller.Subscriptions.Add(probe);

            Assert.That(probe.Disposed, Is.False, "Probe must not be disposed before Subscriptions.Dispose().");

            controller.Subscriptions.Dispose();

            Assert.That(probe.Disposed, Is.True,
                "A probe added to Subscriptions must be disposed by Subscriptions.Dispose().");
        }

        [Test]
        public void WorldController_Dispose_disposesSubscriptions_beforeNullingServices()
        {
            var controller = NewWorldController();

            // Sanity: Services was set by Initialize().
            Assert.That(controller.Services, Is.Not.Null, "Services must be set after Initialize().");

            // Probe reads Services at the moment it is disposed (inside Subscriptions.Dispose()).
            // Because Dispose() calls Subscriptions.Dispose() BEFORE setting Services = null, this
            // captures a non-null Services — proving the ordering guarantee (Decision 3).
            ServiceRegistry servicesAtProbeDispose = null;
            var probe = new ProbeDisposable(() => servicesAtProbeDispose = controller.Services);
            controller.Subscriptions.Add(probe);

            // Full Dispose() runs Destroy(gameObject), which is deferred and warns in EditMode.
            // Ignore that expected noise so the ordering assertion stays deterministic. The Destroy
            // leg itself is covered by the play-mode scene (P6).
            LogAssert.ignoreFailingMessages = true;
            try
            {
                controller.Dispose();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(probe.Disposed, Is.True, "The probe must have been disposed during Dispose().");
            Assert.That(servicesAtProbeDispose, Is.Not.Null,
                "Subscriptions must be disposed BEFORE Services is nulled: the probe saw a live Services.");
            Assert.That(controller.Services, Is.Null,
                "After Dispose(), Services must be null (nulled AFTER Subscriptions.Dispose()).");
        }

        [Test]
        public void WorldController_ReadAction_returnsDrivenValue()
        {
            var controller = NewWorldController();

            Press(_keyboard.dKey);

            var result = controller.PublicReadAction<Vector2>("Player/Move");

            Assert.That(result.x, Is.GreaterThan(0f),
                "WorldController ReadAction<Vector2> x must be positive when 'd' is held.");

            Release(_keyboard.dKey);
        }

        [Test]
        public void WorldController_BindAction_badActionName_throws()
        {
            var controller = NewWorldController();

            Assert.Throws<InvalidOperationException>(
                () => controller.PublicBindActionPerformed("Player/DoesNotExist", _ => { }),
                "A bad action name must throw InvalidOperationException (propagated from InputService).");
        }

        // ── T2.4 Tests — WorldFeature BindAction fires + auto-disposes ─────────────────

        [Test]
        public void WorldFeature_BindActionPerformed_firesWhenActionDriven()
        {
            var feature = NewWorldFeature();

            int callCount = 0;
            feature.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            Press(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "WorldFeature BindActionPerformed callback must fire once when the action is performed.");
        }

        [Test]
        public void WorldFeature_DisposeSubscriptions_stopsActionCallback()
        {
            var feature = NewWorldFeature();

            int callCount = 0;
            feature.PublicBindActionPerformed("Player/Jump", _ => callCount++);

            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "Expected 1 callback before dispose.");

            feature.Subscriptions.Dispose();

            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1),
                "WorldFeature BindAction callback must NOT fire after Subscriptions.Dispose() (no leaked handler).");
        }

        [Test]
        public void WorldFeature_SubscriptionsDispose_disposesProbeToken()
        {
            var feature = NewWorldFeature();

            var probe = new ProbeDisposable();
            feature.Subscriptions.Add(probe);

            Assert.That(probe.Disposed, Is.False, "Probe must not be disposed before Subscriptions.Dispose().");

            feature.Subscriptions.Dispose();

            Assert.That(probe.Disposed, Is.True,
                "A probe added to Subscriptions must be disposed by Subscriptions.Dispose().");
        }

        [Test]
        public void WorldFeature_Dispose_disposesSubscriptions_beforeNullingServices()
        {
            var feature = NewWorldFeature();

            Assert.That(feature.Services, Is.Not.Null, "Services must be set after Initialize().");

            ServiceRegistry servicesAtProbeDispose = null;
            var probe = new ProbeDisposable(() => servicesAtProbeDispose = feature.Services);
            feature.Subscriptions.Add(probe);

            LogAssert.ignoreFailingMessages = true;
            try
            {
                feature.Dispose();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(probe.Disposed, Is.True, "The probe must have been disposed during Dispose().");
            Assert.That(servicesAtProbeDispose, Is.Not.Null,
                "Subscriptions must be disposed BEFORE Services is nulled: the probe saw a live Services.");
            Assert.That(feature.Services, Is.Null,
                "After Dispose(), Services must be null (nulled AFTER Subscriptions.Dispose()).");
        }

        [Test]
        public void WorldFeature_BindAction_badActionName_throws()
        {
            var feature = NewWorldFeature();

            Assert.Throws<InvalidOperationException>(
                () => feature.PublicBindActionPerformed("Player/DoesNotExist", _ => { }),
                "A bad action name must throw InvalidOperationException (propagated from InputService).");
        }

        // ── T3.3 Tests — MapAction bridge (action → CommandRegistry command) ──────────

        /// <summary>
        /// Bridge fires the registered command when the action is driven via a virtual device.
        /// Proves that <c>MapAction</c> routes device input to <see cref="CommandRegistry"/> exactly
        /// as a UI button click via <c>BindCommand</c> would.
        /// </summary>
        [Test]
        public void MapAction_bridgeFiresCommand_whenActionDriven()
        {
            int counter = 0;
            // Register the command; Shutdown() in TearDownFixture calls Commands.Clear().
            MosaicUI.Commands.Register("jump", () => counter++);

            _controller.PublicMapAction("Player/Jump", "jump");

            // Drive the action: press fires 'performed' on a button action.
            Press(_keyboard.spaceKey);

            Assert.That(counter, Is.EqualTo(1),
                "MapAction must invoke the registered command exactly once when the action is performed.");
        }

        /// <summary>
        /// Device-driven invocation and direct <see cref="CommandRegistry.Invoke"/> produce identical
        /// state changes — the "same front door" proof (T3.3 requirement).
        /// </summary>
        [Test]
        public void MapAction_sameFrontDoor_directInvokeIdentical()
        {
            int counter = 0;
            MosaicUI.Commands.Register("jump", () => counter++);

            _controller.PublicMapAction("Player/Jump", "jump");

            // Drive via device (key press → action performed → Commands.Invoke("jump")).
            Press(_keyboard.spaceKey);
            Assert.That(counter, Is.EqualTo(1), "Device-driven path must increment counter to 1.");

            // Drive via direct command invocation — identical result; counter advances by exactly 1 more.
            MosaicUI.Commands.Invoke("jump");
            Assert.That(counter, Is.EqualTo(2),
                "Direct Commands.Invoke must produce the identical state change as the device-driven path.");
        }

        /// <summary>
        /// Disposing the controller's <see cref="SubscriptionGroup"/> stops the action→command mapping.
        /// Proves the leak-fix: after <c>Subscriptions.Dispose()</c> the InputSystem handler is detached
        /// and the command is no longer invoked when the action fires.
        /// </summary>
        [Test]
        public void MapAction_disposeStopsMapping()
        {
            int counter = 0;
            MosaicUI.Commands.Register("jump", () => counter++);

            _controller.PublicMapAction("Player/Jump", "jump");

            // Confirm the mapping fires before dispose.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);
            Assert.That(counter, Is.EqualTo(1), "Expected mapping to fire once before dispose.");

            // Dispose all subscriptions.
            _controller.Subscriptions.Dispose();

            // Drive the action again — the mapping must NOT fire.
            Press(_keyboard.spaceKey);
            Release(_keyboard.spaceKey);

            Assert.That(counter, Is.EqualTo(1),
                "MapAction must NOT invoke the command after Subscriptions.Dispose() (no leaked handler).");
        }

        /// <summary>
        /// The typed <c>MapAction&lt;T&gt;</c> overload delivers a payload derived from the
        /// <see cref="InputAction.CallbackContext"/> to the registered command handler.
        /// Uses a deterministic payload factory (<c>ctx => 42f</c>) to verify the delivery path
        /// without depending on the precise float value that a button's performed context returns.
        /// </summary>
        [Test]
        public void MapAction_typedPayload_deliveredToCommand()
        {
            float lastVal = -1f;
            MosaicUI.Commands.Register<float>("jumpVal", v => lastVal = v);

            // Use a deterministic payload factory so the test does not depend on the exact float
            // value a button action's performed context exposes via ReadValue<float>().
            _controller.PublicMapAction<float>("Player/Jump", "jumpVal", ctx => 42f);

            Press(_keyboard.spaceKey);

            Assert.That(lastVal, Is.EqualTo(42f),
                "MapAction<T> must deliver the payload factory result to the registered command handler.");
        }

        /// <summary>
        /// When a <c>MapAction</c> names an unregistered command id, the exception is thrown from
        /// inside the InputSystem action callback when the action fires — NOT at wiring time.
        ///
        /// <para><b>Nuance (why LogAssert.Expect, not Assert.Throws):</b>
        /// Unity's InputSystem catches exceptions thrown inside action callbacks and surfaces them as
        /// console logs — a <c>LogType.Error</c> wrapper plus the <c>LogType.Exception</c> itself —
        /// rather than propagating them out of <c>Press()</c>. As a result, <c>Assert.Throws</c> around
        /// <c>Press()</c> would NOT catch the exception — it returns cleanly while both logs appear.
        /// <c>LogAssert.Expect</c> (for each emitted log, in order) is the correct assertion for this
        /// pattern: the test passes when the expected logs match.</para>
        /// </summary>
        [Test]
        public void MapAction_unregisteredCommandId_throwsOnFire_loggedAsException()
        {
            // "nope" is NOT registered — the error must surface when the action fires, not at wiring.
            _controller.PublicMapAction("Player/Jump", "nope");

            // The InvalidOperationException thrown inside the InputSystem callback surfaces as TWO
            // console logs (verified emission order): the LogType.Exception itself
            // ("InvalidOperationException: Command 'nope' is not registered.") FIRST, then a
            // LogType.Error wrapper ("InvalidOperationException while executing 'performed'
            // callbacks of ..."). LogAssert enforces order, so declare them in that order BEFORE driving.
            LogAssert.Expect(LogType.Exception, new Regex("Command 'nope' is not registered"));
            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException while executing"));

            // Pressing the key fires the action; the unregistered-id exception is caught by the
            // InputSystem and logged — it does NOT propagate out of Press().
            Press(_keyboard.spaceKey);

            // LogAssert verifies the expected log was emitted. Test passes when the message matched.
        }
    }
}
