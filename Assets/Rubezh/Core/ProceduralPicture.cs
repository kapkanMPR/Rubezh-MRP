namespace SilentOrders.Core
{
    public sealed class ProceduralPicture
    {
        public float Left,Top,Width,Height;
        public int PixelWidth,PixelHeight;
        // Native presentation keeps imported textures and a tiny wall-state mask, never a CPU image copy.
        public string NativeKey;
        public DynamicBuilding.Wall NativeWall;
        public float[][] NativeFaces;
        public uint NativeColor;
        public uint[] Pixels;
        public float[] Depth;
    }
}
