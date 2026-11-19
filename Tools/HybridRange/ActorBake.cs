using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SilentOrders.Core;
using SilentOrders.Rendering;

sealed class ActorBake : BattleVisualRecipes
{
    static V2 Aim(float degrees)
    {
        double a = degrees * Math.PI / 180;
        float x = (float)Math.Cos(a) / 11f,
            y = (float)Math.Sin(a) / 5f;
        return new V2(x - y, x + y).Normalized;
    }

    public void Paint(BattleSimulation battle, Soldier soldier)
    {
        activeMap = battle.Map;
        Canvas = new PixelCanvas(64, 96, 32);
        SoldierVisualSection = 1;
        DrawSoldier(battle, soldier, false);
    }

    static void Main(string[] args)
    {
        Rubezh.Graphics.ConfigurePacks();
        var required = new HashSet<int>(
            new[] { 8, 21, 122, 123, 124, 125, 7902, 7903, 7904, 7905, 7958, 7959, 7960, 7961 }
        );
        foreach (int page in required.Select(id => Rubezh.SpritePackIndex.Page[id]).Distinct())
            Rubezh.Graphics.LoadPage(
                page,
                File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p" + page.ToString("D4") + ".bytes"),
                required: required
            );
        var b = new BattleSimulation(221);
        b.Fog.Enabled = false;
        b.Time = 1;
        var origin = Project(new V2(0, 0), b.Map);
        float sum = (32 - origin.X) / 11f,
            difference = (80 - origin.Y) / 5f;
        var position = new V2((sum - difference) * .5f, (sum + difference) * .5f);
        var facings = new[] { Aim(35), Aim(0), Aim(-35), Aim(180) };
        for (int side = 0; side < 2; side++)
        for (int dir = 0; dir < 4; dir++)
        {
            var s = new Soldier(b.Rules)
            {
                Id = 0,
                Side = side == 0 ? Side.Green : Side.Red,
                Role = Role.Rifleman,
                Look = side == 0 ? new Rubezh.GreenLook { headId = 116 } : null,
                Position = position,
                Facing = facings[dir],
                WeaponVariant = side == 0 ? 0 : 1,
                LastFire = -100,
                Health = 100,
                Deployed = true,
            };
            var painter = new ActorBake();
            painter.Paint(b, s);
            var pixels = painter.Canvas.Pixels;
            using (var w = new BinaryWriter(File.Create(Path.Combine(args[0], "frame" + (side * 4 + dir) + ".rgba"))))
            {
                w.Write(painter.Canvas.Width);
                w.Write(painter.Canvas.Height);
                foreach (uint pixel in pixels)
                    w.Write(pixel);
            }
            Console.WriteLine(
                "Painted side=" + side + " dir=" + dir + " missing=" + Rubezh.Graphics.MissingSpriteCount
            );
        }
        if (Rubezh.Graphics.MissingSpriteCount != 0)
            throw new Exception("Missing source sprite " + Rubezh.Graphics.LastMissingSprite);
    }
}
