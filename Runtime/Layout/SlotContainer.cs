using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    public class SlotContainer
    {
        public string SlotName { get; }
        public VisualElement Element { get; }

        private readonly SortedList<int, List<PanelInstance>> _panels = new();

        public SlotContainer(string slotName, VisualElement element)
        {
            SlotName = slotName;
            Element = element;
        }

        public void AddPanel(PanelInstance panel, int sortOrder)
        {
            if (!_panels.TryGetValue(sortOrder, out var list))
            {
                list = new List<PanelInstance>();
                _panels[sortOrder] = list;
            }
            list.Add(panel);
            RebuildVisualOrder();
        }

        public void RemovePanel(PanelInstance panel)
        {
            int emptyKey = int.MinValue;
            bool foundEmptyKey = false;

            foreach (var kvp in _panels)
            {
                if (kvp.Value.Remove(panel))
                {
                    if (kvp.Value.Count == 0)
                    {
                        emptyKey = kvp.Key;
                        foundEmptyKey = true;
                    }
                    break;
                }
            }

            if (foundEmptyKey)
                _panels.Remove(emptyKey);

            panel.Root?.RemoveFromHierarchy();
        }

        private void RebuildVisualOrder()
        {
            Element.Clear();
            foreach (var kvp in _panels)
            {
                foreach (var panel in kvp.Value)
                {
                    if (panel.Root != null)
                        Element.Add(panel.Root);
                }
            }
        }

        public void Clear()
        {
            _panels.Clear();
            Element.Clear();
        }
    }
}
