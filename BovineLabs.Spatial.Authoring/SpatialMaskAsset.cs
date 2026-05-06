using System;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using BovineLabs.Spatial.Settings;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Spatial.Authoring
{
    [AutoRef(nameof(SpatialGridSettings), "schemas", nameof(SpatialMaskAsset), "Schemas/Spatial/Masks")]
    [CreateAssetMenu(menuName = "BovineLabs/Spatial/Mask", fileName = "Spatial Mask")]
    public sealed class SpatialMaskAsset : ScriptableObject, IUID
    {
        private const int MinSize = 1;
        private const int MaxSize = 31;

        [SerializeField] [InspectorReadOnly] private ushort id;

        [SerializeField] [HideInInspector] private int width = 3;
        [SerializeField] [HideInInspector] private int height = 3;

        // Unity serializes byte[] safely.
        // Runtime reads each value as signed sbyte using unchecked cast.
        [SerializeField] [HideInInspector] private byte[] values = new byte[9];

        public ushort Id => id;
        public ushort Key => id;

        public int Width => width;
        public int Height => height;
        public int CenterX => width / 2;
        public int CenterY => height / 2;

        private void OnValidate()
        {
            width = SanitizeSize(width);
            height = SanitizeSize(height);
            EnsureArray();
        }

        int IUID.ID
        {
            get => id;
            set
            {
                if (value is < 0 or > ushort.MaxValue)
                {
                    Debug.LogError("Ran out of Spatial mask keys.");
                    return;
                }

                id = (ushort)value;
            }
        }

        public static implicit operator ushort(SpatialMaskAsset mask)
        {
            return mask == null ? (ushort)0 : mask.id;
        }

        public sbyte Get(int x, int y)
        {
            if (!IsInside(x, y))
                return 0;

            EnsureArray();
            return unchecked((sbyte)values[IndexOf(x, y)]);
        }

        public void Set(int x, int y, sbyte value)
        {
            if (!IsInside(x, y))
                return;

            EnsureArray();
            values[IndexOf(x, y)] = unchecked((byte)value);
        }

        public void Add(int x, int y, int delta)
        {
            var current = Get(x, y);
            var next = math.clamp(current + delta, sbyte.MinValue, sbyte.MaxValue);
            Set(x, y, (sbyte)next);
        }

        public void ResizeCentered(int newWidth, int newHeight)
        {
            newWidth = SanitizeSize(newWidth);
            newHeight = SanitizeSize(newHeight);

            EnsureArray();

            var oldWidth = width;
            var oldHeight = height;
            var oldValues = values;

            var oldCenter = new int2(oldWidth / 2, oldHeight / 2);
            var newCenter = new int2(newWidth / 2, newHeight / 2);

            width = newWidth;
            height = newHeight;
            values = new byte[width * height];

            for (var oldY = 0; oldY < oldHeight; oldY++)
            for (var oldX = 0; oldX < oldWidth; oldX++)
            {
                var offset = new int2(oldX, oldY) - oldCenter;
                var newPos = newCenter + offset;

                if (newPos.x < 0 || newPos.x >= width || newPos.y < 0 || newPos.y >= height)
                    continue;

                values[IndexOf(newPos.x, newPos.y)] = oldValues[oldX + oldY * oldWidth];
            }
        }

        public void Clear()
        {
            EnsureArray();
            Array.Clear(values, 0, values.Length);
        }

        public void MirrorX()
        {
            EnsureArray();

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width / 2; x++)
            {
                var a = IndexOf(x, y);
                var b = IndexOf(width - 1 - x, y);
                (values[a], values[b]) = (values[b], values[a]);
            }
        }

        public void MirrorY()
        {
            EnsureArray();

            for (var y = 0; y < height / 2; y++)
            for (var x = 0; x < width; x++)
            {
                var a = IndexOf(x, y);
                var b = IndexOf(x, height - 1 - y);
                (values[a], values[b]) = (values[b], values[a]);
            }
        }

        public void RotateClockwise()
        {
            EnsureArray();

            if (width != height)
                return;

            var old = values;
            var size = width;
            values = new byte[old.Length];

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var newX = size - 1 - y;
                var newY = x;
                values[newX + newY * size] = old[x + y * size];
            }
        }

        public int2 GetOffset(int x, int y)
        {
            // Top row = actor local forward.
            return new int2(x - CenterX, CenterY - y);
        }

        public int NonZeroCount()
        {
            EnsureArray();

            var count = 0;
            for (var i = 0; i < values.Length; i++)
                if (unchecked((sbyte)values[i]) != 0)
                    count++;

            return count;
        }

        public int TotalWeight()
        {
            EnsureArray();

            var total = 0;
            for (var i = 0; i < values.Length; i++)
                total += unchecked((sbyte)values[i]);

            return total;
        }

        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        private int IndexOf(int x, int y)
        {
            return x + y * width;
        }

        private void EnsureArray()
        {
            var required = width * height;

            if (values == null || values.Length != required)
                values = new byte[required];
        }

        private static int SanitizeSize(int value)
        {
            value = math.clamp(value, MinSize, MaxSize);

            if ((value & 1) == 0)
                value++;

            return math.clamp(value, MinSize, MaxSize);
        }
    }
}