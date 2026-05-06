using BovineLabs.Spatial.Data;
using Unity.Entities;
using UnityEngine;
using TransformAuthoring = BovineLabs.Core.Authoring.TransformAuthoring;

namespace BovineLabs.Spatial.Authoring
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TransformAuthoring))]
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