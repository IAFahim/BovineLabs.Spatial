using BovineLabs.Bridge.Data.Camera;
using BovineLabs.Core.Spatial;
using BovineLabs.Spatial.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Spatial.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct NeighborSystem : ISystem
    {
        private EntityQuery trackers;
        private EntityQuery targets;
        private EntityQuery camera;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            trackers = SystemAPI.QueryBuilder().WithAll<NeighborTracker, LocalTransform>().WithAllRW<Neighbor>()
                .Build();
            targets = SystemAPI.QueryBuilder().WithAll<NeighborTarget, LocalTransform>().Build();
            camera = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalTransform>().Build();

            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate(trackers);
            state.RequireForUpdate(camera);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();

            var config = SystemAPI.GetSingleton<SpatialGridConfig>();
            var camLtw = camera.GetSingleton<LocalTransform>();
            var dir = math.forward(camLtw.Rotation);
            var centerPosition = camLtw.Position;
            if (math.abs(dir.y) > 0.001f)
            {
                var t = -camLtw.Position.y / dir.y;
                centerPosition += dir * t;
            }
            else
            {
                centerPosition += dir * config.CameraOffset;
            }

            var targetCount = targets.CalculateEntityCount();
            var positions = new NativeList<SpatialEntityPosition>(targetCount, state.WorldUpdateAllocator);

            state.Dependency = new GatherTargetsJob
            {
                CameraPosition = centerPosition,
                MaxDistanceSq = config.ActiveMapSize * 0.5f * (config.ActiveMapSize * 0.5f),
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                Positions = positions.AsParallelWriter()
            }.ScheduleParallel(targets, state.Dependency);

            var map = new SpatialMap<SpatialEntityPosition>(config.CellSize, config.ActiveMapSize,
                state.WorldUpdateAllocator);
            state.Dependency = map.Build(positions, state.Dependency);

            state.Dependency = new FindNeighborsJob
            {
                Map = map.AsReadOnly(),
                Targets = positions.AsDeferredJobArray()
            }.ScheduleParallel(trackers, state.Dependency);
        }

        [BurstCompile]
        private struct GatherTargetsJob : IJobChunk
        {
            public float3 CameraPosition;
            public float MaxDistanceSq;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            public NativeList<SpatialEntityPosition>.ParallelWriter Positions;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var entities = chunk.GetNativeArray(EntityHandle);

                for (var i = 0; i < chunk.Count; i++)
                    if (TryGetTargetWithinCameraBounds(transforms[i].Position, CameraPosition, MaxDistanceSq,
                            entities[i], out var position))
                        Positions.AddNoResize(position);
            }

            private static bool TryGetTargetWithinCameraBounds(float3 targetPos, float3 cameraPos, float maxDistanceSq,
                Entity entity, out SpatialEntityPosition position)
            {
                position = default;
                if (math.distancesq(targetPos.xz, cameraPos.xz) <= maxDistanceSq)
                {
                    position = new SpatialEntityPosition { Entity = entity, WorldPosition = targetPos };
                    return true;
                }

                return false;
            }
        }

        [BurstCompile]
        private partial struct FindNeighborsJob : IJobEntity
        {
            [ReadOnly] public SpatialMap.ReadOnly Map;
            [ReadOnly] public NativeArray<SpatialEntityPosition> Targets;

            private void Execute(Entity entity, ref DynamicBuffer<Neighbor> neighbors, in NeighborTracker tracker,
                in LocalTransform transform)
            {
                neighbors.Clear();
                TryGetNeighbors(in Map, Targets, transform.Position.xz, tracker.Range, entity, ref neighbors);
            }

            private static bool TryGetNeighbors(in SpatialMap.ReadOnly map,
                in NativeArray<SpatialEntityPosition> targets, float2 center, float range, Entity self,
                ref DynamicBuffer<Neighbor> neighbors)
            {
                var rangeSq = range * range;
                var min = map.Quantized(center - new float2(range));
                var max = map.Quantized(center + new float2(range));

                for (var y = min.y; y <= max.y; y++)
                for (var x = min.x; x <= max.x; x++)
                {
                    var hash = map.Hash(new int2(x, y));
                    if (map.Map.TryGetFirstValue(hash, out var targetIndex, out var it))
                        do
                        {
                            var target = targets[targetIndex];
                            if (target.Entity == self) continue;

                            var distSq = math.distancesq(center, target.WorldPosition.xz);
                            if (distSq <= rangeSq)
                                neighbors.Add(new Neighbor
                                {
                                    Entity = target.Entity,
                                    DistanceSq = distSq
                                });
                        } while (map.Map.TryGetNextValue(out targetIndex, ref it));
                }

                return !neighbors.IsEmpty;
            }
        }
    }
}