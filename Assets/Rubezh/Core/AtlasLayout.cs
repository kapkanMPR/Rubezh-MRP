using System;
using System.Collections.Generic;

namespace SilentOrders.Rendering
{
    // Geometry only: no image resampling, no Unity dependency. One-pixel extrusion on all sides.
    public static class AtlasLayout
    {
        public struct Size
        {
            public int W,
                H;

            public Size(int w, int h)
            {
                W = w;
                H = h;
            }
        }

        public struct Placement
        {
            public int Page,
                X,
                Y,
                W,
                H;
        }

        public sealed class Layout
        {
            public Placement[] Items;
            public Size[] Pages;
            public long PixelCount;
        }

        sealed class Shelf
        {
            public int Y,
                H,
                X;
        }

        sealed class Page
        {
            public readonly List<Shelf> Shelves = new List<Shelf>();
            public int W = 4,
                H;
        }

        // Two pixels for extrusion plus four pixels for the reserved primitive tile.
        // Keep the same alignment/limit contract as Pack; subtracting only two lets
        // a square tile fill an entire page and leaves nowhere for the white tile.
        public static int ProceduralTileSide(int deviceLimit)
        {
            int page = Math.Min(1024, deviceLimit) / 4 * 4;
            if (page < 8)
                throw new NotSupportedException("Procedural atlas needs at least 8 pixels.");
            return page - 6;
        }

        static int Align(int n) => checked((n + 3) / 4 * 4);

        public static Layout Pack(IList<Size> sizes, int deviceLimit)
        {
            int limit = Math.Min(4096, deviceLimit);
            limit = limit / 4 * 4;
            if (limit < 4)
                throw new NotSupportedException("Atlas device limit is below four pixels.");
            int widest = 4;
            long area = 16;
            for (int i = 0; i < sizes.Count; i++)
            {
                var s = sizes[i];
                if (s.W < 1 || s.H < 1 || s.W > limit - 2 || s.H > limit - 2)
                    throw new NotSupportedException("Sprite exceeds device atlas limit.");
                widest = Math.Max(widest, s.W + 2);
                area += (long)(s.W + 2) * (s.H + 2);
            }

            var widths = new SortedSet<int> { limit, Align(widest) };
            for (int w = 256; w < limit; w *= 2)
                if (w >= widest)
                    widths.Add(w);
            int ideal = Math.Min(limit, Math.Max(Align(widest), Align((int)Math.Ceiling(Math.Sqrt(area)))));
            widths.Add(ideal);
            Layout best = null;
            foreach (int width in widths)
            {
                Layout trial;
                try
                {
                    trial = PackWidth(sizes, width, limit);
                }
                catch (NotSupportedException)
                {
                    continue;
                }

                if (
                    best == null
                    || trial.Pages.Length < best.Pages.Length
                    || trial.Pages.Length == best.Pages.Length && trial.PixelCount < best.PixelCount
                )
                    best = trial;
            }

            if (best == null)
                throw new NotSupportedException("Sprite leaves no room for atlas white tile.");
            return best;
        }

        static Layout PackWidth(IList<Size> sizes, int width, int limit)
        {
            var order = new List<int>();
            for (int i = 0; i < sizes.Count; i++)
                order.Add(i);
            order.Sort(
                (a, b) =>
                {
                    int c = sizes[b].H.CompareTo(sizes[a].H);
                    if (c == 0)
                        c = sizes[b].W.CompareTo(sizes[a].W);
                    return c == 0 ? a.CompareTo(b) : c;
                }
            );
            var pages = new List<Page>();
            var result = new Placement[sizes.Count];
            foreach (int id in order)
            {
                var size = sizes[id];
                int w = size.W + 2,
                    h = Math.Max(4, size.H + 2),
                    pi = -1;
                Shelf selected = null;
                for (int p = 0; p < pages.Count; p++)
                    foreach (var shelf in pages[p].Shelves)
                        if (shelf.H >= h && shelf.X + w <= width && (selected == null || shelf.H < selected.H))
                        {
                            pi = p;
                            selected = shelf;
                        }

                if (selected == null)
                {
                    for (int p = 0; p < pages.Count; p++)
                        if (pages[p].H + h <= limit)
                        {
                            pi = p;
                            break;
                        }

                    if (pi < 0)
                    {
                        pi = pages.Count;
                        pages.Add(new Page());
                    }

                    var pg = pages[pi];
                    int x = pg.Shelves.Count == 0 && w + 4 <= width ? 4 : 0;
                    // White tile has its own first row if a sprite fills the chosen width.
                    if (pg.Shelves.Count == 0 && x == 0)
                        pg.H = 4;
                    if (pg.H + h > limit)
                    {
                        pi = pages.Count;
                        pages.Add(new Page());
                        pg = pages[pi];
                        x = 0;
                        pg.H = 4;
                        if (pg.H + h > limit)
                            throw new NotSupportedException("Sprite leaves no room for atlas white tile.");
                    }

                    selected = new Shelf
                    {
                        Y = pg.H,
                        H = Math.Max(h, 4),
                        X = x,
                    };
                    pg.Shelves.Add(selected);
                    pg.H += selected.H;
                }

                result[id] = new Placement
                {
                    Page = pi,
                    X = selected.X + 1,
                    Y = selected.Y + 1,
                    W = size.W,
                    H = size.H,
                };
                selected.X += w;
                pages[pi].W = Math.Max(pages[pi].W, selected.X);
            }

            if (pages.Count == 0)
                pages.Add(new Page { H = 4 });
            var dims = new Size[pages.Count];
            long pixels = 0;
            for (int i = 0; i < dims.Length; i++)
            {
                dims[i] = new Size(Align(pages[i].W), Align(pages[i].H));
                pixels += (long)dims[i].W * dims[i].H;
            }

            return new Layout
            {
                Items = result,
                Pages = dims,
                PixelCount = pixels,
            };
        }

        public static void CopyExtruded(byte[] source, byte[] destination, Size page, Placement r)
        {
            if (
                source == null
                || source.Length != checked(r.W * r.H * 4)
                || destination == null
                || destination.Length != checked(page.W * page.H * 4)
                || r.X < 1
                || r.Y < 1
                || r.X + r.W >= page.W
                || r.Y + r.H >= page.H
            )
                throw new ArgumentException("Invalid atlas copy bounds.");
            for (int yy = -1; yy <= r.H; yy++)
            for (int xx = -1; xx <= r.W; xx++)
            {
                int src = (Math.Max(0, Math.Min(r.H - 1, yy)) * r.W + Math.Max(0, Math.Min(r.W - 1, xx))) * 4;
                int dst = ((r.Y + yy) * page.W + r.X + xx) * 4;
                for (int c = 0; c < 4; c++)
                    destination[dst + c] = source[src + c];
            }
        }

        // Ordered contiguous batches: never sort translucent quads by texture.
        public sealed class DrawRuns
        {
            public struct Run
            {
                public int Page,
                    Start,
                    Count;
            }

            public readonly List<Run> Items = new List<Run>();
            int count;
            public int CurrentPage => Items.Count == 0 ? 0 : Items[Items.Count - 1].Page;

            public void Clear()
            {
                Items.Clear();
                count = 0;
            }

            public void AddQuad(int page)
            {
                int last = Items.Count - 1;
                if (last >= 0 && Items[last].Page == page)
                {
                    var r = Items[last];
                    r.Count += 6;
                    Items[last] = r;
                }
                else
                    Items.Add(
                        new Run
                        {
                            Page = page,
                            Start = count,
                            Count = 6,
                        }
                    );
                count += 6;
            }
        }
    }
}
