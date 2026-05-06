using BovineLabs.Core.PhysicsStates;
using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialTrackingActive : IComponentData, IEnableableComponent
    {
    }

    public struct SpatialFocusedMap : IComponentData
    {
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