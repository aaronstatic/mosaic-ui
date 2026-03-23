using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    public class PanelInstance
    {
        public PanelDefinition Definition { get; }
        public PanelController Controller { get; private set; }
        public VisualElement Root { get; private set; }
        public string SlotName { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; private set; }

        public PanelInstance(PanelDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        /// <summary>
        /// Instantiate the panel: clone UXML, apply optional USS, create controller, inject dependencies, call OnBind.
        /// </summary>
        public void Instantiate(ServiceRegistry services)
        {
            if (Definition.UXML == null)
                throw new InvalidOperationException($"Panel '{Definition.PanelName}' has no UXML assigned.");

            Root = Definition.UXML.CloneTree();
            Root.name = Definition.PanelName;

            if (Definition.USS != null)
            {
                Root.styleSheets.Add(Definition.USS);
            }

            if (!string.IsNullOrEmpty(Definition.ControllerTypeName))
            {
                var controllerType = Type.GetType(Definition.ControllerTypeName);
                if (controllerType == null)
                {
                    Debug.LogError($"[MosaicUI] Could not resolve controller type '{Definition.ControllerTypeName}' for panel '{Definition.PanelName}'.");
                }
                else if (!typeof(PanelController).IsAssignableFrom(controllerType))
                {
                    Debug.LogError($"[MosaicUI] Type '{Definition.ControllerTypeName}' does not extend PanelController.");
                }
                else
                {
                    Controller = (PanelController)Activator.CreateInstance(controllerType);
                    Controller.Root = Root;
                    Controller.Services = services;
                    Controller.OnBind();
                }
            }
        }

        /// <summary>
        /// Show the panel. Calls OnShow on the controller.
        /// </summary>
        public void Show()
        {
            if (Root == null) return;

            Root.style.display = DisplayStyle.Flex;
            IsActive = true;
            Controller?.OnShow();
        }

        /// <summary>
        /// Hide the panel. Calls OnHide on the controller.
        /// </summary>
        public void Hide()
        {
            if (Root == null) return;

            Controller?.OnHide();
            Root.style.display = DisplayStyle.None;
            IsActive = false;
        }

        /// <summary>
        /// Notify the controller that the mode has changed (panel is shared across modes).
        /// </summary>
        public void NotifyModeChanged(string modeName)
        {
            Controller?.OnModeChanged(modeName);
        }

        /// <summary>
        /// Dispose the panel: hide, dispose controller, remove from visual tree.
        /// </summary>
        public void Dispose()
        {
            Hide();
            Controller?.Dispose();
            Controller = null;
            Root?.RemoveFromHierarchy();
            Root = null;
            IsActive = false;
        }
    }
}
