using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    [InternalBufferCapacity(16)]
    public struct Neighbor : IBufferElementData
    {
        public Entity Entity;
        public float DistanceSq;
    }
}