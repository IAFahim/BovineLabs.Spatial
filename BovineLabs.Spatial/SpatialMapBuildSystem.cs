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

namespace BovineLabs.Spatial
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct SpatialMapBuildSystem : ISystem
    {
        private SpatialMap<SpatialPosition> map;
        private NativeList<SpatialPosition> positions;
        private NativeList<Entity> entities;
        private EntityQuery targetQuery;
        private EntityQuery cameraQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            targetQuery = SystemAPI.QueryBuilder().WithAll<SpatialTarget, LocalTransform>().Build();
            cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalToWorld>().Build();
            positions = new NativeList<SpatialPosition>(Allocator.Persistent);
            entities = new NativeList<Entity>(Allocator.Persistent);
            state.RequireForUpdate<SpatialFocusedMap>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (map.IsCreated) map.Dispose();
            positions.Dispose();
            entities.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();

            var focus = SystemAPI.GetSingleton<SpatialFocusedMap>();

            if (!map.IsCreated)
            {
                map = new SpatialMap<SpatialPosition>(focus.CellSize, focus.Size, Allocator.Persistent);
                state.EntityManager.AddComponentData(state.SystemHandle, new SpatialMapSingleton());
            }

            var camPos = float2.zero;
            if (!cameraQuery.IsEmpty)
            {
                var ltw = cameraQuery.GetSingleton<LocalToWorld>();
                var origin = ltw.Position;
                var forward = ltw.Forward;
                if (math.abs(forward.y) > 1e-6f)
                {
                    var t = -origin.y / forward.y;
                    if (t > 0)
                        camPos = (origin + forward * t).xz;
                }
            }

            var count = targetQuery.CalculateEntityCount();
            positions.ResizeUninitialized(count);
            entities.ResizeUninitialized(count);

            var gatherJob = new GatherJob
            {
                CameraPos = camPos,
                Positions = positions.AsArray(),
                Entities = entities.AsArray(),
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                BaseIndices = targetQuery.CalculateBaseEntityIndexArrayAsync(state.WorldUpdateAllocator, state.Dependency, out var baseDep)
            }.ScheduleParallel(targetQuery, JobHandle.CombineDependencies(state.Dependency, baseDep));

            state.Dependency = map.Build(positions.AsDeferredJobArray(), gatherJob);
            state.Dependency.Complete();

            SystemAPI.SetComponent(state.SystemHandle, new SpatialMapSingleton
            {
                Map = map.AsReadOnly(),
                Entities = entities.AsArray(),
                Positions = positions.AsArray(),
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
            [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public NativeArray<int> BaseIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var chunkEntities = chunk.GetNativeArray(EntityHandle);
                var baseIdx = BaseIndices[unfilteredChunkIndex];

                for (var i = 0; i < chunk.Count; i++)
                {
                    Positions[baseIdx + i] = new SpatialPosition { Position = new float3(transforms[i].Position.x - CameraPos.x, 0, transforms[i].Position.z - CameraPos.y) };
                    Entities[baseIdx + i] = chunkEntities[i];
                }
            }
        }
    }
}