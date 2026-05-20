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
    [UpdateAfter(typeof(EntityLinkTargetPatchSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct SpatialTrackCollectSystem : ISystem
    {
        private NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount> _intrinsicChanges;
        private NativeParallelMultiHashMapFallback<Entity, EventAmount> _eventChanges;
        private NativeList<Entity> _uniqueKeys;
        private NativeList<Entity> _uniqueEventKeys;

        private ComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _sourcesLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linksLookup;
        private ComponentLookup<LocalToWorld> _transformLookup;

        private IntrinsicWriter.Lookup _intrinsicWriters;
        private ConditionEventWriter.Lookup _eventWriters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _intrinsicChanges =
                new NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>(64, Allocator.Persistent);
            _eventChanges = new NativeParallelMultiHashMapFallback<Entity, EventAmount>(64, Allocator.Persistent);
            _uniqueKeys = new NativeList<Entity>(64, Allocator.Persistent);
            _uniqueEventKeys = new NativeList<Entity>(64, Allocator.Persistent);

            _targetsLookup = state.GetComponentLookup<Targets>(true);
            _sourcesLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linksLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _transformLookup = state.GetComponentLookup<LocalToWorld>(true);

            _intrinsicWriters.Create(ref state);
            _eventWriters.Create(ref state);

            state.RequireForUpdate<SpatialMaskDatabase>();
            state.RequireForUpdate<SpatialTrackingActive>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _intrinsicChanges.Dispose();
            _eventChanges.Dispose();
            _uniqueKeys.Dispose();
            _uniqueEventKeys.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var buildSystemHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<SpatialMapBuildSystem>();
            if (!state.EntityManager.HasComponent<SpatialMapSingleton>(buildSystemHandle)) return;

            var mapSingleton = state.EntityManager.GetComponentData<SpatialMapSingleton>(buildSystemHandle);
            if (!mapSingleton.Map.Map.IsCreated) return;

            _targetsLookup.Update(ref state);
            _sourcesLookup.Update(ref state);
            _linksLookup.Update(ref state);
            _transformLookup.Update(ref state);

            _intrinsicWriters.Update(ref state, SystemAPI.GetSingleton<EssenceConfig>());
            _eventWriters.Update(ref state);

            var maskDatabase = SystemAPI.GetSingleton<SpatialMaskDatabase>();

            ref var buildSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<SpatialMapBuildSystem>(buildSystemHandle);
            var entitiesArray = buildSystem.Entities.AsArray();

            var evalJob = new EvaluateJob
            {
                MapSingleton = mapSingleton,
                MapEntities = entitiesArray,
                MaskDatabase = maskDatabase,
                TargetsLookup = _targetsLookup,
                SourcesLookup = _sourcesLookup,
                LinksLookup = _linksLookup,
                TransformLookup = _transformLookup,
                IntrinsicChanges = _intrinsicChanges.AsWriter(),
                EventChanges = _eventChanges.AsWriter()
            };

            var exitJob = new ExitJob
            {
                IntrinsicChanges = _intrinsicChanges.AsWriter(),
                EventChanges = _eventChanges.AsWriter()
            };

            var evalDep = evalJob.ScheduleParallel(state.Dependency);
            var exitDep = exitJob.ScheduleParallel(evalDep);

            var intrinsicApplyDep = _intrinsicChanges.Apply(exitDep, out var intrinsicReader);
            var eventApplyDep = _eventChanges.Apply(exitDep, out var eventReader);

            var uniqueIntrinsicDep = new GetUniqueKeysJob<IntrinsicAmount>
                { Map = intrinsicReader, Keys = _uniqueKeys }.Schedule(intrinsicApplyDep);
            var uniqueEventDep =
                new GetUniqueKeysJob<EventAmount> { Map = eventReader, Keys = _uniqueEventKeys }.Schedule(
                    eventApplyDep);

            var applyIntrinsicDep = new ApplyIntrinsicJob
            {
                Keys = _uniqueKeys.AsDeferredJobArray(),
                GroupChanges = intrinsicReader,
                IntrinsicWriters = _intrinsicWriters
            }.Schedule(_uniqueKeys, 64, uniqueIntrinsicDep);

            var applyEventDep = new ApplyEventJob
            {
                Keys = _uniqueEventKeys.AsDeferredJobArray(),
                GroupChanges = eventReader,
                EventWriters = _eventWriters
            }.Schedule(_uniqueEventKeys, 64, JobHandle.CombineDependencies(uniqueEventDep, applyIntrinsicDep));

            var clearIntrinsicDep = _intrinsicChanges.Clear(applyIntrinsicDep);
            var clearEventDep = _eventChanges.Clear(applyEventDep);

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
                Map.GetUniqueKeyArray(Keys);
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
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> SourcesLookup;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> LinksLookup;
            [ReadOnly] public ComponentLookup<LocalToWorld> TransformLookup;

            public NativeParallelMultiHashMapFallback<Entity, IntrinsicAmount>.ParallelWriter IntrinsicChanges;
            public NativeParallelMultiHashMapFallback<Entity, EventAmount>.ParallelWriter EventChanges;

            private void Execute(Entity clipEntity, in TrackBinding binding, in SpatialActiveClipData clipData,
                ref DynamicBuffer<SpatialActiveTarget> previous)
            {
                if (binding.Value == Entity.Null || !MaskDatabase.Blob.Value.Has(clipData.MaskKey)) return;
                if (!TransformLookup.TryGetComponent(binding.Value, out var casterTransform)) return;

                var currentHits = new NativeList<Entity>(Allocator.Temp);

                var centerCell =
                    MapSingleton.Map.Quantized(casterTransform.Position.xz - MapSingleton.CameraPos);
                ref var mask = ref MaskDatabase.Blob.Value.Masks[clipData.MaskKey];

                for (var y = 0; y < mask.Size; y++)
                for (var x = 0; x < mask.Size; x++)
                {
                    if (mask.Get(x, y) <= 0) continue;

                    var cell = centerCell + new int2(x - mask.Size / 2, mask.Size / 2 - y);
                    var hash = MapSingleton.Map.Hash(cell);

                    if (!MapSingleton.Map.Map.TryGetFirstValue(hash, out var item, out var it)) continue;

                    do
                    {
                        var target = MapEntities[item];
                        var targets = TargetsLookup.HasComponent(binding.Value)
                            ? TargetsLookup[binding.Value]
                            : default;

                        if (TryResolveTarget(clipData.RouteTo, clipData.RouteLinkKey, binding.Value, target,
                                targets, SourcesLookup, LinksLookup,
                                out var resolved))
                            currentHits.Add(resolved);
                    } while (MapSingleton.Map.Map.TryGetNextValue(out item, ref it));
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
                        i++;
                        j++;
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

                while (i < previous.Length)
                {
                    FireExit(previous[i].Target, clipData);
                    i++;
                }

                while (j < uniqueCount)
                {
                    FireEnter(currentHits[j], clipData);
                    j++;
                }

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
                if (data.IntrinsicStore.Value != 0)
                    IntrinsicChanges.Add(e, new IntrinsicAmount(data.IntrinsicStore, 1));
                if (data.OnEnter != ConditionKey.Null) EventChanges.Add(e, new EventAmount(data.OnEnter, 1));
            }

            private void FireExit(Entity e, in SpatialActiveClipData data)
            {
                if (data.IntrinsicStore.Value != 0)
                    IntrinsicChanges.Add(e, new IntrinsicAmount(data.IntrinsicStore, -1));
                if (data.OnExit != ConditionKey.Null) EventChanges.Add(e, new EventAmount(data.OnExit, 1));
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
                        IntrinsicChanges.Add(previous[i].Target, new IntrinsicAmount(clipData.IntrinsicStore, -1));

                    if (clipData.OnExit != ConditionKey.Null)
                        EventChanges.Add(previous[i].Target, new EventAmount(clipData.OnExit, 1));
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

            private static void AddOrAccumulate(ref FixedList4096Bytes<IntrinsicAmount> values, IntrinsicAmount value,
                ref IntrinsicWriter writer)
            {
                for (var i = 0; i < values.Length; i++)
                    if (values[i].Intrinsic.Equals(value.Intrinsic))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
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
            [ReadOnly] public NativeArray<Entity> Keys;
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

            private static void AddOrAccumulate(ref FixedList4096Bytes<EventAmount> values, EventAmount value,
                ref ConditionEventWriter writer)
            {
                for (var i = 0; i < values.Length; i++)
                    if (values[i].Event.Equals(value.Event))
                    {
                        var existing = values[i];
                        existing.Amount += value.Amount;
                        values[i] = existing;
                        return;
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

            public IntrinsicAmount(IntrinsicKey intrinsic, int amount)
            {
                Intrinsic = intrinsic;
                Amount = amount;
            }

            public bool Equals(IntrinsicAmount other)
            {
                return Intrinsic.Equals(other.Intrinsic);
            }

            public override int GetHashCode()
            {
                return Intrinsic.GetHashCode();
            }
        }

        private struct EventAmount : IEquatable<EventAmount>
        {
            public readonly ConditionKey Event;
            public int Amount;

            public EventAmount(ConditionKey evt, int amount)
            {
                Event = evt;
                Amount = amount;
            }

            public bool Equals(EventAmount other)
            {
                return Event.Equals(other.Event);
            }

            public override int GetHashCode()
            {
                return Event.GetHashCode();
            }
        }

        private static bool TryResolveTarget(Target targetMode, ushort linkKey, Entity self, Entity other,
            in Targets targets,
            in UnsafeComponentLookup<EntityLinkSource> sources, in UnsafeBufferLookup<EntityLinkEntry> links,
            out Entity resolved)
        {
            resolved = Entity.Null;

            var target = targetMode switch
            {
                Target.Self => self,
                Target.Target => other,
                Target.Owner => targets.Owner,
                Target.Source => targets.Source,
                Target.Custom => targets.Custom,
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