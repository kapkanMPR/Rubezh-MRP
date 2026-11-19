using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeBattleComposer
    {
        sealed class WorldItemState
        {
            public int Id,
                Seen,
                Stage,
                Art,
                Width,
                HeightPixels;
            public V2 Position;
            public float Height,
                Angle,
                Bias,
                Scale;
            public bool Primitive;
        }

        readonly Dictionary<(object, int), WorldItemState> worldItems = new Dictionary<(object, int), WorldItemState>();
        readonly List<(object, int)> expiredWorldItems = new List<(object, int)>();
        int worldEpoch,
            nextWorldId = int.MinValue + 100;
        HdGpuRenderer worldRenderer;
        public int WorldItemRebuilds { get; private set; }
        public int WorldItemReuseHits { get; private set; }
        public int WorldItemPruned { get; private set; }
        public int RetainedWorldItems => worldItems.Count;

        void BeginWorldItems(HdGpuRenderer renderer, bool reset)
        {
            if (reset)
            {
                if (worldRenderer != null)
                    foreach (var item in worldItems.Values)
                        worldRenderer.RemoveWorldItem(item.Id);
                worldItems.Clear();
                expiredWorldItems.Clear();
                nextWorldId = int.MinValue + 100;
            }
            worldRenderer = renderer;
            worldEpoch++;
        }

        protected override bool BeginWorldItem(
            object item,
            int kind,
            V2 position,
            float height,
            float angle,
            int art,
            int width = 0,
            int heightPixels = 0,
            float bias = 0
        )
        {
            var key = (item, kind);
            WorldItemState previous;
            bool created = !worldItems.TryGetValue(key, out previous);
            if (created)
                worldItems.Add(key, previous = new WorldItemState { Id = nextWorldId++ });
            previous.Seen = worldEpoch;
            int stage = compositionStage;
            if (
                !created
                && previous.Stage == stage
                && previous.Position.X == position.X
                && previous.Position.Y == position.Y
                && previous.Height == height
                && previous.Angle == angle
                && previous.Art == art
                && previous.Width == width
                && previous.HeightPixels == heightPixels
                && previous.Bias == bias
                && previous.Scale == Canvas.Scale
                && previous.Primitive == Rubezh.Graphics.PrimitiveBattleMode
            )
            {
                worldRenderer.ReuseWorldItem(previous.Id);
                WorldItemReuseHits++;
                return false;
            }
            previous.Stage = stage;
            previous.Position = position;
            previous.Height = height;
            previous.Angle = angle;
            previous.Scale = Canvas.Scale;
            previous.Primitive = Rubezh.Graphics.PrimitiveBattleMode;
            previous.Art = art;
            previous.Width = width;
            previous.HeightPixels = heightPixels;
            previous.Bias = bias;
            worldRenderer.BeginWorldItem(previous.Id);
            WorldItemRebuilds++;
            return true;
        }

        protected override void EndWorldItem()
        {
            worldRenderer.EndWorldItem();
        }

        void EndWorldItems()
        {
            expiredWorldItems.Clear();
            foreach (var item in worldItems)
                if (item.Value.Seen != worldEpoch)
                    expiredWorldItems.Add(item.Key);
            foreach (var key in expiredWorldItems)
            {
                worldRenderer.RemoveWorldItem(worldItems[key].Id);
                worldItems.Remove(key);
                WorldItemPruned++;
            }
            expiredWorldItems.Clear();
        }
    }
}
