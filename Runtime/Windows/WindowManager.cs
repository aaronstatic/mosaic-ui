using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Windows
{
    public class WindowManager
    {
        private readonly VisualElement _windowLayer;
        private readonly ServiceRegistry _services;
        private readonly Dictionary<string, WindowState> _openWindows = new();
        private readonly IWindowPersistence _persistence;

        public WindowManager(VisualElement windowLayer, ServiceRegistry services, IWindowPersistence persistence = null)
        {
            _windowLayer = windowLayer ?? throw new ArgumentNullException(nameof(windowLayer));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _persistence = persistence;
        }

        /// <summary>
        /// Opens a window for the given definition. If the window is already open, returns the existing instance.
        /// </summary>
        public PanelInstance Open(WindowDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var key = definition.WindowName;

            if (_openWindows.TryGetValue(key, out var existing))
                return existing.PanelInstance;

            return CreateWindow(definition, key);
        }

        /// <summary>
        /// Opens a context-bound window. The window key is scoped to both the window name and context.
        /// If the window is already open for this context, returns the existing instance.
        /// </summary>
        public PanelInstance Open<TContext>(WindowDefinition definition, TContext context)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var key = $"{definition.WindowName}:{context}";

            if (_openWindows.TryGetValue(key, out var existing))
                return existing.PanelInstance;

            var panelInstance = CreateWindow(definition, key);

            if (panelInstance.Controller is PanelController<TContext> typedController)
                typedController.SetContext(context);

            return panelInstance;
        }

        private PanelInstance CreateWindow(WindowDefinition definition, string key)
        {
            var chrome = new WindowChrome(definition);

            var panelInstance = new PanelInstance(definition.Panel);
            panelInstance.Instantiate(_services);

            chrome.Content.Add(panelInstance.Root);

            chrome.CloseRequested += () => Close(key);

            _windowLayer.Add(chrome);

            // Restore persisted position/size if available
            if (_persistence != null)
            {
                var savedState = _persistence.Load(key);
                if (savedState.HasValue)
                {
                    chrome.style.left = savedState.Value.position.x;
                    chrome.style.top = savedState.Value.position.y;
                    chrome.style.width = savedState.Value.size.x;
                    chrome.style.height = savedState.Value.size.y;
                }
            }

            panelInstance.Show();

            _openWindows[key] = new WindowState
            {
                Key = key,
                Definition = definition,
                Chrome = chrome,
                PanelInstance = panelInstance
            };

            return panelInstance;
        }

        /// <summary>
        /// Closes a window by its internal key, saving its position if persistence is configured.
        /// </summary>
        public void Close(string key)
        {
            if (!_openWindows.TryGetValue(key, out var state))
                return;

            if (_persistence != null)
            {
                _persistence.Save(key, new WindowSavedState
                {
                    position = new Vector2(state.Chrome.resolvedStyle.left, state.Chrome.resolvedStyle.top),
                    size = new Vector2(state.Chrome.resolvedStyle.width, state.Chrome.resolvedStyle.height)
                });
            }

            // PanelInstance.Dispose() removes its own Root from the hierarchy.
            // Remove the chrome shell separately.
            state.PanelInstance.Dispose();
            state.Chrome.RemoveFromHierarchy();
            _openWindows.Remove(key);
        }

        /// <summary>
        /// Toggles a window open or closed using its window name as the key.
        /// </summary>
        public void Toggle(WindowDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var key = definition.WindowName;
            if (_openWindows.ContainsKey(key))
                Close(key);
            else
                Open(definition);
        }

        /// <summary>
        /// Closes all currently open windows.
        /// </summary>
        public void CloseAll()
        {
            var keys = new List<string>(_openWindows.Keys);
            foreach (var key in keys)
                Close(key);
        }

        /// <summary>
        /// Returns true if a window with the given name is currently open.
        /// </summary>
        public bool IsOpen(string windowName)
        {
            if (string.IsNullOrEmpty(windowName))
                return false;
            return _openWindows.ContainsKey(windowName);
        }

        private class WindowState
        {
            public string Key;
            public WindowDefinition Definition;
            public WindowChrome Chrome;
            public PanelInstance PanelInstance;
        }
    }
}
