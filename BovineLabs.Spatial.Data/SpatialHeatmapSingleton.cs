using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialHeatmapSingleton : IComponentData
    {
        public NativeParallelHashMap<int, int> Map;
    }
}