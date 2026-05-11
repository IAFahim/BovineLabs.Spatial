using BovineLabs.Core.Iterators;
using BovineLabs.Spatial.Data;
using BovineLabs.Timeline;
using BovineLabs.Timeline.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using NativeParallelMultiHashMapExtensions = BovineLabs.Core.Extensions.NativeParallelMultiHashMapExtensions;

namespace BovineLabs.Spatial
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(SpatialMapBuildSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct SpatialHeatmapSystem : ISystem
    {
        private NativeParallelMultiHashMap<int, int> multiMap;
        private NativeList<int> uniqueKeys;

        private ComponentLookup<LocalToWorld> transformLookup;
        private BufferLookup<SpatialHeatmapBuffer> heatmapBufferLookup;
        private Entity systemEntity;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            multiMap = new NativeParallelMultiHashMap<int, int>(1024, Allocator.Persistent);
            uniqueKeys = new NativeList<int>(1024, Allocator.Persistent);

            transformLookup = state.GetComponentLookup<LocalToWorld>(true);
            heatmapBufferLookup = state.GetBufferLookup<SpatialHeatmapBuffer>();

            state.EntityManager.AddComponent<SpatialHeatmapSingleton>(state.SystemHandle);
            systemEntity = Entity.Null;

            state.RequireForUpdate<SpatialMaskDatabase>();
            state.RequireForUpdate<SpatialTrackingActive>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            multiMap.Dispose();
            uniqueKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Lazy-init: add the heatmap buffer to the system entity on first available frame
            if (systemEntity == Entity.Null)
            {
                systemEntity = SystemAPI.GetSingletonEntity<SpatialHeatmapSingleton>();
                state.EntityManager.AddBuffer<SpatialHeatmapBuffer>(systemEntity);
                heatmapBufferLookup.Update(ref state);
            }

            var buildSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialMapBuildSystem>();
            if (!state.EntityManager.HasComponent<SpatialMapSingleton>(buildSystemHandle)) return;

            var mapSingleton = state.EntityManager.GetComponentData<SpatialMapSingleton>(buildSystemHandle);
            if (!mapSingleton.Map.Map.IsCreated) return;

            multiMap.Clear();

            var maskDatabase = SystemAPI.GetSingleton<SpatialMaskDatabase>();

            transformLookup.Update(ref state);
            heatmapBufferLookup.Update(ref state);

            var gatherJob = new GatherHeatmapJob
            {
                MapSingleton = mapSingleton,
                MaskDatabase = maskDatabase,
                TransformLookup = transformLookup,
                MultiMap = multiMap.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            var keysJob = new GetUniqueKeysJob
            {
                MultiMap = multiMap,
                UniqueKeys = uniqueKeys
            }.Schedule(gatherJob);

            state.Dependency = new ReduceHeatmapJob
            {
                Keys = uniqueKeys.AsDeferredJobArray(),
                MultiMap = multiMap.AsReadOnly(),
                HeatmapBuffers = heatmapBufferLookup,
                SystemEntity = systemEntity
            }.Schedule(keysJob);
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
                if (binding.Value == Entity.Null || !MaskDatabase.Blob.Value.Has(clipData.MaskKey)) return;
                if (!TransformLookup.TryGetComponent(binding.Value, out var casterTransform)) return;

                var centerCell = MapSingleton.Map.Quantized(casterTransform.Position.xz - MapSingleton.CameraPos);
                ref var mask = ref MaskDatabase.Blob.Value.Masks[clipData.MaskKey];

                for (var y = 0; y < mask.Size; y++)
                for (var x = 0; x < mask.Size; x++)
                {
                    var weight = mask.Get(x, y);
                    if (weight == 0) continue;

                    var cell = centerCell + new int2(x - mask.Size / 2, mask.Size / 2 - y);
                    var hash = MapSingleton.Map.Hash(cell);

                    MultiMap.Add(hash, weight);
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
                NativeParallelMultiHashMapExtensions.GetUniqueKeyArray(MultiMap, UniqueKeys);
            }
        }

        [BurstCompile]
        private struct ReduceHeatmapJob : IJob
        {
            [ReadOnly] public NativeArray<int> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<int, int>.ReadOnly MultiMap;
            public BufferLookup<SpatialHeatmapBuffer> HeatmapBuffers;
            public Entity SystemEntity;

            public void Execute()
            {
                if (!HeatmapBuffers.TryGetBuffer(SystemEntity, out var buffer)) return;

                // Clear or initialize the DynamicBuffer-backed hashmap
                if (buffer.Length > 0)
                    buffer.AsHashMap<SpatialHeatmapBuffer, int, int>().Clear();
                else
                    buffer.InitializeHashMap<SpatialHeatmapBuffer, int, int>(1024);

                var map = buffer.AsHashMap<SpatialHeatmapBuffer, int, int>();

                for (var index = 0; index < Keys.Length; index++)
                {
                    var key = Keys[index];
                    var sum = 0;

                    if (MultiMap.TryGetFirstValue(key, out var value, out var it))
                    {
                        sum += value;
                        while (MultiMap.TryGetNextValue(out value, ref it)) sum += value;
                    }

                    map.TryAdd(key, sum);
                }
            }
        }
    }
}