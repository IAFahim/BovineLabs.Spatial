#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Quill;
using BovineLabs.Spatial.Data;
using BovineLabs.Timeline.Data;
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
    public partial struct SpatialTrackDebugSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var drawer = SystemAPI.GetSingleton<DrawSystem.Singleton>().CreateDrawer();
            var mapSingleton = SystemAPI.GetSingleton<SpatialMapSingleton>();
            var focus = SystemAPI.GetSingleton<SpatialFocusedMap>();
            var maskDatabase = SystemAPI.GetSingleton<SpatialMaskDatabase>();

            state.Dependency = new DrawGridJob
            {
                Drawer = drawer,
                MapSingleton = mapSingleton,
                Focus = focus
            }.Schedule(state.Dependency);

            state.Dependency = new DrawMasksJob
            {
                Drawer = drawer,
                MapSingleton = mapSingleton,
                MaskDatabase = maskDatabase
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
                var cam = new float3(MapSingleton.CameraPos.x, 0, MapSingleton.CameraPos.y);
                var extent = Focus.Size * Focus.CellSize;
                var half = extent * 0.5f;
                var min = cam - new float3(half, 0, half);
                var max = cam + new float3(half, 0, half);
                var color = new Color(0.4f, 0.4f, 0.4f, 0.1f);
                var step = Focus.CellSize;

                for (var x = 0; x <= Focus.Size; x++)
                {
                    var px = min.x + x * step;
                    Drawer.Line(new float3(px, 0, min.z), new float3(px, 0, max.z), color);
                }

                for (var z = 0; z <= Focus.Size; z++)
                {
                    var pz = min.z + z * step;
                    Drawer.Line(new float3(min.x, 0, pz), new float3(max.x, 0, pz), color);
                }

                var origin = new float3(cam.x, 0, cam.z);
                Drawer.Point(origin, step * 0.5f, new Color(1f, 1f, 0f, 0.8f));
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct DrawMasksJob : IJobEntity
        {
            public Drawer Drawer;
            [ReadOnly] public SpatialMapSingleton MapSingleton;
            [ReadOnly] public SpatialMaskDatabase MaskDatabase;

            private void Execute(in SpatialActiveClipData clipData, in LocalTransform transform)
            {
                if (!MaskDatabase.Blob.Value.Has(clipData.MaskKey)) return;
                
                var centerCell = MapSingleton.Map.Quantized(transform.Position.xz - MapSingleton.CameraPos);
                ref var mask = ref MaskDatabase.Blob.Value.Masks[clipData.MaskKey];
                
                var cellSize = new float3(MapSingleton.CellSize, 0, MapSingleton.CellSize) * 0.95f;

                for (var y = 0; y < mask.Size; y++)
                {
                    for (var x = 0; x < mask.Size; x++)
                    {
                        if (mask.Get(x, y) <= 0) continue;

                        var cell = centerCell + new int2(x - mask.Size / 2, mask.Size / 2 - y);
                        var wposXZ = (float2)cell * MapSingleton.CellSize + MapSingleton.CameraPos + MapSingleton.CellSize * 0.5f;
                        var wpos = new float3(wposXZ.x, 0, wposXZ.y);

                        Drawer.Cuboid(wpos, quaternion.identity, cellSize, new Color(0.8f, 0.2f, 0.2f, 0.3f));
                    }
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
                Drawer.Point(new float3(transform.Position.x, 0, transform.Position.z), 0.1f, Color.cyan);
            }
        }
    }
}
#endif