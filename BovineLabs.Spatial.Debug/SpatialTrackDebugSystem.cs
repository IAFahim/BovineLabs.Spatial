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

            var vis = SystemAPI.HasSingleton<SpatialDebugVisualization>()
                ? SystemAPI.GetSingleton<SpatialDebugVisualization>()
                : new SpatialDebugVisualization
                {
                    Mode = VisualizationMode.CubeHeight, Palette = ColorPalette.CoolWarm, HeightScale = 0.9f,
                    Opacity = 0.85f, ShowGrid = 1, ShowNegativeBelow = 1
                };

            if (vis.ShowGrid != 0)
            {
                state.Dependency =
                    new DrawGridJob { Drawer = drawer, MapSingleton = mapSingleton, Focus = focus }.Schedule(
                        state.Dependency);
            }

            state.Dependency = new DrawHeatmapJob
            {
                Drawer = drawer,
                MapSingleton = mapSingleton,
                Focus = focus,
                Heatmap = heatmap.Map,
                Vis = vis
            }.Schedule(state.Dependency);

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
            public SpatialDebugVisualization Vis;

            public void Execute()
            {
                if (Heatmap.IsEmpty) return;

                var physicalSize = (int)math.ceil(Focus.Size * Focus.CellSize);
                var quantizeSize = (int)math.ceil(physicalSize / Focus.CellSize);
                var halfSize = new float2(physicalSize) / 2f;

                int maxAbs = 1;
                foreach (var kvp in Heatmap) maxAbs = math.max(maxAbs, math.abs(kvp.Value));

                foreach (var kvp in Heatmap)
                {
                    var weight = kvp.Value;
                    if (weight == 0) continue;

                    var x = kvp.Key % quantizeSize;
                    var y = kvp.Key / quantizeSize;
                    var wposXZ = (float2)new int2(x, y) * Focus.CellSize - halfSize + MapSingleton.CameraPos +
                                 Focus.CellSize * 0.5f;

                    float t = math.clamp((float)weight / maxAbs, -1f, 1f);
                    float absT = math.abs(t);

                    // choose size based on mode
                    float3 size;
                    float yPos = 0f;
                    switch (Vis.Mode)
                    {
                        case VisualizationMode.FlatPlate:
                            size = new float3(Focus.CellSize * 0.95f, Vis.PlateThickness, Focus.CellSize * 0.95f);
                            yPos = 0;
                            break;
                        case VisualizationMode.Pillar:
                            size = new float3(Focus.CellSize * 0.25f, absT * Focus.CellSize * Vis.HeightScale,
                                Focus.CellSize * 0.25f);
                            yPos = Vis.ShowNegativeBelow != 0 && weight < 0 ? -size.y * 0.5f : size.y * 0.5f;
                            break;
                        case VisualizationMode.AxisTint:
                            size = new float3(Focus.CellSize * 0.98f, 0.002f, Focus.CellSize * 0.98f);
                            yPos = 0.001f;
                            break;
                        case VisualizationMode.WireCube:
                        default: // CubeHeight
                            var h = absT * Focus.CellSize * Vis.HeightScale;
                            size = new float3(Focus.CellSize * 0.85f, math.max(0.02f, h), Focus.CellSize * 0.85f);
                            yPos = Vis.ShowNegativeBelow != 0 && weight < 0 ? -size.y * 0.5f : size.y * 0.5f;
                            break;
                    }

                    var color = GetPalette(Vis.Palette, t);
                    color.a = Vis.Opacity * (Vis.Mode == VisualizationMode.WireCube ? 0.3f : 1f);

                    Drawer.Cuboid(new float3(wposXZ.x, yPos, wposXZ.y), quaternion.identity, size, color);
                }
            }

            private static Color GetPalette(ColorPalette p, float t)
            {
                float u = math.saturate((t + 1f) * 0.5f); // 0..1
                return p switch
                {
                    ColorPalette.CoolWarm => new Color(math.lerp(0.23f, 0.71f, u),
                        math.lerp(0.30f, 0.02f, math.abs(u - 0.5f) * 2), math.lerp(0.75f, 0.15f, 1 - u)),
                    ColorPalette.Viridis => new Color(0.27f + 0.72f * u, 0.0f + 0.9f * math.pow(u, 0.5f),
                        0.33f + 0.67f * u * u),
                    ColorPalette.Inferno => new Color(math.pow(u, 0.7f), math.pow(u, 1.5f) * 0.5f, u * u * 0.2f),
                    ColorPalette.Jet => new Color(math.clamp(1.5f - math.abs(4 * u - 3), 0, 1),
                        math.clamp(1.5f - math.abs(4 * u - 2), 0, 1), math.clamp(1.5f - math.abs(4 * u - 1), 0, 1)),
                    ColorPalette.Turbo => new Color(0.19f + 0.81f * u, 0.3f + 0.7f * math.sin(u * 3.14f),
                        0.9f - 0.8f * u),
                    _ => new Color(u, u, u)
                };
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(SpatialTarget))]
    public partial struct DrawTargetsJob : IJobEntity
    {
        public Drawer Drawer;

        private void Execute(in LocalToWorld transform)
        {
            this.Drawer.Point(new float3(transform.Position.x, 0, transform.Position.z), 0.1f, Color.cyan);
        }
    }
}

#endif