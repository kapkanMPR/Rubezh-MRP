namespace SilentOrders.Rendering
{
    // Rendering-only contract. Simulation does not depend on Unity or this sink.
    public interface INativeComposition
    {
        bool DirectComposition { get; }
        void BeginDynamic();
        void SetStage(int stage);
        void EndDynamic();
    }
    public interface IVectorCanvasGpu
    {
        bool Ellipse(float x, float y, int rx, int ry, float scale, uint color, float depth, bool test);
        bool Line(float x0, float y0, float x1, float y1, float width, uint color, float depth, bool test);
    }
}
