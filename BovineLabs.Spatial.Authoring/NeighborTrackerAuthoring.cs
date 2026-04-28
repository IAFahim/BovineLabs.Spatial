using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    public class NeighborTrackerAuthoring : MonoBehaviour
    {
        public float Range = 5f;

        public class TrackerBaker : Baker<NeighborTrackerAuthoring>
        {
            public override void Bake(NeighborTrackerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NeighborTracker { Range = authoring.Range });
                AddBuffer<Neighbor>(entity);
            }
        }
    }
}