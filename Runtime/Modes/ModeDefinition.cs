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

        public string ModeName => modeName;
        public LayoutDefinition Layout => layout;
        public IReadOnlyList<PanelEntry> Panels => panels;
        public IReadOnlyList<WorldFeatureEntry> WorldFeatures => worldFeatures;
        public IReadOnlyList<WorldControllerEntry> WorldControllers => worldControllers;
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
