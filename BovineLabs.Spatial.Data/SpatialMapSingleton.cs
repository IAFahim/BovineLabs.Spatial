using BovineLabs.Core.Spatial;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialMapSingleton : IComponentData
    {
        public SpatialMap.ReadOnly Map;
        public float2 CameraPos;
        public float CellSize;
    }
}