using System;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    // Ground geometry samples the source atlas directly at viewport resolution.
    // Chunking never resizes or bakes the source textures into a map-sized image.
    public interface IGroundChunkGpu
    {
        void GroundChunks(BattleMap map, Action<int, int, int, int> drawCells);
    }

    public abstract partial class BattleVisualRecipes
    {
        protected Action<int, int, int, int> drawGroundCells;

        protected void DrawGroundCells(int left, int top, int right, int bottom)
        {
            bool test = Canvas.TestDepth,
                record = Canvas.RecordDepth;
            float depth = Canvas.Depth;
            try
            {
                Canvas.TestDepth = Canvas.RecordDepth = false;
                Canvas.Depth = 0;
                for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    if (x >= 0 && y >= 0 && x < activeMap.Columns && y < activeMap.Rows)
                    {
                        Canvas.Depth = 0;
                        Ground(activeMap, x, y);
                    }
                    else if (nativeExterior != null)
                        NativeExteriorGround(x, y);
                }
            }
            finally
            {
                Canvas.TestDepth = test;
                Canvas.RecordDepth = record;
                Canvas.Depth = depth;
            }
        }
    }
}
