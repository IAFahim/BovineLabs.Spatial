using BovineLabs.Core.Editor.Inspectors;
using BovineLabs.Core.Editor.ObjectManagement;
using BovineLabs.Spatial.Authoring;
using BovineLabs.Spatial.Settings;
using UnityEditor;
using UnityEngine.UIElements;

namespace BovineLabs.Spatial.Editor
{
    [CustomEditor(typeof(SpatialGridSettings))]
    public sealed class SpatialGridSettingsEditor : ElementEditor
    {
        protected override VisualElement CreateElement(SerializedProperty property)
        {
            return property.name switch
            {
                "schemas" => new AssetCreator<SpatialMaskAsset>(serializedObject, property).Element,
                _ => base.CreateElement(property)
            };
        }
    }
}