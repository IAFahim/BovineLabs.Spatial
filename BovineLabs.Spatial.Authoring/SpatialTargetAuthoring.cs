using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BovineLabs.Core.Authoring.TransformAuthoring))]
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