namespace BovineLabs.Spatial.Data
{
    using BovineLabs.Core.Spatial;
    using Unity.Entities;
    using Unity.Mathematics;

    public struct SpatialMapSingleton : IComponentData
    {
        public SpatialMap.ReadOnly Map;
        public float2 CameraPos;
        public float CellSize;
    }
}