using BovineLabs.Core.PhysicsStates;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialTrackingActive : IComponentData, IEnableableComponent
    {
    }

    public struct SpatialFocusedMap : IComponentData
    {
        public float3 Center;
        public int2 CenterCell;
        public float CellSize;
        public int Size;
    }

    public struct SpatialNeighbors : IBufferElementData
    {
        public Entity Entity;
        public StatefulEventState State;
        public sbyte XOffset;
        public sbyte YOffset;
    }
    
    
}