using BovineLabs.Core.Iterators;
using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    /// <summary>
    ///     Tag component to mark the system entity as having a heatmap buffer.
    ///     The actual data lives in <see cref="SpatialHeatmapBuffer" />.
    /// </summary>
    public struct SpatialHeatmapSingleton : IComponentData
    {
    }

    /// <summary>
    ///     DynamicBuffer-backed hash map for spatial heatmap data.
    ///     Replaces the old NativeParallelHashMap stored inside IComponentData,
    ///     eliminating manual Dispose() and heap-allocation lifecycle issues.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct SpatialHeatmapBuffer : IDynamicHashMap<int, int>
    {
        public byte Value { get; }
    }
}