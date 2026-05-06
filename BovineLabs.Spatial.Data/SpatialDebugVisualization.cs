using Unity.Entities;

namespace BovineLabs.Spatial.Data
{
    public enum VisualizationMode : byte
    {
        CubeHeight, // height = |value|, classic
        FlatPlate, // thin plate on ground, color only
        Pillar, // tall thin column
        WireCube, // transparent cube
        AxisTint // colors the grid cell itself
    }

    public enum ColorPalette : byte
    {
        CoolWarm, // blue-white-red, best for signed data
        Viridis, // matplotlib perceptually uniform
        Inferno,
        Jet, // classic 0-1 rainbow
        Turbo, // Google improved jet
        Grayscale
    }

    public struct SpatialDebugVisualization : IComponentData
    {
        public VisualizationMode Mode;
        public ColorPalette Palette;
        public float HeightScale; // 0.1-2
        public float PlateThickness; // for FlatPlate
        public float Opacity;
        public byte ShowGrid;
        public byte ShowNegativeBelow; // 1 = negatives go below plane
    }
}