namespace SilentOrders.Rendering
{
    // Coordinates of rectangles are physical canvas pixels; sprites use logical coordinates.
    public interface ICanvasGpu
    {
        void Background(uint[] pixels, float[] depth, object map, int revision);
        void Rect(float x, float y, float width, float height, uint color, float depth, bool testDepth);
        void Sprite(int id, float x, float y, int width, int height, float angle, float depth, bool testDepth);
    }
}

namespace SilentOrders.Rendering
{
    // Presentation boundaries only; battle simulation remains unaware of Unity.
    public interface IVisualGroupGpu
    {
        void BeginVisual(int soldierId, float x, float y);
        void EndVisual();
    }
}
