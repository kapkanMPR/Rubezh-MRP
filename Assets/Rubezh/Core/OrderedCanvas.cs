using System;
using System.Collections.Generic;

namespace SilentOrders.Rendering
{
    // Stable painter order shared by CPU and HD GPU, including airborne objects.
    public interface ISmokePuffGpu { bool SmokePuff(float x,float y,float scale,int phase,int density,bool dark,float depth); }
    public sealed class OrderedCanvas : ICanvasGpu, ISmokePuffGpu, IVisualGroupGpu
    {
        struct Command
        {
            public int Id, Order, Layer, W, H,Actor;
            public float ActorX,ActorY;
            public float X, Y, Width, Height, Angle, Depth;
            public uint Color;
            public bool Test;
        }

        readonly List<Command> commands = new List<Command>();
        PixelCanvas canvas;
        ICanvasGpu output;
        public int Stage;
        int actor=-1;float actorX,actorY;
        public void BeginVisual(int id,float x,float y){actor=id;actorX=x;actorY=y;}
        public void EndVisual(){actor=-1;actorX=actorY=0;}
        public void Begin(PixelCanvas source, ICanvasGpu destination)
        {
            canvas = source;
            output = destination;
            commands.Clear();
            Stage = 1;EndVisual();
        }

        public void Background(uint[] p, float[] d, object map, int revision)
        {
            throw new InvalidOperationException("Background must precede ordered objects");
        }

        public void Rect(float x, float y, float w, float h, uint color, float depth, bool test)
        {
            commands.Add(new Command { Actor=actor,ActorX=actorX,ActorY=actorY,Id = -1, Order = commands.Count, Layer = test ? Stage : 2, X = x, Y = y, Width = w, Height = h, Color = color, Depth = depth, Test = test });
        }

        public void Sprite(int id, float x, float y, int w, int h, float angle, float depth, bool test)
        {
            commands.Add(new Command { Actor=actor,ActorX=actorX,ActorY=actorY,Id = id, Order = commands.Count, Layer = test ? Stage : 2, X = x, Y = y, W = w, H = h, Angle = angle, Depth = depth, Test = test });
        }

        public bool SmokePuff(float x,float y,float scale,int phase,int density,bool dark,float depth) {
            if(!(output is ISmokePuffGpu))return false;
            commands.Add(new Command{Actor=actor,ActorX=actorX,ActorY=actorY,Id=-2,Order=commands.Count,Layer=Stage,X=x,Y=y,Width=scale,W=phase,H=density,Test=dark,Depth=depth});return true;
        }
        public void Flush()
        {
            commands.Sort((a, b) =>
            {
                int c = a.Layer.CompareTo(b.Layer);
                if (c == 0 && a.Layer < 2)
                    c = a.Depth.CompareTo(b.Depth);
                return c == 0 ? a.Order.CompareTo(b.Order) : c;
            });
            bool test = canvas.TestDepth, record = canvas.RecordDepth;
            float depth = canvas.Depth;
            var groups=output as IVisualGroupGpu;int emittedActor=-1;
            try
            {
                canvas.RecordDepth = false;
                foreach (var c in commands)
                {
                    if (output != null)
                    {
                        if(groups!=null&&emittedActor!=c.Actor){if(emittedActor>=0)groups.EndVisual();emittedActor=c.Actor;if(emittedActor>=0)groups.BeginVisual(emittedActor,c.ActorX,c.ActorY);}
                        if(c.Id==-2)
                            ((ISmokePuffGpu)output).SmokePuff(c.X,c.Y,c.Width,c.W,c.H,c.Test,c.Depth);
                        else if (c.Id < 0)
                            output.Rect(c.X, c.Y, c.Width, c.Height, c.Color, c.Depth, c.Test);
                        else
                            output.Sprite(c.Id, c.X, c.Y, c.W, c.H, c.Angle, c.Depth, c.Test);
                        continue;
                    }

                    canvas.TestDepth = c.Test;
                    canvas.Depth = c.Depth;
                    if (c.Id >= 0)
                        Rubezh.Graphics.ReplaySprite(canvas, c.Id, c.X, c.Y, c.W, c.H, c.Angle);
                    else
                        for (int y = Math.Max(0, (int)c.Y); y < Math.Min(canvas.Height, (int)(c.Y + c.Height)); y++)
                            for (int x = Math.Max(0, (int)c.X); x < Math.Min(canvas.Width, (int)(c.X + c.Width)); x++)
                                canvas.FineDot(x, y, c.Color);
                }
            }
            finally
            {
                if(groups!=null&&emittedActor>=0)groups.EndVisual();
                canvas.TestDepth = test;
                canvas.RecordDepth = record;
                canvas.Depth = depth;
                commands.Clear();
            }
        }
    }
}
