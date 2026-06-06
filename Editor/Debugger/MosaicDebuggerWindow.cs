using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    /// <summary>
    /// MosaicUI Debugger — a tabbed EditorWindow for observing runtime state during play mode.
    ///
    /// Tabs: State | Events | Commands | Composition
    ///
    /// Lifecycle:
    ///   OnEnable  → subscribe EditorApplication.playModeStateChanged + update
    ///   play begins → update handler detects IsInitialized flip → AttachAll()
    ///   play exits  → playModeStateChanged (ExitingPlayMode) → DetachAll()
    ///   OnDisable → unsubscribe both handlers + DetachAll()
    ///
    /// Duplicate-subscription guard: unsubscribe before subscribing (in OnEnable/OnDisable);
    /// a bool _attached prevents AttachAll/DetachAll from double-firing.
    /// </summary>
    public class MosaicDebuggerWindow : EditorWindow
    {
        // ── Menu ─────────────────────────────────────────────────────────────

        [MenuItem("Window/MosaicUI/Debugger")]
        public static void Open()
        {
            var window = GetWindow<MosaicDebuggerWindow>("MosaicUI Debugger");
            window.titleContent = new GUIContent("MosaicUI Debugger");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        // ── Panes ─────────────────────────────────────────────────────────────

        private List<DebuggerPane> _panes;
        private int _selectedTab = 0;

        // ── UI elements ───────────────────────────────────────────────────────

        private VisualElement _tabStrip;
        private VisualElement _contentHost;
        private VisualElement _emptyState;
        private List<Button> _tabButtons;

        // ── Lifecycle state ───────────────────────────────────────────────────

        /// <summary>True once AttachAll() has been called; prevents double-attach.</summary>
        private bool _attached = false;

        /// <summary>Last known value of MosaicUI.IsInitialized, tracked by the update handler.</summary>
        private bool _wasInitialized = false;

        // ── EditorWindow callbacks ────────────────────────────────────────────

        private void OnEnable()
        {
            // Unsubscribe first to prevent duplicate handlers across domain reloads or
            // repeated enable/disable cycles (a -= on a non-subscribed delegate is a safe no-op).
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;

            // Sync local flag with reality (window may open mid-play).
            _wasInitialized = MosaicUI.IsInitialized;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;

            DetachAll();
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void CreateGUI()
        {
            // Build panes first (Initialize sets their Root).
            _panes = new List<DebuggerPane>
            {
                new StateInspectorPane(),
                new EventMonitorPane(),
                new CommandsInspectorPane(),
                new CompositionInspectorPane(),
            };

            foreach (var pane in _panes)
                pane.Initialize(this);

            // Root container — column layout.
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;

            // Tab strip.
            _tabStrip = BuildTabStrip();
            root.Add(_tabStrip);

            // Separator line.
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            root.Add(separator);

            // Content host — fills remaining space; pane Roots are added here and toggled visible.
            _contentHost = new VisualElement();
            _contentHost.style.flexGrow = 1;
            _contentHost.style.position = Position.Relative;
            foreach (var pane in _panes)
            {
                pane.Root.style.position = Position.Absolute;
                pane.Root.style.top = 0;
                pane.Root.style.left = 0;
                pane.Root.style.right = 0;
                pane.Root.style.bottom = 0;
                pane.Root.style.display = DisplayStyle.None;
                _contentHost.Add(pane.Root);
            }
            root.Add(_contentHost);

            // Empty state overlay — shown when MosaicUI.IsInitialized == false.
            _emptyState = BuildEmptyState();
            _emptyState.style.position = Position.Absolute;
            _emptyState.style.top = 0;
            _emptyState.style.left = 0;
            _emptyState.style.right = 0;
            _emptyState.style.bottom = 0;
            _contentHost.Add(_emptyState);

            // Set initial tab selection and correct empty/live display state.
            SelectTab(_selectedTab);
            RefreshEmptyState();

            // If the window was opened while already in play mode (or CreateGUI fires after
            // OnEditorUpdate already detected the IsInitialized==true state), attach now.
            // AttachAll() is guarded by _attached so this is safe to call unconditionally.
            if (MosaicUI.IsInitialized && !_attached)
                AttachAll();
        }

        private VisualElement BuildTabStrip()
        {
            var strip = new VisualElement();
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            strip.style.paddingLeft = 4;
            strip.style.paddingRight = 4;

            _tabButtons = new List<Button>();

            for (int i = 0; i < _panes.Count; i++)
            {
                int index = i; // capture for closure
                var btn = new Button(() => SelectTab(index));
                btn.text = _panes[i].Title;
                btn.style.marginLeft = 2;
                btn.style.marginRight = 2;
                btn.style.marginTop = 4;
                btn.style.marginBottom = 0;
                btn.style.paddingLeft = 10;
                btn.style.paddingRight = 10;
                btn.style.paddingTop = 4;
                btn.style.paddingBottom = 4;
                btn.style.borderTopLeftRadius = 3;
                btn.style.borderTopRightRadius = 3;
                btn.style.borderBottomLeftRadius = 0;
                btn.style.borderBottomRightRadius = 0;
                btn.style.borderTopWidth = 0;
                btn.style.borderLeftWidth = 0;
                btn.style.borderRightWidth = 0;
                btn.style.borderBottomWidth = 0;
                strip.Add(btn);
                _tabButtons.Add(btn);
            }

            return strip;
        }

        private VisualElement BuildEmptyState()
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.alignItems = Align.Center;
            container.style.justifyContent = Justify.Center;
            container.style.paddingLeft = 24;
            container.style.paddingRight = 24;

            var icon = new Label("○");
            icon.style.fontSize = 32;
            icon.style.color = new Color(0.4f, 0.4f, 0.4f);
            icon.style.marginBottom = 8;
            container.Add(icon);

            var message = new Label("MosaicUI not initialized");
            message.style.fontSize = 14;
            message.style.unityFontStyleAndWeight = FontStyle.Bold;
            message.style.color = new Color(0.7f, 0.7f, 0.7f);
            message.style.marginBottom = 4;
            message.style.unityTextAlign = TextAnchor.MiddleCenter;
            container.Add(message);

            var hint = new Label("Enter play mode with a MosaicUIManager in the scene.");
            hint.style.fontSize = 11;
            hint.style.color = new Color(0.5f, 0.5f, 0.5f);
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.whiteSpace = WhiteSpace.Normal;
            container.Add(hint);

            return container;
        }

        // ── Tab selection ─────────────────────────────────────────────────────

        private void SelectTab(int index)
        {
            if (_panes == null || index < 0 || index >= _panes.Count)
                return;

            _selectedTab = index;

            // Show the selected pane's Root; hide the others.
            for (int i = 0; i < _panes.Count; i++)
            {
                bool isSelected = i == _selectedTab;
                _panes[i].Root.style.display = isSelected ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Style the active tab button.
            if (_tabButtons != null)
            {
                for (int i = 0; i < _tabButtons.Count; i++)
                {
                    bool isSelected = i == _selectedTab;
                    _tabButtons[i].style.backgroundColor = isSelected
                        ? new Color(0.30f, 0.30f, 0.30f)
                        : new Color(0.22f, 0.22f, 0.22f);
                    _tabButtons[i].style.color = isSelected
                        ? new Color(1f, 1f, 1f)
                        : new Color(0.7f, 0.7f, 0.7f);
                }
            }
        }

        // ── Empty state / live state toggle ───────────────────────────────────

        private void RefreshEmptyState()
        {
            if (_emptyState == null)
                return;

            bool initialized = MosaicUI.IsInitialized;

            // Empty state overlays the content when not initialized.
            _emptyState.style.display = initialized ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ── Attach / detach ───────────────────────────────────────────────────

        private void AttachAll()
        {
            if (_attached)
                return;

            if (_panes == null)
                return;

            _attached = true;

            foreach (var pane in _panes)
            {
                try { pane.Attach(); }
                catch (Exception ex)
                {
                    Debug.LogError($"[MosaicDebugger] {pane.Title}.Attach() threw: {ex}");
                }
            }

            RefreshEmptyState();
        }

        private void DetachAll()
        {
            if (!_attached)
                return;

            _attached = false;

            if (_panes != null)
            {
                foreach (var pane in _panes)
                {
                    try { pane.Detach(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[MosaicDebugger] {pane.Title}.Detach() threw: {ex}");
                    }
                }
            }

            RefreshEmptyState();
        }

        // ── Play-mode lifecycle handlers ──────────────────────────────────────

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    // Detach before MosaicUI.Shutdown() nulls the singletons.
                    DetachAll();
                    _wasInitialized = false;
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // Belt-and-suspenders: ensure detach if we somehow missed ExitingPlayMode.
                    DetachAll();
                    _wasInitialized = false;
                    RefreshEmptyState();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    // MosaicUIManager.Start() calls Initialize() one frame after play begins.
                    // We don't attach here; the update handler detects the IsInitialized flip.
                    break;
            }
        }

        /// <summary>
        /// Lightweight update handler — only job is to detect the IsInitialized false→true flip
        /// and call AttachAll(). Does NOT poll pane data (panes own their own polling in later phases).
        /// </summary>
        private void OnEditorUpdate()
        {
            bool isNow = MosaicUI.IsInitialized;

            if (isNow && !_wasInitialized)
            {
                // IsInitialized just flipped true — attach panes.
                _wasInitialized = true;
                AttachAll();
            }
            else if (!isNow && _wasInitialized)
            {
                // IsInitialized flipped false outside of a known play-mode-exit event.
                // (Defensive: covers Shutdown() called manually or from unexpected code paths.)
                _wasInitialized = false;
                DetachAll();
            }
        }
    }
}
