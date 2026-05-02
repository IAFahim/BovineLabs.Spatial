using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    [DisallowMultipleComponent]
    public class SpatialTargetAuthoring : MonoBehaviour
    {
        private class Baker : Baker<SpatialTargetAuthoring>
        {
            public override void Bake(SpatialTargetAuthoring authoring)
            {
                AddComponent<SpatialTarget>(GetEntity(TransformUsageFlags.None));
            }
        }
    }
}