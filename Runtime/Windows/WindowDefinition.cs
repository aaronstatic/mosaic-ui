using UnityEngine;
using Mosaic.UI;

namespace Mosaic.UI.Windows
{
    [CreateAssetMenu(fileName = "NewWindow", menuName = "MosaicUI/Window Definition")]
    public class WindowDefinition : ScriptableObject
    {
        [SerializeField] private string windowName;
        [SerializeField] private PanelDefinition panel;
        [SerializeField] private Vector2 defaultSize = new(400, 300);
        [SerializeField] private Vector2 minSize = new(200, 150);
        [SerializeField] private Vector2 maxSize = new(1920, 1080);
        [SerializeField] private bool draggable = true;
        [SerializeField] private bool resizable = true;
        [SerializeField] private bool closable = true;
        [SerializeField] private string title;

        public string WindowName => windowName;
        public PanelDefinition Panel => panel;
        public Vector2 DefaultSize => defaultSize;
        public Vector2 MinSize => minSize;
        public Vector2 MaxSize => maxSize;
        public bool Draggable => draggable;
        public bool Resizable => resizable;
        public bool Closable => closable;
        public string Title => string.IsNullOrEmpty(title) ? windowName : title;
    }
}
