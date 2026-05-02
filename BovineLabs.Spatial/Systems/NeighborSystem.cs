using BovineLabs.Bridge.Data.Camera;
using BovineLabs.Core.Extensions;
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
            trackers = SystemAPI.QueryBuilder().WithAll<NeighborTracker, LocalTransform>().WithAllRW<Neighbor>().Build();
            targets = SystemAPI.QueryBuilder().WithAll<NeighborTarget, LocalTransform>().Build();
            camera = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalTransform>().Build();

            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate(trackers);
            state.RequireForUpdate(targets);
            state.RequireForUpdate(camera);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
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
                CameraPositionXZ = centerPosition.xz,
                MaxDistanceSq = config.ActiveMapSize * 0.5f * (config.ActiveMapSize * 0.5f),
                HalfSize = new float2(config.ActiveMapSize * 0.5f),
                TransformHandle = SystemAPI.GetComponentTypeHandle<LocalTransform>(true),
                EntityHandle = SystemAPI.GetEntityTypeHandle(),
                Positions = positions.AsParallelWriter()
            }.ScheduleParallel(targets, state.Dependency);

            var map = new SpatialMap<SpatialEntityPosition>(config.CellSize, config.ActiveMapSize, state.WorldUpdateAllocator);
            state.Dependency = map.Build(positions, state.Dependency);

            state.Dependency = new FindNeighborsJob
            {
                Map = map.AsReadOnly(),
                Targets = positions.AsDeferredJobArray(),
                CameraPositionXZ = centerPosition.xz,
                CellSize = config.CellSize,
                GridWidth = (int)math.ceil(config.ActiveMapSize / config.CellSize),
                HalfSize = new float2(config.ActiveMapSize * 0.5f)
            }.ScheduleParallel(trackers, state.Dependency);
        }

        [BurstCompile]
        private unsafe struct GatherTargetsJob : IJobChunk
        {
            public float2 CameraPositionXZ;
            public float MaxDistanceSq;
            public float2 HalfSize;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            public NativeList<SpatialEntityPosition>.ParallelWriter Positions;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var entities = chunk.GetNativeArray(EntityHandle);

                int count = 0;
                for (var i = 0; i < chunk.Count; i++)
                {
                    if (math.distancesq(transforms[i].Position.xz, CameraPositionXZ) <= MaxDistanceSq) count++;
                }

                if (count == 0) return;

                // 1. Atomic grab chunk capacity. Guaranteed no race condition.
                Positions.ReserveNoResize(count, out var ptr, out _);

                int written = 0;
                for (var i = 0; i < chunk.Count; i++)
                {
                    var pos = transforms[i].Position;
                    if (math.distancesq(pos.xz, CameraPositionXZ) <= MaxDistanceSq)
                    {
                        var mapPos = pos.xz - CameraPositionXZ;
                        // 2. Clamp relative sliding window to avoid bounds crash
                        mapPos = math.clamp(mapPos, -HalfSize + 0.001f, HalfSize - 0.001f);

                        ptr[written++] = new SpatialEntityPosition 
                        { 
                            Entity = entities[i], 
                            WorldPosition = pos,
                            MapPosition = mapPos
                        };
                    }
                }
            }
        }

        [BurstCompile]
        private partial struct FindNeighborsJob : IJobEntity
        {
            [ReadOnly] public SpatialMap.ReadOnly Map;
            [ReadOnly] public NativeArray<SpatialEntityPosition> Targets;
            public float2 CameraPositionXZ;
            public float CellSize;
            public int GridWidth;
            public float2 HalfSize;

            private void Execute(Entity entity, ref DynamicBuffer<Neighbor> neighbors, in NeighborTracker tracker, in LocalTransform transform)
            {
                neighbors.Clear();
                var mapCenter = transform.Position.xz - CameraPositionXZ;
                mapCenter = math.clamp(mapCenter, -HalfSize + 0.001f, HalfSize - 0.001f);

                var range = tracker.Range;
                var rangeSq = range * range;
                var min = Map.Quantized(mapCenter - new float2(range));
                var max = Map.Quantized(mapCenter + new float2(range));

                // Restrict cell scan exactly
                min = math.max(min, int2.zero);
                max = math.min(max, new int2(GridWidth - 1));

                for (var y = min.y; y <= max.y; y++)
                for (var x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);

                    // 3. Reject outer corner grid cells before cache iterating
                    if (CalculateSquareCellMinDistanceSq(mapCenter, cell, CellSize, HalfSize) > rangeSq) continue;

                    var hash = Map.Hash(cell);
                    if (Map.Map.TryGetFirstValue(hash, out var targetIndex, out var it))
                    {
                        do
                        {
                            ref readonly var target = ref Targets.ElementAtRO(targetIndex);
                            if (target.Entity == entity) continue;

                            var distSq = math.distancesq(mapCenter, target.MapPosition);
                            if (distSq <= rangeSq)
                            {
                                neighbors.Add(new Neighbor
                                {
                                    Entity = target.Entity,
                                    DistanceSq = distSq
                                });
                            }
                        } while (Map.Map.TryGetNextValue(out targetIndex, ref it));
                    }
                }
            }

            private static float CalculateSquareCellMinDistanceSq(float2 position, int2 cell, float quantizeStep, float2 halfSize)
            {
                var min = (new float2(cell.x, cell.y) * quantizeStep) - halfSize;
                var max = min + quantizeStep;
                var dx = math.max(0f, math.max(min.x - position.x, position.x - max.x));
                var dy = math.max(0f, math.max(min.y - position.y, position.y - max.y));
                return (dx * dx) + (dy * dy);
            }
        }
    }
}