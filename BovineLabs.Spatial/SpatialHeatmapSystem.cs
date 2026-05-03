// BovineLabs.Spatial/SpatialHeatmapSystem.cs

using BovineLabs.Timeline;

namespace BovineLabs.Spatial
{
    using BovineLabs.Core.Spatial;
    using BovineLabs.Spatial.Data;
    using BovineLabs.Timeline.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Transforms;

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(SpatialMapBuildSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct SpatialHeatmapSystem : ISystem
    {
        private NativeParallelMultiHashMap<int, int> multiMap;
        private NativeParallelHashMap<int, int> heatmap;
        private NativeList<int> uniqueKeys;
        
        private ComponentLookup<LocalToWorld> transformLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.multiMap = new NativeParallelMultiHashMap<int, int>(1024, Allocator.Persistent);
            this.heatmap = new NativeParallelHashMap<int, int>(1024, Allocator.Persistent);
            this.uniqueKeys = new NativeList<int>(1024, Allocator.Persistent);

            this.transformLookup = state.GetComponentLookup<LocalToWorld>(true);

            state.EntityManager.AddComponent<SpatialHeatmapSingleton>(state.SystemHandle);

            state.RequireForUpdate<SpatialMaskDatabase>();
            state.RequireForUpdate<SpatialTrackingActive>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            this.multiMap.Dispose();
            this.heatmap.Dispose();
            this.uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var buildSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialMapBuildSystem>();
            if (!state.EntityManager.HasComponent<SpatialMapSingleton>(buildSystemHandle)) return;

            var mapSingleton = state.EntityManager.GetComponentData<SpatialMapSingleton>(buildSystemHandle);
            if (!mapSingleton.Map.Map.IsCreated) return;

            this.multiMap.Clear();
            this.heatmap.Clear();

            var maskDatabase = SystemAPI.GetSingleton<SpatialMaskDatabase>();

            SystemAPI.SetComponent(state.SystemHandle, new SpatialHeatmapSingleton { Map = this.heatmap });

            this.transformLookup.Update(ref state);

            var gatherJob = new GatherHeatmapJob
            {
                MapSingleton = mapSingleton,
                MaskDatabase = maskDatabase,
                TransformLookup = this.transformLookup,
                MultiMap = this.multiMap.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            var keysJob = new GetUniqueKeysJob
            {
                MultiMap = this.multiMap,
                UniqueKeys = this.uniqueKeys
            }.Schedule(gatherJob);

            state.Dependency = new ReduceHeatmapJob
            {
                Keys = this.uniqueKeys.AsDeferredJobArray(),
                MultiMap = this.multiMap.AsReadOnly(),
                Heatmap = this.heatmap.AsParallelWriter()
            }.Schedule(this.uniqueKeys, 64, keysJob);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherHeatmapJob : IJobEntity
        {
            [ReadOnly] public SpatialMapSingleton MapSingleton;
            [ReadOnly] public SpatialMaskDatabase MaskDatabase;
            [ReadOnly] public ComponentLookup<LocalToWorld> TransformLookup;
            
            public NativeParallelMultiHashMap<int, int>.ParallelWriter MultiMap;

            private void Execute(in SpatialActiveClipData clipData, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !this.MaskDatabase.Blob.Value.Has(clipData.MaskKey)) return;
                if (!this.TransformLookup.TryGetComponent(binding.Value, out var casterTransform)) return;

                var centerCell = this.MapSingleton.Map.Quantized(casterTransform.Position.xz - this.MapSingleton.CameraPos);
                ref var mask = ref this.MaskDatabase.Blob.Value.Masks[clipData.MaskKey];

                for (var y = 0; y < mask.Size; y++)
                {
                    for (var x = 0; x < mask.Size; x++)
                    {
                        var weight = mask.Get(x, y);
                        if (weight <= 0) continue;

                        var cell = centerCell + new int2(x - mask.Size / 2, mask.Size / 2 - y);
                        var hash = this.MapSingleton.Map.Hash(cell); 

                        this.MultiMap.Add(hash, weight);
                    }
                }
            }
        }

        [BurstCompile]
        private struct GetUniqueKeysJob : IJob
        {
            public NativeParallelMultiHashMap<int, int> MultiMap;
            public NativeList<int> UniqueKeys;

            public void Execute()
            {
                BovineLabs.Core.Extensions.NativeParallelMultiHashMapExtensions.GetUniqueKeyArray(this.MultiMap, this.UniqueKeys);
            }
        }

        [BurstCompile]
        private struct ReduceHeatmapJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<int, int>.ReadOnly MultiMap;
            public NativeParallelHashMap<int, int>.ParallelWriter Heatmap;

            public void Execute(int index)
            {
                var key = this.Keys[index];
                var sum = 0;

                if (this.MultiMap.TryGetFirstValue(key, out var value, out var it))
                {
                    sum += value;
                    while (this.MultiMap.TryGetNextValue(out value, ref it))
                    {
                        sum += value;
                    }
                }

                this.Heatmap.TryAdd(key, sum);
            }
        }
    }
}