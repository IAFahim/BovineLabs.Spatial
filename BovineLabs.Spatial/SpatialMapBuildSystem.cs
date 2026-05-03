// BovineLabs.Spatial/SpatialMapBuildSystem.cs
namespace BovineLabs.Spatial
{
    using BovineLabs.Bridge.Data.Camera;
    using BovineLabs.Core.Spatial;
    using BovineLabs.Spatial.Data;
    using Unity.Burst;
    using Unity.Burst.Intrinsics;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Transforms;

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateAfter(typeof(SpatialTrackCollectSystem))]
    public partial struct SpatialMapBuildSystem : ISystem
    {
        private SpatialMap<SpatialPosition> map;
        private NativeList<SpatialPosition> positions;
        
        public NativeList<Entity> Entities;
        
        private EntityQuery targetQuery;
        private EntityQuery cameraQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.targetQuery = SystemAPI.QueryBuilder().WithAll<SpatialTarget, LocalToWorld>().Build();
            this.cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalToWorld>().Build();
            this.positions = new NativeList<SpatialPosition>(Allocator.Persistent);
            this.Entities = new NativeList<Entity>(Allocator.Persistent);
            
            state.EntityManager.AddComponent<SpatialMapSingleton>(state.SystemHandle);

            state.RequireForUpdate<SpatialFocusedMap>();
            state.RequireForUpdate<SpatialTrackingActive>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (this.map.IsCreated) this.map.Dispose();
            this.positions.Dispose();
            this.Entities.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // var collect = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialTrackCollectSystem>();
            // if (collect!= SystemHandle.Null)
            // {
            //     ref var collectState = ref state.WorldUnmanaged.ResolveSystemStateRef(collect);
            //     state.Dependency = JobHandle.CombineDependencies(state.Dependency, collectState.Dependency);
            // }
            state.Dependency.Complete();

            var focus = SystemAPI.GetSingleton<SpatialFocusedMap>();
            var physicalSize = (int)math.ceil(focus.Size * focus.CellSize);

            if (!this.map.IsCreated)
            {
                this.map = new SpatialMap<SpatialPosition>(focus.CellSize, physicalSize, Allocator.Persistent);
            }

            var camPos = float2.zero;
            if (!this.cameraQuery.IsEmpty)
            {
                var ltw = this.cameraQuery.GetSingleton<LocalToWorld>();
                var origin = ltw.Position;
                var forward = ltw.Forward;
                if (math.abs(forward.y) > 1e-6f)
                {
                    var t = -origin.y / forward.y;
                    if (t > 0)
                        camPos = (origin + forward * t).xz;
                }
            }

            var count = this.targetQuery.CalculateEntityCount();
            this.positions.ResizeUninitialized(count);
            this.Entities.ResizeUninitialized(count);

            var gatherJob = new GatherJob
            {
                CameraPos = camPos,
                Positions = this.positions.AsArray(),
                Entities = this.Entities.AsArray(),
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalToWorld>(true),
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                BaseIndices = this.targetQuery.CalculateBaseEntityIndexArrayAsync(state.WorldUpdateAllocator, state.Dependency, out var baseDep)
            }.ScheduleParallel(this.targetQuery, JobHandle.CombineDependencies(state.Dependency, baseDep));

            state.Dependency = this.map.Build(this.positions.AsDeferredJobArray(), gatherJob);

            SystemAPI.SetComponent(state.SystemHandle, new SpatialMapSingleton
            {
                Map = this.map.AsReadOnly(),
                CameraPos = camPos,
                CellSize = focus.CellSize
            });
        }

        [BurstCompile]
        private struct GatherJob : IJobChunk
        {
            public float2 CameraPos;
            [NativeDisableContainerSafetyRestriction] public NativeArray<SpatialPosition> Positions;
            [NativeDisableContainerSafetyRestriction] public NativeArray<Entity> Entities;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> TransformHandle;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public NativeArray<int> BaseIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref this.TransformHandle);
                var chunkEntities = chunk.GetNativeArray(this.EntityHandle);
                var baseIdx = this.BaseIndices[unfilteredChunkIndex];

                for (var i = 0; i < chunk.Count; i++)
                {
                    this.Positions[baseIdx + i] = new SpatialPosition { Position = new float3(transforms[i].Position.x - this.CameraPos.x, 0, transforms[i].Position.z - this.CameraPos.y) };
                    this.Entities[baseIdx + i] = chunkEntities[i];
                }
            }
        }
    }
}