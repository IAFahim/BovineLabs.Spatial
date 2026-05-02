#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Bridge.Data.Camera;
using BovineLabs.Core;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Quill;
using BovineLabs.Spatial.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Spatial.Debug
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct NeighborDebugSystem : ISystem
    {
        private EntityQuery cameraQuery;
        private UnsafeComponentLookup<LocalTransform> transformLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalTransform>().Build();
            transformLookup = state.GetUnsafeComponentLookup<LocalTransform>(true);
            
            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate(cameraQuery);
        }
        

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            transformLookup.Update(ref state);

            var config = SystemAPI.GetSingleton<SpatialGridConfig>();
            var camLtw = cameraQuery.GetSingleton<LocalTransform>();
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

            var renderer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();

            state.Dependency = new GridDebugJob
            {
                Renderer = renderer,
                CameraPosition = centerPosition,
                CellSize = config.CellSize,
                ActiveMapSize = config.ActiveMapSize
            }.Schedule(state.Dependency);

            state.Dependency = new TargetDebugJob
            {
                Renderer = renderer,
                CellSize = config.CellSize
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new TrackerDebugJob
            {
                Renderer = renderer,
                TransformLookup = transformLookup
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private struct GridDebugJob : IJob
        {
            public Drawer Renderer;
            public float3 CameraPosition;
            public float CellSize;
            public int ActiveMapSize;

            public void Execute()
            {
                var halfSize = ActiveMapSize * 0.5f;
                var minX = math.floor((CameraPosition.x - halfSize) / CellSize) * CellSize;
                var maxX = math.ceil((CameraPosition.x + halfSize) / CellSize) * CellSize;
                var minZ = math.floor((CameraPosition.z - halfSize) / CellSize) * CellSize;
                var maxZ = math.ceil((CameraPosition.z + halfSize) / CellSize) * CellSize;

                var color = new Color(1f, 1f, 1f, 0.2f);

                for (var x = minX; x <= maxX; x += CellSize)
                    Renderer.Line(new float3(x, CameraPosition.y, minZ), new float3(x, CameraPosition.y, maxZ), color);

                for (var z = minZ; z <= maxZ; z += CellSize)
                    Renderer.Line(new float3(minX, CameraPosition.y, z), new float3(maxX, CameraPosition.y, z), color);
            }
        }

        [BurstCompile]
        private partial struct TargetDebugJob : IJobEntity
        {
            public Drawer Renderer;
            public float CellSize;

            private void Execute(in NeighborTarget target, in LocalTransform transform)
            {
                var cell = (int2)math.floor(transform.Position.xz / CellSize);
                var min = new float3(cell.x * CellSize, transform.Position.y, cell.y * CellSize);
                var max = min + new float3(CellSize, 0, CellSize);

                var p0 = min;
                var p1 = new float3(max.x, min.y, min.z);
                var p2 = max;
                var p3 = new float3(min.x, min.y, max.z);

                Renderer.SolidQuad(p0, p1, p2, p3, new Color(1f, 1f, 1f, 0.3f));

                Renderer.Point(transform.Position, 0.1f, Color.green);
            }
        }

        [BurstCompile]
        private partial struct TrackerDebugJob : IJobEntity
        {
            public Drawer Renderer;
            [ReadOnly] public UnsafeComponentLookup<LocalTransform> TransformLookup;

            private void Execute(in NeighborTracker tracker, in DynamicBuffer<Neighbor> neighbors,
                in LocalTransform transform)
            {
                Renderer.Circle(transform.Position, math.up() * tracker.Range, Color.green);

                for (var i = 0; i < neighbors.Length; i++)
                    if (TransformLookup.TryGetComponent(neighbors[i].Entity, out var neighborTransform))
                        Renderer.Line(transform.Position, neighborTransform.Position, Color.red);
            }
        }
    }
}
#endif