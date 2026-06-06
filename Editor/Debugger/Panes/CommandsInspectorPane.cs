using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    /// <summary>
    /// Commands Inspector pane — lists all command ids currently registered in
    /// MosaicUI.Commands, sorted alphabetically. CommandRegistry has no change
    /// notification, so the list is polled every 500ms.
    ///
    /// Polling mechanism:
    ///   Attach() starts a 500ms scheduled item that calls Refresh().
    ///   Detach() pauses the scheduled item.
    ///   Re-entering Attach() resumes it (the IVisualElementScheduledItem is reused).
    ///
    /// Empty state: "No commands registered." shown when RegisteredIds is empty.
    /// </summary>
    public class CommandsInspectorPane : DebuggerPane
    {
        public override string Title => "Commands";

        // ── UI structure ──────────────────────────────────────────────────────

        private Label _countLabel;
        private Label _emptyLabel;
        private VisualElement _listContainer;
        private ScrollView _scrollView;

        // ── Poll state ────────────────────────────────────────────────────────

        private IVisualElementScheduledItem _pollSchedule;

        /// <summary>
        /// The last snapshot we rendered, sorted. Used to detect changes so we only
        /// rebuild the list when something actually changed (avoids per-poll GC).
        /// </summary>
        private List<string> _lastIds = new();

        // ── DebuggerPane overrides ─────────────────────────────────────────────

        protected override void BuildUI()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.flexDirection = FlexDirection.Column;

            // Header row
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.paddingLeft = 8;
            headerRow.style.paddingRight = 8;
            headerRow.style.paddingTop = 8;
            headerRow.style.paddingBottom = 4;
            Root.Add(headerRow);

            var header = new Label("Commands Inspector");
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.flexGrow = 1;
            headerRow.Add(header);

            _countLabel = new Label("");
            _countLabel.style.fontSize = 11;
            _countLabel.style.color = new Color(0.55f, 0.55f, 0.55f);
            headerRow.Add(_countLabel);

            // Separator
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            sep.style.marginBottom = 4;
            Root.Add(sep);

            // Empty message
            _emptyLabel = new Label("No commands registered.");
            _emptyLabel.style.paddingLeft = 8;
            _emptyLabel.style.paddingTop = 8;
            _emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            _emptyLabel.style.display = DisplayStyle.None;
            Root.Add(_emptyLabel);

            // Scrollable list
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            Root.Add(_scrollView);

            _listContainer = new VisualElement();
            _listContainer.style.paddingLeft = 8;
            _listContainer.style.paddingRight = 8;
            _listContainer.style.paddingBottom = 8;
            _scrollView.Add(_listContainer);
        }

        public override void Attach()
        {
            if (!MosaicUI.IsInitialized || MosaicUI.Commands == null)
                return;

            // Immediately populate before the first poll fires.
            Refresh();

            // Start (or resume) the 500ms poll.
            if (_pollSchedule == null)
            {
                _pollSchedule = Root.schedule.Execute(Refresh).Every(500);
            }
            else
            {
                _pollSchedule.Resume();
            }
        }

        public override void Detach()
        {
            // Pause poll — do not discard the handle so Attach() can resume it.
            _pollSchedule?.Pause();

            // Clear displayed state.
            _listContainer?.Clear();
            _lastIds.Clear();
            _countLabel.text = "";
            _emptyLabel.style.display = DisplayStyle.None;
        }

        public override void Refresh()
        {
            if (!MosaicUI.IsInitialized || MosaicUI.Commands == null)
                return;

            IReadOnlyCollection<string> registeredIds;
            try
            {
                registeredIds = MosaicUI.Commands.RegisteredIds;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MosaicDebugger] CommandsInspectorPane.Refresh() failed reading RegisteredIds: {ex}");
                return;
            }

            // Sort into a stable list for comparison and display.
            var sorted = new List<string>(registeredIds);
            sorted.Sort(StringComparer.Ordinal);

            // Only rebuild the UI if the list actually changed (avoids per-poll GC).
            if (ListsEqual(sorted, _lastIds))
                return;

            _lastIds = sorted;
            RebuildList(sorted);
        }

        // ── List rendering ─────────────────────────────────────────────────────

        private void RebuildList(List<string> sortedIds)
        {
            _listContainer.Clear();

            if (sortedIds.Count == 0)
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _countLabel.text = "(0)";
                return;
            }

            _emptyLabel.style.display = DisplayStyle.None;
            _countLabel.text = $"({sortedIds.Count})";

            for (int i = 0; i < sortedIds.Count; i++)
            {
                var id = sortedIds[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft = 4;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.18f, 0.18f);

                // Index badge
                var indexLabel = new Label($"{i + 1}.");
                indexLabel.style.fontSize = 10;
                indexLabel.style.color = new Color(0.45f, 0.45f, 0.45f);
                indexLabel.style.width = 24;
                indexLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                indexLabel.style.marginRight = 6;
                row.Add(indexLabel);

                // Command id
                var idLabel = new Label(id);
                idLabel.style.fontSize = 11;
                idLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
                idLabel.style.flexGrow = 1;
                row.Add(idLabel);

                _listContainer.Add(row);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool ListsEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
