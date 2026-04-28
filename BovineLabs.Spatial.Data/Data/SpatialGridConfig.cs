using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialGridConfig : IComponentData
    {
        public float CellSize;
        public int ActiveMapSize;
        public float CameraOffset;
    }
}