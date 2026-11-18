using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class KnownSilhouette
    {
        public int Id;
        public V2 Position;
        public float SeenAt;
    }

    public struct DangerPatch
    {
        public V2 Position;
        public float Radius, Strength;
    }

    // Presentation-only knowledge projection. Never writes to AI contacts or consumes RNG.
    public sealed class BattleVisibility
    {
        public bool Enabled, Hardcore;
        public float NextUpdate = -1;
        public float[] Clear, ExploredBlend;
        public bool[] Explored, Current;
        public readonly HashSet<int> Visible = new HashSet<int>();
        public readonly Dictionary<int, KnownSilhouette> Ghosts = new Dictionary<int, KnownSilhouette>();
        public readonly List<DangerPatch> Danger = new List<DangerPatch>();
        int columns, rows;
        bool[] lit;
        readonly List<Soldier> observers = new List<Soldier>();
        readonly Dictionary<int, Contact> known = new Dictionary<int, Contact>();
        readonly List<int> remove = new List<int>();
        public bool CanDraw(Soldier s) => !Enabled || s.Side == Side.Green || Visible.Contains(s.Id);
        public bool Revealed(V2 p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            return !Enabled || Clear != null && x >= 0 && y >= 0 && x < columns && y < rows && Current != null && Current[y * columns + x];
        }

        public float Light(V2 p)
        {
            if (Clear == null)
                return 0;
            return Sample(Clear, p);
        }

        float Sample(float[] field, V2 p)
        {
            float x = p.X - .5f, y = p.Y - .5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float result = 0;
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int xx = x0 + dx, yy = y0 + dy;
                    if (xx >= 0 && yy >= 0 && xx < columns && yy < rows)
                        result += field[yy * columns + xx] * (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                }

            return result;
        }

        public bool WasExplored(V2 p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            return Explored != null && x >= 0 && y >= 0 && x < columns && y < rows && Explored[y * columns + x];
        }

        public uint Overlay(V2 point)
        {
            float light = Light(point), explored = ExploredBlend == null ? 0 : Sample(ExploredBlend, point);
            bool unknown = Hardcore && explored <= 0;
            float alpha = Hardcore ? 1 - explored * (1 - .63f * (1 - light)) : .63f * (1 - light), danger = 0;
            foreach (var patch in Danger)
            {
                float d = V2.Distance(point, patch.Position) / patch.Radius;
                if (d < 1)
                    danger = Math.Max(danger, patch.Strength * (1 - d * d) * (1 - d * d) * (1 - light));
            }

            // A reported sound may tint black terrain; opaque alpha still hides the terrain itself.
            int red = (int)(255 * danger), green = unknown ? 0 : (int)(255 * .015f * (1 - danger)), blue = unknown ? 0 : (int)(255 * .025f * (1 - danger)), a = (int)(255 * Math.Max(alpha, danger));
            return (uint)(red | (green << 8) | (blue << 16) | (a << 24));
        }

        public static bool Observer(Soldier s) => s.Alive && s.Deployed && (!s.Incapacitated || CareExperiments.Conscious(s));
        public static bool Sees(BattleSimulation b, Soldier s, V2 p) => DirectionalVision.InView(b, s, p) && b.Map.LineOfSight(s.Position, p) && b.SmokeTransmission(s.Position, p) >= .25f;
        public static float SmokeRadius(BattleSimulation b, SmokeCloud smoke) => smoke.Radius * (.35f + .65f * Util.Clamp((b.Time - smoke.Born) / 1.8f, 0, 1));
        // Only the near-facing half-cell rim is presentation-visible. No contact is created.
        public static bool SmokeRim(BattleSimulation b, Soldier observer, V2 p)
        {
            if (b.Smoke.Count == 0)
                return false;
            bool checkedView = false;
            foreach (var smoke in b.Smoke)
            {
                if (smoke.Until <= b.Time)
                    continue;
                float radius = SmokeRadius(b, smoke);
                V2 offset = p - smoke.Position;
                if (offset.Length > radius || V2.Dot(offset, observer.Position - smoke.Position) <= 0)
                    continue;
                if (!checkedView)
                {
                    if (!DirectionalVision.InView(b, observer, p) || !b.Map.LineOfSight(observer.Position, p))
                        return false;
                    checkedView = true;
                }

                V2 ray = p - observer.Position;
                float distance = ray.Length;
                if (distance < .001f)
                    return true;
                V2 front = p - ray / distance * .5f;
                if (Sees(b, observer, front))
                    return true;
            }

            return false;
        }

        public bool SmokeVisible(BattleSimulation b, SmokeCloud smoke)
        {
            if (!Enabled)
                return true;
            foreach (var observer in b.Soldiers)
                if (observer.Side == Side.Green && Observer(observer))
                {
                    V2 delta = observer.Position - smoke.Position;
                    float distance = delta.Length;
                    V2 near = distance > .001f ? smoke.Position + delta / distance * SmokeRadius(b, smoke) : smoke.Position;
                    if (Sees(b, observer, near) || SmokeRim(b, observer, near))
                        return true;
                }

            return false;
        }

        public void Update(BattleSimulation b)
        {
            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.Visibility.Update"))
            {
                if (!Enabled || b.Time < NextUpdate)
                    return;
                float dt = NextUpdate < 0 ? .2f : Math.Max(.01f, b.Time - (NextUpdate - .2f));
                NextUpdate = b.Time + .2f;
                if (Clear == null || columns != b.Map.Columns || rows != b.Map.Rows)
                {
                    columns = b.Map.Columns;
                    rows = b.Map.Rows;
                    Clear = new float[columns * rows];
                    Explored = new bool[Clear.Length];
                    ExploredBlend = new float[Clear.Length];
                    Visible.Clear();
                    Ghosts.Clear();
                }

                observers.Clear();
                foreach (var s in b.Soldiers)
                    if (s.Side == Side.Green && Observer(s))
                        observers.Add(s);
                if (lit == null || lit.Length != Clear.Length)
                {
                    lit = new bool[Clear.Length];
                    Current = new bool[Clear.Length];
                }
                else
                {
                    Array.Clear(lit, 0, lit.Length);
                    Array.Clear(Current, 0, Current.Length);
                }

                foreach (var s in observers)
                {
                    int radius = (int)Math.Ceiling(DirectionalVision.FrontRange(b, s));
                    for (int y = Math.Max(0, (int)s.Position.Y - radius); y < Math.Min(rows, (int)s.Position.Y + radius + 1); y++)
                        for (int x = Math.Max(0, (int)s.Position.X - radius); x < Math.Min(columns, (int)s.Position.X + radius + 1); x++)
                        {
                            int i = y * columns + x;
                            V2 point = new V2(x + .5f, y + .5f);
                            if (!Current[i] && Sees(b, s, point))
                                Current[i] = true;
                            if (Current[i] || (!lit[i] && SmokeRim(b, s, point)))
                                lit[i] = true;
                        }
                }

                for (int i = 0; i < Clear.Length; i++)
                {
                    if (lit[i])
                    {
                        Clear[i] = Math.Min(1, Clear[i] + dt * 4);
                        Explored[i] = true;
                        ExploredBlend[i] = 1;
                    }
                    else
                        Clear[i] = Math.Max(0, Clear[i] - dt * .5f);
                }

                Visible.Clear();
                known.Clear();
                foreach (var s in observers)
                {
                    foreach (var pair in s.Contacts)
                    {
                        var ct = pair.Value;
                        if (b.Time - ct.SeenAt > b.Config.ContactLifetime || ct.Confidence <= .05f)
                            continue;
                        if (!known.TryGetValue(pair.Key, out var old) || ct.SeenAt > old.SeenAt)
                            known[pair.Key] = ct;
                        if (s.Visible.Contains(pair.Key) && ct.Visual && !ct.Inferred && !ct.Reported && ct.Confidence >= .999f && b.Time - ct.SeenAt <= b.Config.PerceptionPeriod * 2 && pair.Key >= 0 && pair.Key < b.Soldiers.Count && Sees(b, s, b.Soldiers[pair.Key].Position))
                            Visible.Add(pair.Key);
                    }

                    foreach (var e in b.Soldiers)
                        if (e.Side == Side.Red && !e.Alive && e.Deployed && Sees(b, s, e.Position))
                            Visible.Add(e.Id);
                }

                foreach (var ct in b.Green.Contacts.Values)
                    if (b.Time - ct.SeenAt <= b.Config.ContactLifetime && ct.Confidence > .05f && (!known.TryGetValue(ct.EnemyId, out var old) || ct.SeenAt > old.SeenAt))
                        known[ct.EnemyId] = ct;
                foreach (int id in Visible)
                {
                    var unit = b.Soldiers[id];
                    if (unit.Alive)
                    {
                        KnownSilhouette ghost;
                        if (!Ghosts.TryGetValue(id, out ghost))
                        {
                            ghost = new KnownSilhouette
                            {
                                Id = id
                            };
                            Ghosts[id] = ghost;
                        }

                        ghost.Position = unit.Position;
                        ghost.SeenAt = b.Time;
                    }
                    else
                        Ghosts.Remove(id);
                }

                remove.Clear();
                foreach (var pair in Ghosts)
                    if (!Visible.Contains(pair.Key) && (!known.ContainsKey(pair.Key) || b.Time - pair.Value.SeenAt > b.Config.ContactLifetime))
                        remove.Add(pair.Key);
                foreach (int id in remove)
                    Ghosts.Remove(id);
                Danger.Clear();
                foreach (var ct in known.Values)
                    if (!Visible.Contains(ct.EnemyId))
                    {
                        float age = Math.Max(0, b.Time - ct.SeenAt);
                        float radius = Math.Max(ct.Uncertainty, ct.Inferred || !ct.Visual ? 3 : 1) + Math.Min(6, age * .35f);
                        Danger.Add(new DangerPatch { Position = ct.Position, Radius = radius, Strength = Util.Clamp(ct.Confidence * (1 - age / b.Config.ContactLifetime), 0, 1) * .4f });
                    }

                foreach (var noise in b.Green.Observation.NoiseAreas)
                {
                    float age = b.Time - noise.SeenAt;
                    if (age <= 12)
                        Danger.Add(new DangerPatch { Position = noise.Position, Radius = 4.25f, Strength = .25f * (1 - age / 12) });
                }
            }
        }
    }
}
