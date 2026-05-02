using BovineLabs.Core.Spatial;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialMapSingleton : IComponentData
    {
        public SpatialMap.ReadOnly Map;
        public NativeArray<Entity> Entities;
        public NativeArray<SpatialPosition> Positions;
        public float2 CameraPos;
        public float CellSize;
    }
}