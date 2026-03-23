using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    [CreateAssetMenu(fileName = "NewPanel", menuName = "MosaicUI/Panel Definition")]
    public class PanelDefinition : ScriptableObject
    {
        [SerializeField] private string panelName;
        [SerializeField] private VisualTreeAsset uxml;
        [SerializeField] private StyleSheet uss;
        [SerializeField] private string controllerTypeName;

        public string PanelName => panelName;
        public VisualTreeAsset UXML => uxml;
        public StyleSheet USS => uss;
        public string ControllerTypeName => controllerTypeName;
    }
}
