using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    [CustomEditor(typeof(ModeDefinition))]
    public class ModeDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mode = (ModeDefinition)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            if (mode.Layout != null && mode.Layout.LayoutUxml != null)
            {
                // Get available slot names from the layout UXML
                var layoutRoot = mode.Layout.LayoutUxml.CloneTree();
                var slotElements = layoutRoot.Query(className: "mosaic-slot").ToList();
                var slotNames = new HashSet<string>();
                foreach (var slot in slotElements)
                {
                    if (!string.IsNullOrEmpty(slot.name))
                        slotNames.Add(slot.name);
                }

                // Validate panel slot assignments
                foreach (var panel in mode.Panels)
                {
                    if (panel.panel == null) continue;
                    if (string.IsNullOrEmpty(panel.targetSlot))
                    {
                        EditorGUILayout.HelpBox(
                            $"Panel '{panel.panel.PanelName}' has no target slot assigned.",
                            MessageType.Warning);
                    }
                    else if (!slotNames.Contains(panel.targetSlot))
                    {
                        EditorGUILayout.HelpBox(
                            $"Panel '{panel.panel.PanelName}' targets slot '{panel.targetSlot}' which doesn't exist in the layout. Available: {string.Join(", ", slotNames)}",
                            MessageType.Error);
                    }
                }

                if (slotNames.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Available slots: {string.Join(", ", slotNames)}",
                        MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No layout assigned or layout has no UXML. Slot validation unavailable.",
                    MessageType.Info);
            }
        }
    }
}
