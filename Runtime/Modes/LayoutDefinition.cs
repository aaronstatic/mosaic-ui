using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    [CreateAssetMenu(fileName = "NewLayout", menuName = "MosaicUI/Layout Definition")]
    public class LayoutDefinition : ScriptableObject
    {
        [Tooltip("UXML template defining the layout shell with named slot containers")]
        [SerializeField] private VisualTreeAsset layoutUxml;

        public VisualTreeAsset LayoutUxml => layoutUxml;
    }
}
