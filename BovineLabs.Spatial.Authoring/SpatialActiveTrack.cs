using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Spatial.Authoring
{
    [TrackClipType(typeof(SpatialActiveClip))]
    [TrackBindingType(typeof(SpatialTargetAuthoring))]
    public sealed class SpatialActiveTrack : DOTSTrack
    {
    }
}
