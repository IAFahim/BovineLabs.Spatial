using BovineLabs.Essence.Authoring;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Spatial.Data;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Spatial.Authoring
{
    public sealed class SpatialActiveClip : DOTSClip, ITimelineClipAsset
    {
        public SpatialMaskAsset mask;
        public Target routeTo = Target.Target;
        public EntityLinkSchema routeLink;
        public ConditionEventObject onEnter;
        public ConditionEventObject onExit;
        public IntrinsicSchemaObject intrinsicStore;

        public override double duration => 1;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            SpatialMaskAuthoringUtility.TryGetKey(mask, out var maskKey);
            EntityLinkAuthoringUtility.TryGetKey(routeLink, out var linkKey);

            context.Baker.AddComponent(clipEntity, new SpatialActiveClipData
            {
                MaskKey = maskKey,
                RouteTo = routeTo,
                RouteLinkKey = linkKey,
                OnEnter = onEnter ? onEnter.Key : ConditionKey.Null,
                OnExit = onExit ? onExit.Key : ConditionKey.Null,
                IntrinsicStore = intrinsicStore ? intrinsicStore.Key : default
            });

            context.Baker.AddBuffer<SpatialActiveTarget>(clipEntity);

            base.Bake(clipEntity, context);
        }
    }
}