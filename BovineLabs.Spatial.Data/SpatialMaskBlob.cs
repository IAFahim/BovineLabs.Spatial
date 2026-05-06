using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Spatial.Data
{
    public struct SpatialMaskDatabase : IComponentData
    {
        public BlobAssetReference<SpatialMaskDatabaseBlob> Blob;
    }

    public struct SpatialMaskDatabaseBlob
    {
        public BlobArray<SpatialMaskBlob> Masks;

        public bool Has(ushort key)
        {
            return key < Masks.Length && Masks[key].Values.Length != 0;
        }
    }

    public struct SpatialMaskBlob
    {
        public BlobArray<sbyte> Values;

        public int Size => (int)math.sqrt(Values.Length);

        public sbyte Get(int x, int y)
        {
            var size = Size;
            return x < 0 || y < 0 || x >= size || y >= size ? (sbyte)0 : Values[x + y * size];
        }
    }
}