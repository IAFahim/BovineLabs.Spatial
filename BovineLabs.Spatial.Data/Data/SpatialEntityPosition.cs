using BovineLabs.Core.Spatial;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialEntityPosition : ISpatialPosition
    {
        public Entity Entity;
        public float3 WorldPosition;
        public float2 MapPosition;

        float2 ISpatialPosition.Position => MapPosition;
    }
}