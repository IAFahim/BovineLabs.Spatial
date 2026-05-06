// BovineLabs.Spatial.Authoring/Settings/SpatialGridSettings.cs

using System.Collections.Generic;
using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Settings;
using BovineLabs.Spatial.Authoring;
using BovineLabs.Spatial.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Settings
{
    [SettingsGroup("Spatial")]
    public sealed class SpatialGridSettings : SettingsBase
    {
        [Header("Grid")] [SerializeField] private float cellSize = 0.5f;

        [SerializeField] private int activeMapSize = 100;

        [Header("Mask Schemas")] [SerializeField]
        public List<SpatialMaskAsset> schemas = new();

        [Header("Debug Visualization")] [SerializeField]
        private VisualizationMode visMode = VisualizationMode.CubeHeight;

        [SerializeField] private ColorPalette palette = ColorPalette.CoolWarm;
        [SerializeField] [Range(0.1f, 2f)] private float heightScale = 0.9f;
        [SerializeField] [Range(0.01f, 0.2f)] private float plateThickness = 0.05f;
        [SerializeField] [Range(0.1f, 1f)] private float opacity = 0.85f;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool showNegativeBelow = true;

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);
            var blob = CreateMaskDatabaseBlob(baker);
            baker.AddBlobAsset(ref blob, out _);

            baker.AddComponent(entity, new SpatialMaskDatabase { Blob = blob });
            baker.AddComponent(entity, new SpatialFocusedMap
            {
                CellSize = cellSize,
                Size = activeMapSize
            });
            baker.AddBuffer<SpatialNeighbors>(entity);

            // Allow tracking
            baker.AddComponent<SpatialTrackingActive>(entity);

            baker.AddComponent(entity, new SpatialDebugVisualization
            {
                Mode = visMode,
                Palette = palette,
                HeightScale = heightScale,
                PlateThickness = plateThickness,
                Opacity = opacity,
                ShowGrid = (byte)(showGrid ? 1 : 0),
                ShowNegativeBelow = (byte)(showNegativeBelow ? 1 : 0)
            });
        }

        private BlobAssetReference<SpatialMaskDatabaseBlob> CreateMaskDatabaseBlob(IBaker baker)
        {
            var masks = ValidMasks(baker);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SpatialMaskDatabaseBlob>();
            var entries = builder.Allocate(ref root.Masks, MaskArrayLength(masks));

            for (var i = 0; i < masks.Count; i++)
            {
                var mask = masks[i];
                var values = builder.Allocate(ref entries[mask.Id].Values, mask.Width * mask.Height);

                var index = 0;
                for (var y = 0; y < mask.Height; y++)
                for (var x = 0; x < mask.Width; x++)
                    values[index++] = mask.Get(x, y);
            }

            var blob = builder.CreateBlobAssetReference<SpatialMaskDatabaseBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        private List<SpatialMaskAsset> ValidMasks(IBaker baker)
        {
            var result = new List<SpatialMaskAsset>();
            var keys = new HashSet<ushort>();

            foreach (var mask in schemas)
            {
                if (mask == null || mask.Id == 0)
                    continue;

                if (mask.Width != mask.Height || (mask.Width & 1) == 0)
                {
                    Debug.LogError($"Spatial mask {mask.name} must be odd square.", mask);
                    continue;
                }

                if (!keys.Add(mask.Id))
                {
                    Debug.LogError($"Duplicate spatial mask key {mask.Id} on {mask.name}.", mask);
                    continue;
                }

                result.Add(mask);
                baker.DependsOn(mask);
            }

            return result;
        }

        private static int MaskArrayLength(List<SpatialMaskAsset> masks)
        {
            var length = 0;
            for (var i = 0; i < masks.Count; i++)
                length = Mathf.Max(length, masks[i].Id + 1);

            return length;
        }

        public static bool TryGetKey(SpatialMaskAsset mask, out ushort key)
        {
            key = mask == null ? (ushort)0 : mask.Id;
            return key != 0;
        }
    }
}