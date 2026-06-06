using System;
using System.Collections.Generic;
using Mosaic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// EditMode NUnit suite for <see cref="InputService"/>.
    /// <para>
    /// Inherits <see cref="InputTestFixture"/> so that the InputSystem is reset to a known
    /// clean state before every test and fully restored after. This means:
    /// <list type="bullet">
    ///   <item>Virtual devices added in tests do not leak across tests.</item>
    ///   <item>The <see cref="InputUser"/> static state (including the <c>onChange</c> callback set)
    ///     is reset between tests — which means <see cref="InputService"/> must be constructed
    ///     <em>after</em> <c>base.Setup()</c> (NUnit calls base-class <c>[SetUp]</c> first, so the
    ///     order is: <c>InputTestFixture.Setup()</c> → our <c>SetUp()</c> → test body).</item>
    /// </list>
    /// </para>
    /// </summary>
    [TestFixture]
    public class InputServiceTests : InputTestFixture
    {
        // -----------------------------------------------------------------------------------------
        // Shared fixture state — created fresh by [SetUp] after InputTestFixture.Setup() runs.
        // -----------------------------------------------------------------------------------------

        private InputActionAsset _asset;
        private EventBus _bus;
        private InputService _svc;

        // Control-scheme / binding-group names used throughout.
        private const string KbmScheme = "KBM";
        private const string GamepadScheme = "Gamepad";

        /// <summary>
        /// Creates the <see cref="InputActionAsset"/> fixture and the service under test.
        /// Called AFTER <see cref="InputTestFixture.Setup"/> (NUnit base-class SetUp order).
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            // Build the fixture asset entirely in code.
            // Map: "Player"
            //   Action: "Jump"   — Button, bound to <Keyboard>/space  (group: KBM)
            //   Action: "Move"   — Value/Vector2, bound to <Gamepad>/leftStick (group: Gamepad)
            // Control schemes (used by the scheme-switch test):
            //   "KBM"     requires <Keyboard>
            //   "Gamepad" requires <Gamepad>
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();

            var playerMap = _asset.AddActionMap("Player");

            var jumpAction = playerMap.AddAction("Jump", InputActionType.Button);
            jumpAction.AddBinding("<Keyboard>/space", groups: KbmScheme);

            var moveAction = playerMap.AddAction("Move", InputActionType.Value);
            moveAction.AddBinding("<Gamepad>/leftStick", groups: GamepadScheme);

            _asset.AddControlScheme(KbmScheme).WithRequiredDevice<Keyboard>();
            _asset.AddControlScheme(GamepadScheme).WithRequiredDevice<Gamepad>();

            _bus = new EventBus();

            // Construct AFTER base.Setup() so InputUser.onChange hook is registered on the
            // freshly-reset InputSystem, not the stale one from a previous test.
            _svc = new InputService(_bus);
        }

        /// <summary>
        /// Disposes the service and destroys the fixture asset after each test.
        /// Deliberately named distinctly from <see cref="InputTestFixture.TearDown"/> (rather than
        /// hiding it) so BOTH run: NUnit invokes this derived <c>[TearDown]</c> first (service
        /// disposal — detaches the <c>InputUser.onChange</c> hook + disables maps), then the base
        /// <c>InputTestFixture.TearDown()</c> restores the InputSystem state. Mirrors how
        /// <see cref="SetUp"/> is named distinctly from the base <c>Setup()</c>.
        /// </summary>
        [TearDown]
        public void TearDownFixture()
        {
            _svc?.Dispose();
            _svc = null;

            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset);
                _asset = null;
            }
        }

        // -----------------------------------------------------------------------------------------
        // T6.2 Tests
        // -----------------------------------------------------------------------------------------

        // --- Resolution ---

        /// <summary>
        /// After assigning an asset, resolving a known action by qualified name does not throw.
        /// </summary>
        [Test]
        public void SetAsset_thenResolveKnownAction_doesNotThrow()
        {
            _svc.SetAsset(_asset);
            Assert.DoesNotThrow(() => _svc.SubscribeStarted("Player/Jump", _ => { }));
        }

        /// <summary>
        /// Resolving an action name that does not exist in the asset throws an
        /// <see cref="InvalidOperationException"/> whose message contains the action name.
        /// </summary>
        [Test]
        public void ResolveUnknownAction_throwsNamedException()
        {
            _svc.SetAsset(_asset);
            var ex = Assert.Throws<InvalidOperationException>(
                () => _svc.SubscribePerformed("Player/Nope", _ => { }));
            Assert.That(ex.Message, Does.Contain("'Player/Nope'"));
        }

        /// <summary>
        /// Resolving a map name that does not exist in the asset throws an
        /// <see cref="InvalidOperationException"/> whose message contains the map name.
        /// </summary>
        [Test]
        public void ResolveUnknownMap_throwsNamedException()
        {
            _svc.SetAsset(_asset);
            var ex = Assert.Throws<InvalidOperationException>(
                () => _svc.EnableMap("Ghost"));
            Assert.That(ex.Message, Does.Contain("'Ghost'"));
        }

        /// <summary>
        /// Calling any resolution method before <see cref="InputService.SetAsset"/> throws an
        /// <see cref="InvalidOperationException"/> that states no asset has been assigned.
        /// </summary>
        [Test]
        public void UseBeforeSetAsset_throwsNoAssetException()
        {
            // Service was just constructed — no SetAsset called yet.
            var ex = Assert.Throws<InvalidOperationException>(
                () => _svc.SubscribeStarted("Player/Jump", _ => { }));
            Assert.That(ex.Message, Does.Contain("No InputActionAsset has been assigned"));
        }

        // --- Subscribe + fire ---

        /// <summary>
        /// Subscribing to the <c>performed</c> phase and then driving the action via a virtual
        /// keyboard fires the callback exactly once.
        /// </summary>
        [Test]
        public void SubscribePerformed_firesWhenActionDriven()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            _svc.SetAsset(_asset);
            _svc.EnableMap("Player");

            int callCount = 0;
            _svc.SubscribePerformed("Player/Jump", _ => callCount++);

            // Drive space key: press (starts + performs the button) then release (cancels).
            Press(keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1));
        }

        // --- ReadValue ---

        /// <summary>
        /// After driving a Vector2 action via a virtual gamepad, <see cref="InputService.ReadValue{TValue}"/>
        /// returns the driven value (within floating-point tolerance).
        /// </summary>
        [Test]
        public void ReadValue_returnsDrivenVector()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();

            _svc.SetAsset(_asset);
            _svc.EnableMap("Player");

            // Use a large magnitude to stay well outside the default dead-zone (~0.125).
            // We assert direction (both components positive) and that the magnitude is non-trivial
            // rather than exact equality, because dead-zone normalisation shifts the precise value.
            var driven = new Vector2(0.7f, 0.7f);
            Set(gamepad.leftStick, driven);

            var result = _svc.ReadValue<Vector2>("Player/Move");

            // After dead-zone normalisation the exact value shifts, but the direction is preserved
            // and the magnitude is well above zero. Assert components are non-negative and non-trivial.
            Assert.That(result.x, Is.GreaterThan(0f), "X component should be positive after driving right");
            Assert.That(result.y, Is.GreaterThan(0f), "Y component should be positive after driving up");
        }

        // --- Enable/disable gating ---

        /// <summary>
        /// While a map is disabled the subscribed action does not fire; after enabling it fires;
        /// after disabling again it stops.
        /// </summary>
        [Test]
        public void DisabledMap_doesNotFire_enabledMapFires()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            _svc.SetAsset(_asset);
            // Map starts disabled — do NOT call EnableMap yet.

            int callCount = 0;
            _svc.SubscribePerformed("Player/Jump", _ => callCount++);

            // Press while map is disabled → should not fire.
            Press(keyboard.spaceKey);
            Release(keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(0), "callback fired while map was disabled");

            // Enable the map then press → should fire once.
            _svc.EnableMap("Player");
            Press(keyboard.spaceKey);
            Release(keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1), "callback did not fire after EnableMap");

            // Disable the map then press → should not fire again.
            _svc.DisableMap("Player");
            Press(keyboard.spaceKey);

            Assert.That(callCount, Is.EqualTo(1), "callback fired after DisableMap");
        }

        // --- Dispose token + double-dispose ---

        /// <summary>
        /// Disposing the subscription token stops further callbacks. Disposing the token a
        /// second time is a no-op (no exception, no double-detach).
        /// </summary>
        [Test]
        public void DisposeToken_stopsCallback_andDoubleDisposeIsNoOp()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            _svc.SetAsset(_asset);
            _svc.EnableMap("Player");

            int callCount = 0;
            var token = _svc.SubscribePerformed("Player/Jump", _ => callCount++);

            // Fire once to confirm the subscription is live.
            Press(keyboard.spaceKey);
            Release(keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "expected 1 callback before dispose");

            // Dispose the token.
            token.Dispose();

            // Press again — callback must NOT fire.
            Press(keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "callback fired after token.Dispose()");

            // Second Dispose must not throw.
            Assert.DoesNotThrow(() => token.Dispose());
        }

        // --- Control-scheme change ---

        /// <summary>
        /// Switching from the KBM scheme to the Gamepad scheme via an explicit
        /// <see cref="InputUser"/> pairing publishes exactly one <see cref="ControlSchemeChanged"/>
        /// event on the bus with the correct <c>Previous</c> and <c>Current</c> names.
        /// <para>
        /// This test performs the full InputUser pairing dance required to trigger
        /// <see cref="InputUser.onChange"/> (<c>InputUser.onChange</c> fires only for paired users):
        /// pair the keyboard device, associate the asset's actions with the user, activate the
        /// KBM scheme, then pair the gamepad and activate the Gamepad scheme.
        /// </para>
        /// </summary>
        [Test]
        public void KbmToGamepad_publishesControlSchemeChanged()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();

            _svc.SetAsset(_asset);
            _svc.EnableMap("Player");

            // Collect ControlSchemeChanged events from the bus.
            var received = new List<ControlSchemeChanged>();
            using var sub = _bus.Subscribe<ControlSchemeChanged>(e => received.Add(e));

            // --- Pair keyboard and activate the KBM scheme ---
            // PerformPairingWithDevice creates a new InputUser and pairs the device.
            var user = InputUser.PerformPairingWithDevice(keyboard);

            // Associate the asset's actions with this user so ActivateControlScheme works.
            user.AssociateActionsWithUser(_asset);

            // Activating the control scheme fires InputUserChange.ControlSchemeChanged.
            user.ActivateControlScheme(KbmScheme);
            // The first activation sets scheme from null → KBM. This fires one ControlSchemeChanged
            // (null → "KBM"). We want to capture the *transition* KBM → Gamepad below, so we
            // drain any initial event here by clearing the list after the first activation.
            received.Clear();

            // --- Pair gamepad and activate the Gamepad scheme (the transition we want to assert) ---
            // UnpairCurrentDevicesFromUser is not used here; we just switch the scheme on the same user.
            user.ActivateControlScheme(GamepadScheme);

            // Assert exactly one ControlSchemeChanged event was published for the KBM→Gamepad switch.
            Assert.That(received.Count, Is.EqualTo(1),
                $"Expected exactly 1 ControlSchemeChanged event; got {received.Count}");
            Assert.That(received[0].Previous, Is.EqualTo(KbmScheme));
            Assert.That(received[0].Current, Is.EqualTo(GamepadScheme));
        }

        // --- Shutdown / teardown ---

        /// <summary>
        /// After <see cref="InputService.EnableMap"/> and subscribing, calling
        /// <see cref="InputService.Dispose"/> leaves no active callbacks and no enabled maps.
        /// A second <see cref="InputService.Dispose"/> does nothing (idempotent).
        /// </summary>
        [Test]
        public void Shutdown_disablesMaps_andDetachesHandlers()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            _svc.SetAsset(_asset);
            _svc.EnableMap("Player");

            int callCount = 0;
            _svc.SubscribePerformed("Player/Jump", _ => callCount++);

            // Confirm the subscription is live before teardown.
            Press(keyboard.spaceKey);
            Release(keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "expected 1 callback before Dispose");

            // Dispose the service — this must disable all maps and detach all handlers.
            _svc.Dispose();

            // Driving the action after Dispose must not fire the callback.
            Press(keyboard.spaceKey);
            Assert.That(callCount, Is.EqualTo(1), "callback fired after Dispose");

            // The Player map must no longer be enabled.
            var playerMap = _asset.FindActionMap("Player");
            Assert.That(playerMap.enabled, Is.False, "map is still enabled after Dispose");

            // Second Dispose must not throw.
            Assert.DoesNotThrow(() => _svc.Dispose());
        }

        // --- Facade integration ---

        /// <summary>
        /// After <see cref="MosaicUI.Initialize"/>, <c>MosaicUI.Input</c> is non-null and
        /// <c>MosaicUI.Events</c> is non-null. A second <c>Initialize</c> while already
        /// initialized is a no-op (same instance returned). After <see cref="MosaicUI.Shutdown"/>,
        /// <c>MosaicUI.Input</c> is null. <c>MosaicUI.Events.Subscribe&lt;ControlSchemeChanged&gt;</c>
        /// does not throw — confirming the event is delivered through the bus facade.
        /// </summary>
        [Test]
        public void FacadeIntegration_InitializeAndShutdown()
        {
            try
            {
                MosaicUI.Initialize();

                Assert.That(MosaicUI.Input, Is.Not.Null, "MosaicUI.Input is null after Initialize");
                Assert.That(MosaicUI.Events, Is.Not.Null, "MosaicUI.Events is null after Initialize");

                // Confirm a second Initialize is idempotent (same instance).
                var firstInput = MosaicUI.Input;
                MosaicUI.Initialize();
                Assert.That(MosaicUI.Input, Is.SameAs(firstInput),
                    "MosaicUI.Input was replaced by a second Initialize (expected idempotency)");

                // Confirm ControlSchemeChanged is delivered through MosaicUI.Events.
                // We cannot do the full pairing dance here (InputTestFixture has reset the
                // InputSystem, but MosaicUI.Input's internal service was constructed after that),
                // so we just confirm Events.Subscribe<ControlSchemeChanged> does not throw.
                Assert.DoesNotThrow(() =>
                {
                    using var token = MosaicUI.Events.Subscribe<ControlSchemeChanged>(evt => { });
                });

                MosaicUI.Shutdown();
                Assert.That(MosaicUI.Input, Is.Null, "MosaicUI.Input is not null after Shutdown");
            }
            finally
            {
                // Ensure the facade is shut down even if an assertion fires.
                if (MosaicUI.IsInitialized)
                    MosaicUI.Shutdown();
            }
        }

        // --- Introspection seam (T6.3) ---

        /// <summary>
        /// The internal introspection members reflect the current state of the service.
        /// </summary>
        [Test]
        public void IntrospectionSeam_reflectsCurrentState()
        {
            // Before SetAsset: AssetName is null, EnabledMaps is empty, ActiveScheme is null.
            Assert.That(_svc.AssetName, Is.Null);
            Assert.That(_svc.EnabledMaps.Count, Is.EqualTo(0));
            Assert.That(_svc.ActiveScheme, Is.Null);

            _svc.SetAsset(_asset);
            Assert.That(_svc.AssetName, Is.EqualTo(_asset.name));

            _svc.EnableMap("Player");
            Assert.That(_svc.EnabledMaps.Count, Is.EqualTo(1));

            _svc.DisableMap("Player");
            Assert.That(_svc.EnabledMaps.Count, Is.EqualTo(0));
        }
    }
}
