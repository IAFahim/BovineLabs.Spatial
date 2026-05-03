// BovineLabs.Spatial/SpatialTrackCollectSystem.cs

using BovineLabs.Core.Extensions;

namespace BovineLabs.Spatial
{
    using System;
    using BovineLabs.Core.Collections;
    using BovineLabs.Core.Spatial;
    using BovineLabs.Core.Iterators;
    using BovineLabs.Essence;
    using BovineLabs.Essence.Data;
    using BovineLabs.Reaction.Conditions;
    using BovineLabs.Reaction.Data.Conditions;
    using BovineLabs.Reaction.Data.Core;
    using BovineLabs.Spatial.Data;
    using BovineLabs.Timeline;
    using BovineLabs.Timeline.Data;
    using BovineLabs.Timeline.EntityLinks;
    using BovineLabs.Timeline.EntityLinks.Data;
    using Unity.Burst;
    using Unity.Burst.CompilerServices;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Transforms;

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(SpatialMapBuildSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct SpatialTrackCollectSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> intrinsicChanges;
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeList<Entity> uniqueKeys;
        private NativeList<Entity> uniqueEventKeys;

        private ComponentLookup<Targets> targetsLookup;
        private ComponentLookup<TargetsCustom> customsLookup;
        private UnsafeComponentLookup<EntityLinkSource> sourcesLookup;
        private UnsafeBufferLookup<EntityLinkEntry> linksLookup;
        private ComponentLookup<LocalTransform> transformLookup;
        
        private IntrinsicWriter.Lookup intrinsicWriters;
        private ConditionEventWriter.Lookup eventWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.intrinsicChanges = new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            this.eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            this.uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            this.uniqueEventKeys = new NativeList<Entity>(64, Allocator.Persistent);

            this.targetsLookup = state.GetComponentLookup<Targets>(true);
            this.customsLookup = state.GetComponentLookup<TargetsCustom>(true);
            this.sourcesLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            this.linksLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            this.transformLookup = state.GetComponentLookup<LocalTransform>(true);

            this.intrinsicWriters.Create(ref state);
            this.eventWriters.Create(ref state);

            state.RequireForUpdate<SpatialMaskDatabase>();
            state.RequireForUpdate<SpatialTrackingActive>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            this.intrinsicChanges.Dispose();
            this.eventChanges.Dispose();
            this.uniqueKeys.Dispose();
            this.uniqueEventKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var buildSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialMapBuildSystem>();
            if (!state.EntityManager.HasComponent<SpatialMapSingleton>(buildSystemHandle)) return;

            var mapSingleton = state.EntityManager.GetComponentData<SpatialMapSingleton>(buildSystemHandle);
            if (!mapSingleton.Map.Map.IsCreated) return;
            
            this.targetsLookup.Update(ref state);
            this.customsLookup.Update(ref state);
            this.sourcesLookup.Update(ref state);
            this.linksLookup.Update(ref state);
            this.transformLookup.Update(ref state);
            
            this.intrinsicWriters.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            this.eventWriters.Update(ref state);

            var maskDatabase = SystemAPI.GetSingleton<SpatialMaskDatabase>();

            ref var buildSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<SpatialMapBuildSystem>(buildSystemHandle);
            var entitiesArray = buildSystem.Entities.AsArray();

            var evalJob = new EvaluateJob
            {
                MapSingleton = mapSingleton,
                MapEntities = entitiesArray,
                MaskDatabase = maskDatabase,
                TargetsLookup = this.targetsLookup,
                CustomsLookup = this.customsLookup,
                SourcesLookup = this.sourcesLookup,
                LinksLookup = this.linksLookup,
                TransformLookup = this.transformLookup,
                IntrinsicChanges = this.intrinsicChanges.AsWriter(),
                EventChanges = this.eventChanges.AsWriter(),
            };
            
            var exitJob = new ExitJob
            {
                IntrinsicChanges = this.intrinsicChanges.AsWriter(),
                EventChanges = this.eventChanges.AsWriter(),
            };

            var evalDep = evalJob.ScheduleParallel(state.Dependency);
            var exitDep = exitJob.ScheduleParallel(evalDep);

            var intrinsicApplyDep = this.intrinsicChanges.Apply(exitDep, out var intrinsicReader);
            var eventApplyDep = this.eventChanges.Apply(exitDep, out var eventReader);

            var uniqueIntrinsicDep = new GetUniqueKeysJob<IntrinsicAmount> { Map = intrinsicReader, Keys = this.uniqueKeys }.Schedule(intrinsicApplyDep);
            var uniqueEventDep = new GetUniqueKeysJob<EventAmount> { Map = eventReader, Keys = this.uniqueEventKeys }.Schedule(eventApplyDep);

            var applyIntrinsicDep = new ApplyIntrinsicJob
            {
                Keys = this.uniqueKeys.AsDeferredJobArray(),
                GroupChanges = intrinsicReader,
                IntrinsicWriters = this.intrinsicWriters
            }.Schedule(this.uniqueKeys, 64, uniqueIntrinsicDep);

            var applyEventDep = new ApplyEventJob
            {
                Keys = this.uniqueEventKeys.AsDeferredJobArray(),
                GroupChanges = eventReader,
                EventWriters = this.eventWriters
            }.Schedule(this.uniqueEventKeys, 64, JobHandle.CombineDependencies(uniqueEventDep, applyIntrinsicDep));

            var clearIntrinsicDep = this.intrinsicChanges.Clear(applyIntrinsicDep);
            var clearEventDep = this.eventChanges.Clear(applyEventDep);

            state.Dependency = JobHandle.CombineDependencies(clearIntrinsicDep, clearEventDep);
        }

        [BurstCompile]
        private struct GetUniqueKeysJob<T> : IJob
            where T : unmanaged
        {
            public NativeParallelMultiHashMap<Entity, T>.ReadOnly Map;
            public NativeList<Entity> Keys;

            public void Execute()
            {
                BovineLabs.Core.Extensions.NativeParallelMultiHashMapExtensions.GetUniqueKeyArray(this.Map, this.Keys);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateJob : IJobEntity
        {
            [ReadOnly] public SpatialMapSingleton MapSingleton;
            [ReadOnly] public NativeArray<Entity> MapEntities;
            [ReadOnly] public SpatialMaskDatabase MaskDatabase;
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> SourcesLookup;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> LinksLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;

            private void Execute(Entity clipEntity, in TrackBinding binding, in SpatialActiveClipData clipData, ref DynamicBuffer<SpatialActiveTarget> previous)
            {
                if (binding.Value == Entity.Null || !this.MaskDatabase.Blob.Value.Has(clipData.MaskKey)) return;
                if (!this.TransformLookup.TryGetComponent(binding.Value, out var casterTransform)) return;

                var currentHits = new NativeList<Entity>(Allocator.Temp);
                
                var centerCell = this.MapSingleton.Map.Quantized(casterTransform.Position.xz - this.MapSingleton.CameraPos);
                ref var mask = ref this.MaskDatabase.Blob.Value.Masks[clipData.MaskKey];

                for (var y = 0; y < mask.Size; y++)
                {
                    for (var x = 0; x < mask.Size; x++)
                    {
                        if (mask.Get(x, y) <= 0) continue;

                        var cell = centerCell + new int2(x - mask.Size / 2, mask.Size / 2 - y);
                        var hash = this.MapSingleton.Map.Hash(cell); 

                        if (!this.MapSingleton.Map.Map.TryGetFirstValue(hash, out var item, out var it)) continue;

                        do
                        {
                            var target = this.MapEntities[item];
                            var targets = this.TargetsLookup.HasComponent(binding.Value) ? this.TargetsLookup[binding.Value] : default;

                            if (TryResolveTarget(clipData.RouteTo, clipData.RouteLinkKey, binding.Value, target, targets, this.CustomsLookup, this.SourcesLookup, this.LinksLookup, out var resolved))
                            {
                                currentHits.Add(resolved);
                            }

                        } while (this.MapSingleton.Map.Map.TryGetNextValue(out item, ref it));
                    }
                }

                currentHits.Sort();
                var uniqueCount = CompactUnique(currentHits);

                var i = 0;
                var j = 0;
                
                while (i < previous.Length && j < uniqueCount)
                {
                    var p = previous[i].Target;
                    var c = currentHits[j];
                    
                    if (p == c)
                    {
                        i++; j++;
                    }
                    else if (p.Index < c.Index || (p.Index == c.Index && p.Version < c.Version))
                    {
                        this.FireExit(p, clipData);
                        i++;
                    }
                    else
                    {
                        this.FireEnter(c, clipData);
                        j++;
                    }
                }

                while (i < previous.Length) { this.FireExit(previous[i].Target, clipData); i++; }
                while (j < uniqueCount) { this.FireEnter(currentHits[j], clipData); j++; }

                previous.Clear();
                for (var k = 0; k < uniqueCount; k++) previous.Add(new SpatialActiveTarget { Target = currentHits[k] });
            }

            private static int CompactUnique(NativeList<Entity> entities)
            {
                if (entities.Length == 0) return 0;
                var write = 1;
                var previous = entities[0];
                for (var read = 1; read < entities.Length; read++)
                {
                    var current = entities[read];
                    if (current == previous) continue;
                    entities[write++] = current;
                    previous = current;
                }
                return write;
            }

            private void FireEnter(Entity e, in SpatialActiveClipData data)
            {
                if (data.IntrinsicStore.Value != 0) this.IntrinsicChanges.Add(e, new IntrinsicAmount(data.IntrinsicStore, 1));
                if (data.OnEnter != ConditionKey.Null) this.EventChanges.Add(e, new EventAmount(data.OnEnter, 1));
            }

            private void FireExit(Entity e, in SpatialActiveClipData data)
            {
                if (data.IntrinsicStore.Value != 0) this.IntrinsicChanges.Add(e, new IntrinsicAmount(data.IntrinsicStore, -1));
                if (data.OnExit != ConditionKey.Null) this.EventChanges.Add(e, new EventAmount(data.OnExit, 1));
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActivePrevious))]
        [WithDisabled(typeof(ClipActive))]
        private partial struct ExitJob : IJobEntity
        {
            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;

            private void Execute(in SpatialActiveClipData clipData, ref DynamicBuffer<SpatialActiveTarget> previous)
            {
                for (var i = 0; i < previous.Length; i++)
                {
                    if (clipData.IntrinsicStore.Value != 0)
                        this.IntrinsicChanges.Add(previous[i].Target, new IntrinsicAmount(clipData.IntrinsicStore, -1));
                    
                    if (clipData.OnExit != ConditionKey.Null)
                        this.EventChanges.Add(previous[i].Target, new EventAmount(clipData.OnExit, 1));
                }
                previous.Clear();
            }
        }

        [BurstCompile]
        private struct ApplyIntrinsicJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            public void Execute(int index)
            {
                var key = this.Keys[index];
                if (Hint.Unlikely(!this.IntrinsicWriters.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<IntrinsicAmount>();

                if (this.GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);
                    while (this.GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref writer);
                }

                foreach (var i in values) writer.Add(i.Intrinsic, i.Amount);
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<IntrinsicAmount> values, IntrinsicAmount value, ref IntrinsicWriter writer)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i].Intrinsic.Equals(value.Intrinsic))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
                    }
                }

                if (values.Length < values.Capacity) { values.Add(value); return; }
                writer.Add(value.Intrinsic, value.Amount);
            }
        }

        [BurstCompile]
        private struct ApplyEventJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup EventWriters;

            public void Execute(int index)
            {
                var key = this.Keys[index];
                if (Hint.Unlikely(!this.EventWriters.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<EventAmount>();

                if (this.GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);
                    while (this.GroupChanges.TryGetNextValue(out value, ref it))
                        AddOrAccumulate(ref values, value, ref writer);
                }

                foreach (var e in values) writer.Trigger(e.Event, e.Amount);
            }

            private static void AddOrAccumulate(ref FixedList4096Bytes<EventAmount> values, EventAmount value, ref ConditionEventWriter writer)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i].Event.Equals(value.Event))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
                    }
                }

                if (values.Length < values.Capacity) { values.Add(value); return; }
                writer.Trigger(value.Event, value.Amount);
            }
        }

        private struct IntrinsicAmount : IEquatable<IntrinsicAmount>
        {
            public readonly IntrinsicKey Intrinsic;
            public int Amount;
            public IntrinsicAmount(IntrinsicKey intrinsic, int amount) { this.Intrinsic = intrinsic; this.Amount = amount; }
            public bool Equals(IntrinsicAmount other) => this.Intrinsic.Equals(other.Intrinsic);
            public override int GetHashCode() => this.Intrinsic.GetHashCode();
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;
            public EventAmount(ConditionKey evt, int amount) { this.Event = evt; this.Amount = amount; }
            public bool Equals(EventAmount other) => this.Event.Equals(other.Event);
            public override int GetHashCode() => this.Event.GetHashCode();
        }

        private static bool TryResolveTarget(Target targetMode, ushort linkKey, Entity self, Entity other, in Targets targets, in ComponentLookup<TargetsCustom> customLookup, in UnsafeComponentLookup<EntityLinkSource> sources, in UnsafeBufferLookup<EntityLinkEntry> links, out Entity resolved)
        {
            resolved = Entity.Null;

            var target = targetMode switch
            {
                Target.Self => self,
                Target.Target => other,
                Target.Owner => targets.Owner,
                Target.Source => targets.Source,
                Target.Custom0 => customLookup.TryGetComponent(self, out var custom) ? custom.Target0 : Entity.Null,
                Target.Custom1 => customLookup.TryGetComponent(self, out var custom) ? custom.Target1 : Entity.Null,
                _ => Entity.Null
            };

            if (target == Entity.Null) return false;

            if (linkKey == 0)
            {
                resolved = target;
                return true;
            }

            if (EntityLinkResolver.TryResolve(target, linkKey, sources, links, out var linked))
            {
                resolved = linked;
                return true;
            }

            resolved = target;
            return true;
        }
    }
}