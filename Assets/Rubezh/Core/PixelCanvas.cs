using System;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    public sealed class PixelCanvas
    {
        public float[] RoofOcclusion;
        public bool RoofBlocker=true;
        public void StoreDepth(int index,float depth){Occlusion[index]=depth;if(RoofBlocker&&RoofOcclusion!=null&&depth>RoofOcclusion[index])RoofOcclusion[index]=depth;}
        public readonly int Width;
        public readonly int Height;
        public readonly float Scale;
        uint[] pixelStorage;
        public uint[] Pixels => pixelStorage ?? (pixelStorage = new uint[Width * Height]);
        public long AllocatedPixelBytes => pixelStorage == null ? 0 : (long)pixelStorage.Length * 4;
        public void ReleasePixels() { pixelStorage = null; }
        public ICanvasGpu Gpu;
        // Retained native actors keep local geometry independent of fractional world position.
        // Global pixel rounding would otherwise change thin marker topology while walking.
        public bool RetainedObjectGeometry;
        bool objectTransform;
        float pivotX, pivotY, objectX, objectY, objectCos, objectSin;
        public void ClearObjectTransform()
        {
            objectTransform = false;
        }

        public void SetObjectTransform(float x, float y, float sx, float sy, float degrees)
        {
            objectTransform = sx != 1 || sy != 1 || degrees != 0;
            pivotX = x * Scale;
            pivotY = y * Scale;
            objectX = sx;
            objectY = sy;
            objectCos = (float)Math.Cos(degrees * Math.PI / 180);
            objectSin = (float)Math.Sin(degrees * Math.PI / 180);
        }

        public void TransformSprite(ref float x, ref float y, ref float w, ref float h)
        {
            if (!objectTransform) return;
            float px = pivotX / Scale, py = pivotY / Scale;
            x = px + (x - px) * objectX; y = py + (y - py) * objectY;
            w *= objectX; h *= objectY;
        }

        void ObjectRect(float x, float y, float w, float h, uint color)
        {
            if (Gpu != null && objectSin == 0 && objectX > 0 && objectY > 0) {
                if (RetainedObjectGeometry) {
                    Gpu.Rect(pivotX+(x-pivotX)*objectX,pivotY+(y-pivotY)*objectY,w*objectX,h*objectY,color,Depth,TestDepth);
                    return;
                }
                int left = (int)Math.Round(pivotX + (x - pivotX) * objectX);
                int top = (int)Math.Round(pivotY + (y - pivotY) * objectY);
                int right = (int)Math.Round(pivotX + (x + w - pivotX) * objectX);
                int bottom = (int)Math.Round(pivotY + (y + h - pivotY) * objectY);
                Gpu.Rect(left, top, right - left, bottom - top, color, Depth, TestDepth);
                return;
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float dx = (x + (k % 2) * w - pivotX) * objectX, dy = (y + (k / 2) * h - pivotY) * objectY;
                float tx = pivotX + dx * objectCos - dy * objectSin, ty = pivotY + dx * objectSin + dy * objectCos;
                minX = Math.Min(minX, tx);
                maxX = Math.Max(maxX, tx);
                minY = Math.Min(minY, ty);
                maxY = Math.Max(maxY, ty);
            }

            objectTransform = false;
            try
            {
                for (int py = Math.Max(0, (int)Math.Floor(minY)); py < Math.Min(Height, (int)Math.Ceiling(maxY)); py++)
                {
                    int run = -1, end = Math.Min(Width, (int)Math.Ceiling(maxX));
                    for (int px = Math.Max(0, (int)Math.Floor(minX)); px <= end; px++)
                    {
                        float dx = px + .5f - pivotX, dy = py + .5f - pivotY;
                        float sourceX = pivotX + (dx * objectCos + dy * objectSin) / objectX, sourceY = pivotY + (-dx * objectSin + dy * objectCos) / objectY;
                        bool inside = px < end && sourceX >= x && sourceX < x + w && sourceY >= y && sourceY < y + h;
                        if (inside && run < 0)
                            run = px;
                        if (!inside && run >= 0)
                        {
                            if (Gpu != null)
                                Gpu.Rect(run, py, px - run, 1, color, Depth, TestDepth);
                            else
                                for (int q = run; q < px; q++)
                                    FineDot(q, py, color);
                            run = -1;
                        }
                    }
                }
            }
            finally
            {
                objectTransform = true;
            }
        }

        public float[] Occlusion;
        public float Depth;
        public int LayerLeft, LayerRight = int.MaxValue;
        public int ClipLeft,ClipTop,ClipRight=int.MaxValue,ClipBottom=int.MaxValue;
        public bool RecordDepth;
        public bool TestDepth;
        public PixelCanvas(int width, int height, float scale = 1f)
        {
            Scale = scale;
            Width = (int)System.Math.Round((float)width * scale);
            Height = (int)System.Math.Round((float)height * scale);
        }

        public static uint RGB(int r, int g, int b)
        {
            return (uint)(r | (g << 8) | (b << 16)) | 0xFF000000u;
        }

        public void Clear(uint color)
        {
            for (int i = 0; i < Pixels.Length; i++)
            {
                Pixels[i] = color;
            }
        }

        public void Dot(int x, int y, uint c)
        {
            Rect(x, y, 1, 1, c);
        }

        public void FineDot(int x, int y, uint c)
        {
            if (objectTransform)
            {
                ObjectRect(x, y, 1, 1, c);
                return;
            }

            if (Gpu != null)
            {
                Gpu.Rect(x, y, 1, 1, c, Depth, TestDepth);
                return;
            }

            if (x >= ClipLeft && y >= ClipTop && x < Math.Min(Width,ClipRight) && y < Math.Min(Height,ClipBottom))
            {
                int num = y * Width + x;
                if (!TestDepth || Occlusion[num] <= Depth)
                {
                    Pixels[num] = c;
                }

                if (RecordDepth)
                {
                    StoreDepth(num,Depth);
                }
            }
        }

        public void Rect(int x, int y, int w, int h, uint c)
        {
            if (objectTransform)
            {
                ObjectRect(x * Scale, y * Scale, w * Scale, h * Scale, c);
                return;
            }

            if (Gpu != null)
            {
                int left = (int)System.Math.Round(x * Scale), top = (int)System.Math.Round(y * Scale);
                Gpu.Rect(left, top, (int)System.Math.Round((x + w) * Scale) - left, (int)System.Math.Round((y + h) * Scale) - top, c, Depth, TestDepth);
                return;
            }

            for (int i = System.Math.Max((int)System.Math.Round((float)y * Scale), ClipTop); i < System.Math.Min((int)System.Math.Round((float)(y + h) * Scale), Math.Min(Height,ClipBottom)); i++)
            {
                for (int j = System.Math.Max((int)System.Math.Round((float)x * Scale), ClipLeft); j < System.Math.Min((int)System.Math.Round((float)(x + w) * Scale), Math.Min(Width,ClipRight)); j++)
                {
                    int num = i * Width + j;
                    if (!TestDepth || Occlusion[num] <= Depth)
                    {
                        Pixels[num] = c;
                    }

                    if (RecordDepth)
                    {
                        StoreDepth(num,Depth);
                    }
                }
            }
        }

        public void Frame(int x, int y, int w, int h, uint c)
        {
            Line(x, y, x + w, y, c);
            Line(x + w, y, x + w, y + h, c);
            Line(x + w, y + h, x, y + h, c);
            Line(x, y + h, x, y, c);
        }

        public void Line(int x0, int y0, int x1, int y1, uint color)
        {
            var vector = Gpu as IVectorCanvasGpu;
            if (vector != null && !objectTransform && vector.Line(x0 * Scale, y0 * Scale, x1 * Scale, y1 * Scale, Scale, color, Depth, TestDepth)) return;
            int num = System.Math.Abs(x1 - x0);
            int num2 = ((x0 < x1) ? 1 : (-1));
            int num3 = -System.Math.Abs(y1 - y0);
            int num4 = ((y0 < y1) ? 1 : (-1));
            int num5 = num + num3;
            while (true)
            {
                Dot(x0, y0, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int num6 = 2 * num5;
                if (num6 >= num3)
                {
                    num5 += num3;
                    x0 += num2;
                }

                if (num6 <= num)
                {
                    num5 += num;
                    y0 += num4;
                }
            }
        }

        public void Polygon(int[] xs, int[] ys, uint c)
        {
            int num = (int)System.Math.Round((float)Height / Scale) - 1;
            int num2 = 0;
            for (int i = 0; i < ys.Length; i++)
            {
                num = System.Math.Min(num, ys[i]);
                num2 = System.Math.Max(num2, ys[i]);
            }

            int[] array = new int[xs.Length];
            for (int j = System.Math.Max(0, num); j <= System.Math.Min((int)System.Math.Round((float)Height / Scale) - 1, num2); j++)
            {
                int num3 = 0;
                int num4 = 0;
                int num5 = xs.Length - 1;
                while (num4 < xs.Length)
                {
                    if ((ys[num4] <= j && ys[num5] > j) || (ys[num5] <= j && ys[num4] > j))
                    {
                        array[num3++] = xs[num4] + (j - ys[num4]) * (xs[num5] - xs[num4]) / (ys[num5] - ys[num4]);
                    }

                    num5 = num4++;
                }

                System.Array.Sort(array, 0, num3);
                for (int k = 0; k + 1 < num3; k += 2)
                {
                    Rect(array[k], j, array[k + 1] - array[k] + 1, 1, c);
                }
            }
        }

        public void Diamond(int x, int y, int hw, int hh, uint c)
        {
            Polygon(new int[4] { x, x + hw, x, x - hw }, new int[4] { y - hh, y, y + hh, y }, c);
        }

        public void Ellipse(int x, int y, int rx, int ry, uint c)
        {
            var vector = Gpu as IVectorCanvasGpu;
            if (vector != null && RetainedObjectGeometry && objectTransform && objectSin == 0 && objectX == objectY) {
                float cx=pivotX+(x*Scale-pivotX)*objectX,cy=pivotY+(y*Scale-pivotY)*objectY;
                if(vector.Ellipse(cx,cy,rx,ry,Scale*objectX,c,Depth,TestDepth))return;
            }
            if (vector != null && !objectTransform && vector.Ellipse(x * Scale, y * Scale, rx, ry, Scale, c, Depth, TestDepth)) return;
            if (Gpu != null)
            {
                if (rx <= 0 || ry <= 0)
                    return;
                for (int row = -ry; row <= ry; row++)
                {
                    int span = rx;
                    while (span > 0 && (float)span * span / (rx * rx) + (float)row * row / (ry * ry) > 1f)
                        span--;
                    Rect(x - span, y + row, span * 2 + 1, 1, c);
                }

                return;
            }

            for (int i = -ry; i <= ry; i++)
            {
                for (int j = -rx; j <= rx; j++)
                {
                    if ((float)j * (float)j / (float)(rx * rx) + (float)i * (float)i / (float)(ry * ry) <= 1f)
                    {
                        Dot(x + j, y + i, c);
                    }
                }
            }
        }
    }
}
