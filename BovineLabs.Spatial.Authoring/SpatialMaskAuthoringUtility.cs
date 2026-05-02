namespace BovineLabs.Spatial.Authoring
{
    public static class SpatialMaskAuthoringUtility
    {
        public static bool TryGetKey(SpatialMaskAsset mask, out ushort key)
        {
            key = mask == null ? (ushort)0 : mask.Id;
            return key != 0;
        }
    }
}