using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mosaic.UI
{
    [CreateAssetMenu(fileName = "NewMode", menuName = "MosaicUI/Mode Definition")]
    public class ModeDefinition : ScriptableObject
    {
        [SerializeField] private string modeName;
        [SerializeField] private LayoutDefinition layout;
        [SerializeField] private List<PanelEntry> panels = new();
        [SerializeField] private List<WorldFeatureEntry> worldFeatures = new();
        [SerializeField] private List<WorldControllerEntry> worldControllers = new();

        /// <summary>
        /// The action-map names (from the <c>InputActionAsset</c> assigned on
        /// <c>MosaicUIManager</c>) that should be enabled while this mode is active.
        /// <para>
        /// <c>MosaicUIManager.SetMode()</c>/<c>Back()</c> diff these against the previously-active
        /// set — enabling maps entering the new mode and disabling maps leaving it — via
        /// <c>MosaicUI.Input.EnableMap</c>/<c>DisableMap</c>, in lock-step with the panel/world diffs.
        /// A plain <see cref="List{T}"/> of names (not an entry class) because a map declaration
        /// carries only its name (Decision 4); unlike <see cref="PanelEntry"/>/<see cref="WorldFeatureEntry"/>
        /// there is no per-entry slot/order/priority to model.
        /// </para>
        /// </summary>
        [SerializeField] private List<string> actionMaps = new();

        public string ModeName => modeName;
        public LayoutDefinition Layout => layout;
        public IReadOnlyList<PanelEntry> Panels => panels;
        public IReadOnlyList<WorldFeatureEntry> WorldFeatures => worldFeatures;
        public IReadOnlyList<WorldControllerEntry> WorldControllers => worldControllers;

        /// <summary>
        /// Read-only view of the action-map names this mode wants enabled (see the
        /// <c>actionMaps</c> field). Mirrors the <see cref="Panels"/>/<see cref="WorldFeatures"/>/
        /// <see cref="WorldControllers"/> read-only exposure pattern.
        /// </summary>
        public IReadOnlyList<string> ActionMaps => actionMaps;
    }

    [Serializable]
    public class PanelEntry
    {
        public PanelDefinition panel;
        public string targetSlot;
        public int sortOrder;
    }

    [Serializable]
    public class WorldFeatureEntry
    {
        public GameObject prefab;
        public int renderOrder;
    }

    [Serializable]
    public class WorldControllerEntry
    {
        public GameObject prefab;
        public int priority;
    }
}
