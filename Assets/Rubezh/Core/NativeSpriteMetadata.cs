using System;

namespace Rubezh
{
    public static partial class Graphics
    {
        // CPU pixel consumers must load their own real source. This representation is GPU-only.
        public static void RegisterNativeSprite(int raw, int width, int height)
        {
            ConfigurePacks();
            int id = SpritePackIndex.Resolve(raw);
            if (id < 0 || id >= atlas.Length || width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(raw));
            if (atlas[id] == null)
                atlas[id] = new[]
                {
                    new Tex
                    {
                        W = width,
                        H = height,
                        P = Array.Empty<byte>(),
                    },
                };
        }

        public static bool HasSpritePixels(int raw)
        {
            int id = SpritePackIndex.Resolve(raw);
            return HasSprite(id) && atlas[id][0].P.Length > 0;
        }
    }
}
