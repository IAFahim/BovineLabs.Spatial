using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    public class NeighborTargetAuthoring : MonoBehaviour
    {
        public class TargetBaker : Baker<NeighborTargetAuthoring>
        {
            public override void Bake(NeighborTargetAuthoring authoring)
            {
                var entity = this.GetEntity(TransformUsageFlags.Dynamic);
                this.AddComponent(entity, new NeighborTarget());
            }
        }
    }
}
