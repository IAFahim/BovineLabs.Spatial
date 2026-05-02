using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    [DisallowMultipleComponent]
    public class SpatialTrackEntityAuthoring : MonoBehaviour
    {
        private class Baker : Baker<SpatialTrackEntityAuthoring>
        {
            public override void Bake(SpatialTrackEntityAuthoring authoring)
            {
                AddComponent<SpatialTrackEntity>(GetEntity(TransformUsageFlags.None));
            }
        }
    }
}