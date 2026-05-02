using System.Collections.Generic;
using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Settings;
using BovineLabs.Spatial.Authoring;
using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Settings
{
    [SettingsGroup("Spatial")]
    public sealed class SpatialGridSettings : SettingsBase
    {
        [Header("Grid")]
        [SerializeField] private float cellSize = 2f;
        [SerializeField] private int activeMapSize = 100;
        [SerializeField] private float cameraOffset = 20f;

        [Header("Mask Schemas")]
        [SerializeField] private List<SpatialMaskAsset> schemas = new();

        public float CellSize => cellSize;
        public int ActiveMapSize => activeMapSize;
        public float CameraOffset => cameraOffset;

        public IReadOnlyList<SpatialMaskAsset> Schemas => schemas;

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);

            baker.AddComponent(entity, new SpatialGridConfig
            {
                CellSize = cellSize,
                ActiveMapSize = activeMapSize,
                CameraOffset = cameraOffset
            });

            // Later:
            // Bake SpatialMaskAsset list into SpatialMaskDatabase blob here.
            // Key should be mask.Id.
        }

        public static bool TryGetKey(SpatialMaskAsset mask, out ushort key)
        {
            key = mask == null ? (ushort)0 : mask.Id;
            return key != 0;
        }
    }
}