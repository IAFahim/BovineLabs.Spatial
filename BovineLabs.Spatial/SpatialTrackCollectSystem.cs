using System;
using BovineLabs.Core.Collections;
using BovineLabs.Core.Extensions;
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

namespace BovineLabs.Spatial
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(SpatialMapBuildSystem))]
    public partial struct SpatialTrackCollectSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> intrinsicChanges;
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> eventChanges;
        private NativeParallelHashSet<Entity> intrinsicTargets;
        private NativeParallelHashSet<Entity> eventTargets;
        private NativeList<Entity> uniqueKeys;
        private NativeList<Entity> uniqueEventKeys;

        private ComponentLookup<Targets> targetsLookup;
        private ComponentLookup<TargetsCustom> customsLookup;
        private UnsafeComponentLookup<EntityLinkSource> sourcesLookup;
        private UnsafeBufferLookup<EntityLinkEntry> linksLookup;
        private IntrinsicWriter.Lookup intrinsicWriters;
        private ConditionEventWriter.Lookup eventWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            intrinsicChanges = new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            intrinsicTargets = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            eventTargets = new NativeParallelHashSet<Entity>(64, Allocator.Persistent);
            uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            uniqueEventKeys = new NativeList<Entity>(64, Allocator.Persistent);

            targetsLookup = state.GetComponentLookup<Targets>(true);
            customsLookup = state.GetComponentLookup<TargetsCustom>(true);
            sourcesLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            linksLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);

            intrinsicWriters.Create(ref state);
            eventWriters.Create(ref state);

            state.RequireForUpdate<SpatialMapSingleton>();
            state.RequireForUpdate<SpatialMaskDatabase>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            intrinsicChanges.Dispose();
            eventChanges.Dispose();
            intrinsicTargets.Dispose();
            eventTargets.Dispose();
            uniqueKeys.Dispose();
            uniqueEventKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            targetsLookup.Update(ref state);
            customsLookup.Update(ref state);
            sourcesLookup.Update(ref state);
            linksLookup.Update(ref state);
            intrinsicWriters.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            eventWriters.Update(ref state);

            intrinsicTargets.Clear();
            eventTargets.Clear();

            var mapSingleton = SystemAPI.GetSingleton<SpatialMapSingleton>();
            var maskDatabase = SystemAPI.GetSingleton<SpatialMaskDatabase>();

            var evalJob = new EvaluateJob
            {
                MapSingleton = mapSingleton,
                MaskDatabase = maskDatabase,
                TargetsLookup = targetsLookup,
                CustomsLookup = customsLookup,
                SourcesLookup = sourcesLookup,
                LinksLookup = linksLookup,
                IntrinsicChanges = intrinsicChanges.AsWriter(),
                EventChanges = eventChanges.AsWriter(),
                IntrinsicTargets = intrinsicTargets.AsParallelWriter(),
                EventTargets = eventTargets.AsParallelWriter()
            };
            
            var exitJob = new ExitJob
            {
                IntrinsicChanges = intrinsicChanges.AsWriter(),
                EventChanges = eventChanges.AsWriter(),
                IntrinsicTargets = intrinsicTargets.AsParallelWriter(),
                EventTargets = eventTargets.AsParallelWriter()
            };

            state.Dependency = JobHandle.CombineDependencies(
                evalJob.ScheduleParallel(state.Dependency),
                exitJob.ScheduleParallel(state.Dependency));

            state.Dependency = intrinsicChanges.Apply(state.Dependency, out var intrinsicReader);
            state.Dependency = eventChanges.Apply(state.Dependency, out var eventReader);

            state.Dependency = new GetKeysJob { UniqueKeys = uniqueKeys, UniqueKeySet = intrinsicTargets }.Schedule(state.Dependency);
            state.Dependency = new GetKeysJob { UniqueKeys = uniqueEventKeys, UniqueKeySet = eventTargets }.Schedule(state.Dependency);

            state.Dependency = new ApplyIntrinsicJob
            {
                Keys = uniqueKeys,
                GroupChanges = intrinsicReader,
                IntrinsicWriters = intrinsicWriters
            }.Schedule(uniqueKeys, 64, state.Dependency);

            state.Dependency = new ApplyEventJob
            {
                Keys = uniqueEventKeys,
                GroupChanges = eventReader,
                EventWriters = eventWriters
            }.Schedule(uniqueEventKeys, 64, state.Dependency);

            state.Dependency = intrinsicChanges.Clear(state.Dependency);
            state.Dependency = eventChanges.Clear(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct EvaluateJob : IJobEntity
        {
            [ReadOnly] public SpatialMapSingleton MapSingleton;
            [ReadOnly] public SpatialMaskDatabase MaskDatabase;
            [ReadOnly] public ComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public ComponentLookup<TargetsCustom> CustomsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> SourcesLookup;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> LinksLookup;

            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter IntrinsicTargets;
            public NativeParallelHashSet<Entity>.ParallelWriter EventTargets;

            private void Execute(Entity clipEntity, in TrackBinding binding, in SpatialActiveClipData clipData, ref DynamicBuffer<SpatialActiveTarget> previous, in LocalTransform casterTransform)
            {
                if (binding.Value == Entity.Null || !MaskDatabase.Blob.Value.Has(clipData.MaskKey)) return;

                var currentHits = new NativeList<Entity>(Allocator.Temp);
                var centerCell = MapSingleton.Map.Quantized(casterTransform.Position.xz - MapSingleton.CameraPos);
                ref var mask = ref MaskDatabase.Blob.Value.Masks[clipData.MaskKey];

                for (var y = 0; y < mask.Size; y++)
                {
                    for (var x = 0; x < mask.Size; x++)
                    {
                        if (mask.Get(x, y) <= 0) continue;

                        var cell = centerCell + new int2(x - mask.Size / 2, mask.Size / 2 - y);
                        var hash = MapSingleton.Map.Hash(cell);

                        if (!MapSingleton.Map.Map.TryGetFirstValue(hash, out var item, out var it)) continue;

                        do
                        {
                            var target = MapSingleton.Entities[item];
                            var targets = TargetsLookup.HasComponent(binding.Value) ? TargetsLookup[binding.Value] : default;

                            if (TryResolveTarget(clipData.RouteTo, clipData.RouteLinkKey, binding.Value, target, targets, CustomsLookup, SourcesLookup, LinksLookup, out var resolved))
                            {
                                currentHits.Add(resolved);
                            }

                        } while (MapSingleton.Map.Map.TryGetNextValue(out item, ref it));
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
                        FireExit(p, clipData);
                        i++;
                    }
                    else
                    {
                        FireEnter(c, clipData);
                        j++;
                    }
                }

                while (i < previous.Length) { FireExit(previous[i].Target, clipData); i++; }
                while (j < uniqueCount) { FireEnter(currentHits[j], clipData); j++; }

                previous.Clear();
                for (var k = 0; k < uniqueCount; k++) previous.Add(new SpatialActiveTarget { Target = currentHits[k] });
            }

            private static int CompactUnique(NativeList<Entity> entities)
            {
                if (entities.Length == 0)
                    return 0;

                var write = 1;
                var previous = entities[0];

                for (var read = 1; read < entities.Length; read++)
                {
                    var current = entities[read];
                    if (current == previous)
                        continue;

                    entities[write++] = current;
                    previous = current;
                }

                return write;
            }

            private void FireEnter(Entity e, in SpatialActiveClipData data)
            {
                if (data.IntrinsicStore.Value != 0)
                {
                    IntrinsicChanges.Add(e, new IntrinsicAmount(data.IntrinsicStore, 1));
                    IntrinsicTargets.Add(e);
                }
                if (data.OnEnter != ConditionKey.Null)
                {
                    EventChanges.Add(e, new EventAmount(data.OnEnter, 1));
                    EventTargets.Add(e);
                }
            }

            private void FireExit(Entity e, in SpatialActiveClipData data)
            {
                if (data.IntrinsicStore.Value != 0)
                {
                    IntrinsicChanges.Add(e, new IntrinsicAmount(data.IntrinsicStore, -1));
                    IntrinsicTargets.Add(e);
                }
                if (data.OnExit != ConditionKey.Null)
                {
                    EventChanges.Add(e, new EventAmount(data.OnExit, 1));
                    EventTargets.Add(e);
                }
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActivePrevious))]
        [WithDisabled(typeof(ClipActive))]
        private partial struct ExitJob : IJobEntity
        {
            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;
            public NativeParallelHashSet<Entity>.ParallelWriter IntrinsicTargets;
            public NativeParallelHashSet<Entity>.ParallelWriter EventTargets;

            private void Execute(in SpatialActiveClipData clipData, ref DynamicBuffer<SpatialActiveTarget> previous)
            {
                for (var i = 0; i < previous.Length; i++)
                {
                    if (clipData.IntrinsicStore.Value != 0)
                    {
                        IntrinsicChanges.Add(previous[i].Target, new IntrinsicAmount(clipData.IntrinsicStore, -1));
                        IntrinsicTargets.Add(previous[i].Target);
                    }
                    if (clipData.OnExit != ConditionKey.Null)
                    {
                        EventChanges.Add(previous[i].Target, new EventAmount(clipData.OnExit, 1));
                        EventTargets.Add(previous[i].Target);
                    }
                }
                previous.Clear();
            }
        }

        [BurstCompile]
        private struct GetKeysJob : IJob
        {
            public NativeList<Entity> UniqueKeys;
            [ReadOnly] public NativeParallelHashSet<Entity> UniqueKeySet;

            public void Execute()
            {
                UniqueKeys.Clear();
                foreach (var key in UniqueKeySet) UniqueKeys.Add(key);
            }
        }

        [BurstCompile]
        private struct ApplyIntrinsicJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, IntrinsicAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public IntrinsicWriter.Lookup IntrinsicWriters;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!IntrinsicWriters.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<IntrinsicAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);
                    while (GroupChanges.TryGetNextValue(out value, ref it))
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

                if (values.Length < values.Capacity)
                {
                    values.Add(value);
                    return;
                }

                writer.Add(value.Intrinsic, value.Amount);
            }
        }

        [BurstCompile]
        private struct ApplyEventJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeList<Entity> Keys;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, EventAmount>.ReadOnly GroupChanges;
            [NativeDisableParallelForRestriction] public ConditionEventWriter.Lookup EventWriters;

            public void Execute(int index)
            {
                var key = Keys[index];
                if (Hint.Unlikely(!EventWriters.TryGet(key, out var writer))) return;

                var values = new FixedList4096Bytes<EventAmount>();

                if (GroupChanges.TryGetFirstValue(key, out var value, out var it))
                {
                    AddOrAccumulate(ref values, value, ref writer);
                    while (GroupChanges.TryGetNextValue(out value, ref it))
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

                if (values.Length < values.Capacity)
                {
                    values.Add(value);
                    return;
                }

                writer.Trigger(value.Event, value.Amount);
            }
        }

        private struct IntrinsicAmount : IEquatable<IntrinsicAmount>
        {
            public readonly IntrinsicKey Intrinsic;
            public int Amount;
            public IntrinsicAmount(IntrinsicKey intrinsic, int amount) { Intrinsic = intrinsic; Amount = amount; }
            public bool Equals(IntrinsicAmount other) => Intrinsic.Equals(other.Intrinsic);
            public override int GetHashCode() => Intrinsic.GetHashCode();
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;
            public EventAmount(ConditionKey evt, int amount) { Event = evt; Amount = amount; }
            public bool Equals(EventAmount other) => Event.Equals(other.Event);
            public override int GetHashCode() => Event.GetHashCode();
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