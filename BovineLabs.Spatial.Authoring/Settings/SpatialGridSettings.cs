using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Settings;
using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Settings
{
    [SettingsGroup("Spatial")]
    public class SpatialGridSettings : SettingsBase
    {
        [SerializeField] private float cellSize = 2f;
        [SerializeField] private int activeMapSize = 100;
        [SerializeField] private float cameraOffset = 20f;

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);
            baker.AddComponent(entity, new SpatialGridConfig
            {
                CellSize = cellSize,
                ActiveMapSize = activeMapSize,
                CameraOffset = cameraOffset
            });
        }
    }
}