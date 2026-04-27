#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Bridge.Data.Camera;
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
    [UpdateInGroup(typeof(BovineLabs.Core.DebugSystemGroup))]
    public partial struct NeighborDebugSystem : ISystem
    {
        private EntityQuery cameraQuery;
        private UnsafeComponentLookup<LocalTransform> transformLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.cameraQuery = SystemAPI.QueryBuilder().WithAll<CameraMain, LocalTransform>().Build();
            this.transformLookup = state.GetUnsafeComponentLookup<LocalTransform>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // intnetoally retuned for now
            return;
            this.transformLookup.Update(ref state);
            var config = SystemAPI.GetSingleton<SpatialGridConfig>();
            var camLtw = this.cameraQuery.GetSingleton<LocalTransform>();
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
            }.Schedule(state.Dependency);

            state.Dependency = new TrackerDebugJob
            {
                Renderer = renderer,
                TransformLookup = this.transformLookup
            }.Schedule(state.Dependency);
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
                var halfSize = this.ActiveMapSize * 0.5f;
                var minX = math.floor((this.CameraPosition.x - halfSize) / this.CellSize) * this.CellSize;
                var maxX = math.ceil((this.CameraPosition.x + halfSize) / this.CellSize) * this.CellSize;
                var minZ = math.floor((this.CameraPosition.z - halfSize) / this.CellSize) * this.CellSize;
                var maxZ = math.ceil((this.CameraPosition.z + halfSize) / this.CellSize) * this.CellSize;

                var color = new Color(1f, 1f, 1f, 0.2f);

                for (var x = minX; x <= maxX; x += this.CellSize)
                {
                    this.Renderer.Line(new float3(x, this.CameraPosition.y, minZ), new float3(x, this.CameraPosition.y, maxZ), color);
                }

                for (var z = minZ; z <= maxZ; z += this.CellSize)
                {
                    this.Renderer.Line(new float3(minX, this.CameraPosition.y, z), new float3(maxX, this.CameraPosition.y, z), color);
                }
            }
        }

        [BurstCompile]
        private partial struct TargetDebugJob : IJobEntity
        {
            public Drawer Renderer;
            public float CellSize;

            private void Execute(in NeighborTarget target, in LocalTransform transform)
            {
                var cell = (int2)math.floor(transform.Position.xz / this.CellSize);
                var min = new float3(cell.x * this.CellSize, transform.Position.y, cell.y * this.CellSize);
                var max = min + new float3(this.CellSize, 0, this.CellSize);
                
                var p0 = min;
                var p1 = new float3(max.x, min.y, min.z);
                var p2 = max;
                var p3 = new float3(min.x, min.y, max.z);

                this.Renderer.SolidQuad(p0, p1, p2, p3, new Color(1f, 1f, 1f, 0.3f));

                this.Renderer.Point(transform.Position, 0.1f, Color.green);
            }
        }

        [BurstCompile]
        private partial struct TrackerDebugJob : IJobEntity
        {
            public Drawer Renderer;
            [ReadOnly] public UnsafeComponentLookup<LocalTransform> TransformLookup;

            private void Execute(in NeighborTracker tracker, in DynamicBuffer<Neighbor> neighbors, in LocalTransform transform)
            {
                this.Renderer.Circle(transform.Position, math.up() * tracker.Range, Color.green);

                for (var i = 0; i < neighbors.Length; i++)
                {
                    if (this.TransformLookup.TryGetComponent(neighbors[i].Entity, out var neighborTransform))
                    {
                        this.Renderer.Line(transform.Position, neighborTransform.Position, Color.red);
                    }
                }
            }
        }
    }
}
#endif
