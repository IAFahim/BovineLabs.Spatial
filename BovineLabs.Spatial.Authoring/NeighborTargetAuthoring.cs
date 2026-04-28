using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    [RequireComponent(typeof(TransformAuthoring))]
    public class NeighborTargetAuthoring : MonoBehaviour
    {
        public class TargetBaker : Baker<NeighborTargetAuthoring>
        {
            public override void Bake(NeighborTargetAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new NeighborTarget());
            }
        }
    }
}