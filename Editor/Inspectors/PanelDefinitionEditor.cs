using System;
using UnityEditor;
using UnityEngine;

namespace Mosaic.UI.Editor
{
    [CustomEditor(typeof(PanelDefinition))]
    public class PanelDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty _panelName;
        private SerializedProperty _uxml;
        private SerializedProperty _uss;
        private SerializedProperty _controllerTypeName;

        private void OnEnable()
        {
            _panelName = serializedObject.FindProperty("panelName");
            _uxml = serializedObject.FindProperty("uxml");
            _uss = serializedObject.FindProperty("uss");
            _controllerTypeName = serializedObject.FindProperty("controllerTypeName");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_panelName);
            EditorGUILayout.PropertyField(_uxml);
            EditorGUILayout.PropertyField(_uss);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Controller", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_controllerTypeName, new GUIContent("Controller Type Name"));

            // Validate controller type
            var typeName = _controllerTypeName.stringValue;
            if (!string.IsNullOrEmpty(typeName))
            {
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    EditorGUILayout.HelpBox(
                        $"Type '{typeName}' could not be found. Use the fully qualified name including namespace and assembly.\nExample: MyNamespace.MyController, MyAssembly",
                        MessageType.Error);
                }
                else if (!typeof(PanelController).IsAssignableFrom(type))
                {
                    EditorGUILayout.HelpBox(
                        $"Type '{typeName}' does not extend PanelController.",
                        MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Controller type resolved: {type.FullName}",
                        MessageType.Info);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
