using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// EditMode logic tests for <see cref="UIRoutingGate"/> (Phase 4, T4.4).
    ///
    /// <para><b>Testability split (Decision 6):</b> the gate's live pointer path
    /// (<see cref="RuntimePanelUtils.ScreenToPanel"/> + <see cref="IPanel.Pick"/>) needs a
    /// <em>live, laid-out</em> runtime panel, which EditMode lacks. That behavior — a panel button
    /// under a known screen position reporting "over UI" (and the world pick being suppressed), and
    /// empty space reporting "not over UI" — is <b>scene-verified in the P6 play-mode scene</b>, NOT
    /// unit-tested here. These tests cover only what is assertable without a live panel:</para>
    /// <list type="bullet">
    ///   <item><description>Null-panel safe defaults: both queries return <c>false</c>, no NRE.</description></item>
    ///   <item><description>
    ///     <see cref="UIRoutingGate.ShouldHandleWorldPointer"/> boolean logic — the override
    ///     (<c>takeRawInput: true</c>) always handles; otherwise it respects the gate. The
    ///     suppression branch (gate reports over-UI ⇒ should NOT handle) is exercised via a small
    ///     <b>test seam</b>: <see cref="UIRoutingGate.IsPointerOverUI"/> is <c>virtual</c>, so a
    ///     subclass forces a known result without a live panel.
    ///   </description></item>
    /// </list>
    ///
    /// <para><b>Project-memory gotchas:</b> no <c>UnityEngine.Object</c>s are constructed here (no
    /// GameObjects), so the bare-<c>Object.</c> ambiguity does not arise. Any collection count would be
    /// asserted via <c>.Count</c> directly, never NUnit <c>Has.Count</c> (none are checked in this
    /// fixture).</para>
    /// </summary>
    [TestFixture]
    public class UIRoutingGateTests
    {
        // ── Test seam ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Forces <see cref="UIRoutingGate.IsPointerOverUI"/> to a fixed value so the
        /// <see cref="UIRoutingGate.ShouldHandleWorldPointer"/> suppression branch can be exercised
        /// without a live panel. <see cref="UIRoutingGate.IsPointerOverUI"/> is documented as a
        /// <c>virtual</c> test seam (the real implementation's live <c>Pick</c>/<c>ScreenToPanel</c>
        /// path is scene-verified in P6).
        /// </summary>
        private sealed class ForcedGate : UIRoutingGate
        {
            private readonly bool _overUI;

            // The base still requires a non-null panel source, even though the override never calls it.
            public ForcedGate(bool overUI) : base(() => null) => _overUI = overUI;

            public override bool IsPointerOverUI(Vector2 screenPos) => _overUI;
        }

        // ── Construction ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_nullPanelSource_throwsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new UIRoutingGate(null));
        }

        // ── Null-panel safe defaults ──────────────────────────────────────────────────

        [Test]
        public void IsPointerOverUI_nullPanel_returnsFalse()
        {
            var gate = new UIRoutingGate(() => null);

            Assert.That(gate.IsPointerOverUI(new Vector2(100f, 100f)), Is.False);
            Assert.That(gate.IsPointerOverUI(Vector2.zero), Is.False);
        }

        [Test]
        public void IsKeyboardCaptured_nullPanel_returnsFalse()
        {
            var gate = new UIRoutingGate(() => null);

            Assert.That(gate.IsKeyboardCaptured(), Is.False);
        }

        // ── ShouldHandleWorldPointer: null-panel gate (IsPointerOverUI == false) ───────

        [Test]
        public void ShouldHandleWorldPointer_nullPanel_returnsTrue()
        {
            // With a null panel source, IsPointerOverUI is false, so the world pointer should be handled.
            var gate = new UIRoutingGate(() => null);

            Assert.That(gate.ShouldHandleWorldPointer(new Vector2(50f, 50f)), Is.True);
        }

        [Test]
        public void ShouldHandleWorldPointer_nullPanel_takeRawInput_returnsTrue()
        {
            var gate = new UIRoutingGate(() => null);

            Assert.That(gate.ShouldHandleWorldPointer(new Vector2(50f, 50f), takeRawInput: true), Is.True);
        }

        // ── ShouldHandleWorldPointer: suppression branch via the virtual seam ──────────

        [Test]
        public void ShouldHandleWorldPointer_overUI_returnsFalse()
        {
            // Gate forced to report "over UI" ⇒ world handling is suppressed.
            var gate = new ForcedGate(overUI: true);

            Assert.That(gate.ShouldHandleWorldPointer(new Vector2(10f, 10f)), Is.False);
        }

        [Test]
        public void ShouldHandleWorldPointer_overUI_takeRawInput_returnsTrue()
        {
            // The override (takeRawInput) bypasses the gate even when the pointer is over UI.
            var gate = new ForcedGate(overUI: true);

            Assert.That(gate.ShouldHandleWorldPointer(new Vector2(10f, 10f), takeRawInput: true), Is.True);
        }

        [Test]
        public void ShouldHandleWorldPointer_notOverUI_returnsTrue()
        {
            // Gate forced to report "not over UI" ⇒ world handling proceeds.
            var gate = new ForcedGate(overUI: false);

            Assert.That(gate.ShouldHandleWorldPointer(new Vector2(10f, 10f)), Is.True);
        }
    }
}
