using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Windows
{
    public class WindowChrome : VisualElement
    {
        public event Action CloseRequested;

        private readonly VisualElement _titleBar;
        private readonly Label _titleLabel;
        private readonly Button _closeButton;
        private readonly VisualElement _content;
        private readonly VisualElement _resizeHandle;

        private bool _isDragging;
        private Vector2 _dragOffset;
        private bool _isResizing;
        private Vector2 _resizeStartPos;
        private Vector2 _resizeStartSize;

        private readonly Vector2 _minSize;
        private readonly Vector2 _maxSize;

        public VisualElement Content => _content;
        public string Title { get => _titleLabel.text; set => _titleLabel.text = value; }

        public WindowChrome(WindowDefinition definition)
        {
            _minSize = definition.MinSize;
            _maxSize = definition.MaxSize;

            AddToClassList("mosaic-window");

            style.position = Position.Absolute;
            style.width = definition.DefaultSize.x;
            style.height = definition.DefaultSize.y;

            // Title bar
            _titleBar = new VisualElement();
            _titleBar.AddToClassList("mosaic-window__title-bar");
            Add(_titleBar);

            _titleLabel = new Label(definition.Title);
            _titleLabel.AddToClassList("mosaic-window__title");
            _titleBar.Add(_titleLabel);

            if (definition.Closable)
            {
                _closeButton = new Button(() => CloseRequested?.Invoke());
                _closeButton.text = "\u2715";
                _closeButton.AddToClassList("mosaic-window__close-button");
                _titleBar.Add(_closeButton);
            }

            // Content area
            _content = new VisualElement();
            _content.AddToClassList("mosaic-window__content");
            Add(_content);

            // Drag handling
            if (definition.Draggable)
            {
                _titleBar.RegisterCallback<PointerDownEvent>(OnTitleBarPointerDown);
                _titleBar.RegisterCallback<PointerMoveEvent>(OnTitleBarPointerMove);
                _titleBar.RegisterCallback<PointerUpEvent>(OnTitleBarPointerUp);
            }

            // Resize handle
            if (definition.Resizable)
            {
                _resizeHandle = new VisualElement();
                _resizeHandle.AddToClassList("mosaic-window__resize-handle");
                Add(_resizeHandle);

                _resizeHandle.RegisterCallback<PointerDownEvent>(OnResizePointerDown);
                _resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizePointerMove);
                _resizeHandle.RegisterCallback<PointerUpEvent>(OnResizePointerUp);
            }

            // Bring to front on click
            RegisterCallback<PointerDownEvent>(evt => BringToFront());
        }

        // --- Drag ---

        private void OnTitleBarPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _isDragging = true;
            _dragOffset = new Vector2(evt.localPosition.x, evt.localPosition.y);
            _titleBar.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnTitleBarPointerMove(PointerMoveEvent evt)
        {
            if (!_isDragging) return;

            var parentPos = parent.WorldToLocal(evt.position);
            style.left = parentPos.x - _dragOffset.x;
            style.top = parentPos.y - _dragOffset.y;
        }

        private void OnTitleBarPointerUp(PointerUpEvent evt)
        {
            if (!_isDragging) return;
            _isDragging = false;
            _titleBar.ReleasePointer(evt.pointerId);
        }

        // --- Resize ---

        private void OnResizePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _isResizing = true;
            _resizeStartPos = evt.position;
            _resizeStartSize = new Vector2(resolvedStyle.width, resolvedStyle.height);
            _resizeHandle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnResizePointerMove(PointerMoveEvent evt)
        {
            if (!_isResizing) return;

            var delta = (Vector2)evt.position - _resizeStartPos;
            var newWidth = Mathf.Clamp(_resizeStartSize.x + delta.x, _minSize.x, _maxSize.x);
            var newHeight = Mathf.Clamp(_resizeStartSize.y + delta.y, _minSize.y, _maxSize.y);

            style.width = newWidth;
            style.height = newHeight;
        }

        private void OnResizePointerUp(PointerUpEvent evt)
        {
            if (!_isResizing) return;
            _isResizing = false;
            _resizeHandle.ReleasePointer(evt.pointerId);
        }
    }
}
