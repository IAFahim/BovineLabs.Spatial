using System;
using BovineLabs.Essence.Data;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialActiveClipData : IComponentData
    {
        public ushort MaskKey;
        public Target RouteTo;
        public ushort RouteLinkKey;
        public ConditionKey OnEnter;
        public ConditionKey OnExit;
        public IntrinsicKey IntrinsicStore;
    }

    [InternalBufferCapacity(0)]
    public struct SpatialActiveTarget : IBufferElementData, IComparable<SpatialActiveTarget>
    {
        public Entity Target;

        public int CompareTo(SpatialActiveTarget other)
        {
            var c = Target.Index.CompareTo(other.Target.Index);
            if (c == 0) c = Target.Version.CompareTo(other.Target.Version);
            return c;
        }
    }
}