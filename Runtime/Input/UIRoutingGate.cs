using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    /// <summary>
    /// The authoritative UI-vs-world routing gate: answers "is the pointer / keyboard focus
    /// captured by a MosaicUI panel or window?" so world-facing input can stand down when the
    /// UI already owns the input.
    /// <para>
    /// <b>UI-Toolkit-native</b> (no <c>EventSystem</c> dependency). It queries the runtime panel
    /// obtained from the active <see cref="UIDocument"/> via a <see cref="Func{IPanel}"/> supplied
    /// at construction. The source is read <em>per query</em> so the gate always sees the live panel
    /// and never caches an <see cref="IPanel"/> that could be torn down / rebuilt on a layout change.
    /// </para>
    /// <para>
    /// <b>One panel covers panels AND windows.</b> <c>WindowManager</c> parents window chrome into the
    /// <em>same</em> <see cref="UIDocument"/> tree as panels, so a single <see cref="IPanel.Pick"/>
    /// covers both — there is no separate window query.
    /// </para>
    /// <para>
    /// <b>Picking semantics.</b> "Over UI" means <see cref="IPanel.Pick"/> returns a non-null element
    /// that is not the transparent root visual tree. UI Toolkit's <see cref="IPanel.Pick"/> already
    /// skips elements with <c>picking-mode: ignore</c>, so a full-screen but pick-ignored container
    /// (the usual layout root) correctly reports "not over UI" for clicks landing on empty space,
    /// while a real panel/button/window reports "over UI".
    /// </para>
    /// <para>
    /// <b>Coordinate conversion.</b> Screen positions are converted to panel space with
    /// <see cref="RuntimePanelUtils.ScreenToPanel"/>, which applies the <c>PanelSettings</c> scale —
    /// never hand-roll the screen-&gt;panel transform. The <em>exact pointer-coordinate convention</em>
    /// fed in (Input System <c>Pointer.current.position</c>, bottom-left origin, and whether a Y-flip
    /// is needed) requires a real pointer over real laid-out content and is therefore
    /// <b>validated in the P6 play-mode scene</b>, not in unit tests (Decision 6). This gate is correct
    /// in <em>structure</em> (<see cref="RuntimePanelUtils.ScreenToPanel"/> + <see cref="IPanel.Pick"/>
    /// + <c>focusController</c>); live pointer behavior is the scene's concern.
    /// </para>
    /// </summary>
    public class UIRoutingGate
    {
        private readonly Func<IPanel> _panelSource;

        /// <summary>
        /// Creates a gate that reads its <see cref="IPanel"/> from <paramref name="panelSource"/>
        /// on every query (never cached).
        /// </summary>
        /// <param name="panelSource">
        /// Supplies the live runtime panel (e.g.
        /// <c>() =&gt; uiDocument.rootVisualElement?.panel</c>). Must not be null; it may legitimately
        /// <em>return</em> null (no panel built yet / document torn down), in which case all queries
        /// return safe defaults.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="panelSource"/> is null.</exception>
        public UIRoutingGate(Func<IPanel> panelSource)
        {
            _panelSource = panelSource ?? throw new ArgumentNullException(nameof(panelSource));
        }

        /// <summary>
        /// Returns <c>true</c> when the given screen position lands on a MosaicUI panel or window
        /// element (not the transparent / pick-ignored layout root).
        /// <para>
        /// Resolves the live panel; if it is null (no UI yet), returns <c>false</c>. Otherwise converts
        /// <paramref name="screenPos"/> to panel space via <see cref="RuntimePanelUtils.ScreenToPanel"/>
        /// and returns whether <see cref="IPanel.Pick"/> hit a real element (non-null and not the root
        /// visual tree). <c>picking-mode: ignore</c> elements are skipped by <see cref="IPanel.Pick"/>,
        /// so a pick-ignored full-screen layout root reports "not over UI".
        /// </para>
        /// <para>
        /// <c>virtual</c> so tests can force a known result to exercise <see cref="ShouldHandleWorldPointer"/>'s
        /// suppression branch without a live panel (the live <see cref="IPanel.Pick"/> /
        /// <see cref="RuntimePanelUtils.ScreenToPanel"/> behavior is scene-verified in P6, per Decision 6).
        /// </para>
        /// </summary>
        /// <param name="screenPos">A screen-space pointer position (origin/convention validated in the P6 scene).</param>
        public virtual bool IsPointerOverUI(Vector2 screenPos)
        {
            var panel = _panelSource();
            if (panel == null)
                return false;

            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);
            var picked = panel.Pick(panelPos);
            return picked != null && picked != panel.visualTree;
        }

        /// <summary>
        /// Returns <c>true</c> when a UI element currently holds keyboard focus (e.g. a focused
        /// <c>TextField</c> that should swallow movement keys from the world).
        /// <para>
        /// Reads <c>panel.focusController.focusedElement</c> from the live panel; returns <c>false</c>
        /// when the panel, focus controller, or focused element is null.
        /// </para>
        /// </summary>
        public bool IsKeyboardCaptured()
        {
            var panel = _panelSource();
            return panel?.focusController?.focusedElement != null;
        }

        /// <summary>
        /// The gate-respecting world raycast guard: world input helpers call this before raycasting,
        /// so a click/point that the UI already owns is not also handled by the world.
        /// <para>
        /// Returns <c>true</c> (proceed with world handling) when the pointer is not over UI, or when
        /// <paramref name="takeRawInput"/> is set (an explicit opt-out for intentional raw input).
        /// Returns <c>false</c> (suppress world handling) when the pointer is over UI and raw input is
        /// not requested. This is the framework replacement for the hand-rolled
        /// "ignore click on empty space" workaround.
        /// </para>
        /// </summary>
        /// <param name="screenPos">The screen-space pointer position to test.</param>
        /// <param name="takeRawInput">
        /// When <c>true</c>, bypass the gate and always return <c>true</c> (handle the world input
        /// regardless of UI). Defaults to <c>false</c> (respect the gate).
        /// </param>
        public bool ShouldHandleWorldPointer(Vector2 screenPos, bool takeRawInput = false)
            => takeRawInput || !IsPointerOverUI(screenPos);
    }
}
