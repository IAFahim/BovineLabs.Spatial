using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Spatial.Authoring
{
    [TrackClipType(typeof(SpatialActiveClip))]
    [TrackBindingType(typeof(SpatialTargetAuthoring))]
    [DisplayName("BovineLabs/Spatial/SpatialTrack")]
    public sealed class SpatialTrack : DOTSTrack
    {
    }
}