using BovineLabs.Quill;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Quill.Grid.Debug
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GridVisualizerInputSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridVisualizerInput>();
            state.RequireForUpdate<GridCenterTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float time = (float)SystemAPI.Time.ElapsedTime;

            foreach (var (input, transform) in SystemAPI.Query<RefRW<GridVisualizerInput>, RefRO<LocalTransform>>()
                         .WithAll<GridCenterTag>())
            {
                TryCalculateDummyHover(time, transform.ValueRO.Position, out float3 hoverPos);
                input.ValueRW.HoverWorldPosition = hoverPos;
                input.ValueRW.IsHovering = true;
            }
        }

        public static bool TryCalculateDummyHover(float time, float3 origin, out float3 hoverPos)
        {
            float radius = 4f;
            hoverPos = origin + new float3(math.cos(time) * radius, 0f, math.sin(time) * radius);
            return true;
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct GridVisualizerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridVisualizerConfig>();
            state.RequireForUpdate<DrawSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var drawSingleton = SystemAPI.GetSingleton<DrawSystem.Singleton>();
            Drawer drawer = drawSingleton.CreateDrawer();
            if (!drawer.IsEnabled) return;

            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (configRO, inputRO, buffer, transform) in SystemAPI
                         .Query<RefRO<GridVisualizerConfig>, RefRO<GridVisualizerInput>,
                             DynamicBuffer<GridCellVisualState>, RefRO<LocalTransform>>().WithAll<GridCenterTag>())
            {
                GridVisualizerConfig config = configRO.ValueRO;
                GridVisualizerInput input = inputRO.ValueRO;

                TryEnsureBufferSize(buffer, config.Size, config.BaseColor);

                var job = new GridDrawJob
                {
                    Config = config,
                    Input = input,
                    Origin = transform.ValueRO.Position,
                    Dt = dt,
                    Drawer = drawer,
                    States = buffer.AsNativeArray()
                };

                state.Dependency = job.Schedule(buffer.Length, 64, state.Dependency);
            }
        }

        public static bool TryEnsureBufferSize(DynamicBuffer<GridCellVisualState> buffer, int2 size,
            float4 baseColor)
        {
            int requiredLength = size.x * size.y;
            if (buffer.Length == requiredLength) return true;

            buffer.ResizeUninitialized(requiredLength);
            for (int i = 0; i < requiredLength; i++)
            {
                buffer[i] = new GridCellVisualState
                {
                    TargetDepth = 0f,
                    CurrentDepth = 0f,
                    TargetColor = baseColor,
                    CurrentColor = baseColor
                };
            }

            return true;
        }
    }

    [BurstCompile]
    public struct GridDrawJob : IJobParallelFor
    {
        [ReadOnly] public GridVisualizerConfig Config;
        [ReadOnly] public GridVisualizerInput Input;
        [ReadOnly] public float3 Origin;
        [ReadOnly] public float Dt;
        
        public Drawer Drawer;
        public NativeArray<GridCellVisualState> States;

        public void Execute(int index)
        {
            int2 xy = GridVisualizerMath.ToCell(index, Config.Size.x);
            GridMathPrimitives.TryGetLocalPosition(xy, Config.Size, Config.BlockSize, Config.Spacing, out float3 localPos);
            float3 worldPos = Origin + localPos;

            if (index == 0)
            {
                float2 totalSize = new float2(
                    Config.Size.x * (Config.BlockSize.x + Config.Spacing),
                    Config.Size.y * (Config.BlockSize.z + Config.Spacing)
                );
                
                float3 scanCenter = Origin + new float3(0f, Config.ScanPlaneYOffset, 0f);
                QuillVisualHelpers.TryDrawScanPlaneGlobal(ref Drawer, scanCenter, totalSize, Config.PlaneColor.ToColor());

                if (Input.IsHovering)
                {
                    float3 cursorTarget = Input.HoverWorldPosition;
                    cursorTarget.y = Origin.y + Config.ScanPlaneYOffset;
                    QuillVisualHelpers.TryDrawCursor(ref Drawer, cursorTarget, Color.white);

                    GridMathPrimitives.TryGetUV(GridVisualizerMath.ToCell(GetClosestIndex(Input.HoverWorldPosition), Config.Size.x), Config.Size, out float2 hoverUV);
                    quaternion flatRotation = quaternion.Euler(math.PI / 2f, 0f, 0f);
                    QuillVisualHelpers.TryDrawUVText(ref Drawer, cursorTarget + new float3(1f, 0f, 1f), flatRotation, hoverUV, Color.white, Config.TextSize * 1.5f);
                }
            }

            GridCellVisualState state = States[index];

            float dist = math.select(float.MaxValue, math.distance(worldPos.xz, Input.HoverWorldPosition.xz), Input.IsHovering);
            float tHover = math.smoothstep(0f, 1f, math.saturate(1f - (dist / math.max(0.001f, Config.HoverRadius))));
            
            float targetDepth = tHover * Config.HoverDepth;
            float4 targetColor = math.lerp(Config.BaseColor, math.lerp(Config.HoverColorOuter, Config.HoverColorCore, tHover), tHover);

            GridVisualPrimitives.TryStepState(in state, targetDepth, targetColor, Dt, Config.TransitionSpeed, out GridCellVisualState nextState);
            States[index] = nextState;

            QuillVisualHelpers.TryDrawTechPillar(
                ref Drawer, 
                worldPos, 
                Config.BlockSize, 
                Config.BaseHeight, 
                nextState.CurrentDepth, 
                nextState.CurrentColor.ToColor(), 
                Config.OutlineColor.ToColor()
            );

            if (Config.RevealEnabled)
            {
                float3 scanPos = worldPos + new float3(0f, Config.ScanPlaneYOffset, 0f);
                QuillVisualHelpers.TryDrawScanLineCell(ref Drawer, scanPos, Config.BlockSize, Config.PlaneColor.ToColor());

                if (nextState.CurrentDepth > 0.05f)
                {
                    float fakeData = math.frac(math.sin(math.dot(new float2(xy.x, xy.y), new float2(12.9898f, 78.233f))) * 43758.5453f);
                    Color tileCol = math.lerp(Config.HeatmapCold, Config.HeatmapHot, fakeData).ToColor();

                    tileCol.a *= math.saturate(nextState.CurrentDepth / Config.HoverDepth);
                    
                    float3 heatmapPos = worldPos + new float3(0f, Config.HeatmapYOffset, 0f);
                    QuillVisualHelpers.TryDrawHeatmapCell(ref Drawer, heatmapPos, Config.BlockSize, fakeData, tileCol, Color.white, Config.TextSize);
                }
            }
        }

        private int GetClosestIndex(float3 targetPos)
        {
            float minDist = float.MaxValue;
            int bestIdx = 0;
            for (int i = 0; i < Config.Size.x * Config.Size.y; i++)
            {
                int2 xy = GridVisualizerMath.ToCell(i, Config.Size.x);
                GridMathPrimitives.TryGetLocalPosition(xy, Config.Size, Config.BlockSize, Config.Spacing, out float3 localPos);
                float dist = math.distancesq(Origin + localPos, targetPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }
    }
}