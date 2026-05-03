// BovineLabs.Spatial.Debug/SpatialTrackDebugSystem.cs
#if UNITY_EDITOR || BL_DEBUG
namespace BovineLabs.Spatial.Debug
{
    using BovineLabs.Core;
    using BovineLabs.Quill;
    using BovineLabs.Spatial.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Transforms;
    using UnityEngine;

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct SpatialTrackDebugSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var heatmapSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialHeatmapSystem>();
            if (!state.EntityManager.HasComponent<SpatialHeatmapSingleton>(heatmapSystemHandle)) return;

            var heatmap = state.EntityManager.GetComponentData<SpatialHeatmapSingleton>(heatmapSystemHandle);
            if (!heatmap.Map.IsCreated) return;

            var buildSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialMapBuildSystem>();
            if (!state.EntityManager.HasComponent<SpatialMapSingleton>(buildSystemHandle)) return;

            var mapSingleton = state.EntityManager.GetComponentData<SpatialMapSingleton>(buildSystemHandle);

            var drawer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();
            var focus = SystemAPI.GetSingleton<SpatialFocusedMap>();

            state.Dependency = new DrawGridJob
            {
                Drawer = drawer,
                MapSingleton = mapSingleton,
                Focus = focus
            }.Schedule(state.Dependency);

            state.Dependency = new DrawHeatmapJob
            {
                Drawer = drawer,
                MapSingleton = mapSingleton,
                Focus = focus,
                Heatmap = heatmap.Map
            }.Schedule(state.Dependency);

            state.Dependency = new DrawTargetsJob
            {
                Drawer = drawer
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private struct DrawGridJob : IJob
        {
            public Drawer Drawer;
            [ReadOnly] public SpatialMapSingleton MapSingleton;
            [ReadOnly] public SpatialFocusedMap Focus;

            public void Execute()
            {
                var cam = new float3(this.MapSingleton.CameraPos.x, 0, this.MapSingleton.CameraPos.y);
                var physicalSize = (int)math.ceil(this.Focus.Size * this.Focus.CellSize);
                var extent = physicalSize;
                var half = extent * 0.5f;
                var min = cam - new float3(half, 0, half);
                var max = cam + new float3(half, 0, half);
                var color = new Color(0.4f, 0.4f, 0.4f, 0.1f);
                var step = this.Focus.CellSize;

                for (var x = 0; x <= this.Focus.Size; x++)
                {
                    var px = min.x + x * step;
                    this.Drawer.Line(new float3(px, 0, min.z), new float3(px, 0, max.z), color);
                }

                for (var z = 0; z <= this.Focus.Size; z++)
                {
                    var pz = min.z + z * step;
                    this.Drawer.Line(new float3(min.x, 0, pz), new float3(max.x, 0, pz), color);
                }

                var origin = new float3(cam.x, 0, cam.z);
                this.Drawer.Point(origin, step * 0.5f, new Color(1f, 1f, 0f, 0.8f));
            }
        }

        [BurstCompile]
        private struct DrawHeatmapJob : IJob
        {
            public Drawer Drawer;
            [ReadOnly] public SpatialMapSingleton MapSingleton;
            [ReadOnly] public SpatialFocusedMap Focus;
            [ReadOnly] public NativeParallelHashMap<int, int> Heatmap;

            public void Execute()
            {
                var physicalSize = (int)math.ceil(this.Focus.Size * this.Focus.CellSize);
                var quantizeSize = (int)math.ceil(physicalSize / this.Focus.CellSize);
                var halfSize = new float2(physicalSize) / 2f;
                var cellSize = new float3(this.Focus.CellSize, 0, this.Focus.CellSize) * 0.95f;

                foreach (var kvp in this.Heatmap)
                {
                    var hash = kvp.Key;
                    var weight = kvp.Value;

                    var x = hash % quantizeSize;
                    var y = hash / quantizeSize;

                    var cell = new int2(x, y);
                    var focusCellSize = new float2(cell.x, cell.y) * this.Focus.CellSize;
                    var wposXZ = focusCellSize - halfSize + this.MapSingleton.CameraPos + (this.Focus.CellSize * 0.5f);
                    var wpos = new float3(wposXZ.x, 0, wposXZ.y);

                    var alpha = math.clamp(weight / 5f, 0.2f, 0.9f);
                    var color = new Color(1f, 0.2f, 0.2f, alpha);

                    this.Drawer.Cuboid(wpos, quaternion.identity, cellSize, color);
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(SpatialTarget))]
        private partial struct DrawTargetsJob : IJobEntity
        {
            public Drawer Drawer;

            private void Execute(in LocalTransform transform)
            {
                this.Drawer.Point(new float3(transform.Position.x, 0, transform.Position.z), 0.1f, Color.cyan);
            }
        }
    }
}
#endif