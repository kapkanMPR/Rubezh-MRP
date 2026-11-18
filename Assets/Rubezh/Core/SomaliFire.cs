using System;

namespace SilentOrders.Core
{
    // Blind fire from shelter. Only hands/weapon move; body position never changes.
    public static class SomaliFire
    {
        // Legacy default for future battles only.
        public static bool Enabled = true;
        public static float MuzzleHeight(Soldier s) => s.SomaliLow ? 2.1f : 1.55f;
        public static bool LowCoverAhead(BattleSimulation b, Soldier s, V2 point, out V2 cover)
        {
            cover = new V2();
            V2 dir = (point - s.Position).Normalized;
            if (dir.Length < .01f)
                return false;
            for (float d = .2f; d <= 1.55f; d += .15f)
            {
                V2 p = s.Position + dir * d;
                var tile = b.Map.At(p);
                if (tile == Tile.Wall)
                    return false;
                if (tile == Tile.LowCover)
                {
                    cover = p;
                    return true;
                }
            }

            return false;
        }

        public static bool Pose(BattleSimulation b, Soldier s, V2 target, out V2 muzzle, out bool low)
        {
            muzzle = s.Position;
            low = false;
            if (LowCoverAhead(b, s, target, out var cover))
            {
                if (!s.Crouched && !s.Crouch.Seated)
                    return false;
                low = true;
                muzzle = s.Position + (target - s.Position).Normalized * .4f;
                return b.Map.LineOfSight(muzzle, target);
            }

            // Reach only an exposed wall corner, never shoot through a wall or move the torso.
            float best = 2f;
            bool found = false;
            int px = (int)Math.Floor(s.Position.X), py = (int)Math.Floor(s.Position.Y);
            for (int y = py - 1; y <= py + 1; y++)
                for (int x = px - 1; x <= px + 1; x++)
                {
                    if (b.Map.At(new V2(x + .5f, y + .5f)) != Tile.Wall)
                        continue;
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sx = -1; sx <= 1; sx += 2)
                        {
                            V2 corner = new V2(x + (sx > 0 ? 1 : 0), y + (sy > 0 ? 1 : 0));
                            // Two open adjoining cells distinguish a real end/corner from a long wall.
                            if (!b.Map.Walkable(x + (sx > 0 ? 1 : -1), y) || !b.Map.Walkable(x, y + (sy > 0 ? 1 : -1)))
                                continue;
                            V2 p = corner + new V2(sx * .18f, sy * .18f);
                            V2 extended = s.Position + (p - s.Position).Normalized * .7f;
                            if (b.Map.Walkable(extended) && b.Map.LineOfSight(s.Position, extended) && b.Map.LineOfSight(extended, target))
                                p = extended;
                            float reach = V2.Distance(s.Position, p);
                            if (reach > .95f || reach < .12f || reach >= best || !b.Map.Walkable(p) || !b.Map.LineOfSight(s.Position, p) || !b.Map.LineOfSight(p, target))
                                continue;
                            if (b.Map.Protection(s.Position, target) < .5f && !WallCornerCover.At(b.Map, s.Position, target))
                                continue;
                            best = reach;
                            muzzle = p;
                            found = true;
                        }
                }

            return found;
        }

        public static bool Ready(BattleSimulation b, Soldier s)
        {
            return b.Rules.SomaliFire && s != null && CombatTraining.Skill(s, 3, "blind") && s.Side == Side.Green && s.Available && s.Deployed && !s.Broken && !s.GunSniper && !HandLauncher.Active(s) && !s.Moving && !s.Motion.Fast && !MotionCombat.InMotion(b, s) && !s.Busy && !PairExchange.Active(b, s) && !CombatCrouch.Standing(b, s) && s.HandsBusyUntil <= b.Time && s.ReloadUntil <= b.Time && s.Suppression < .95f && s.Health > 20 && s.Ammo >= (s.SomaliRemaining > 0 ? 1 : 8) && b.Time >= s.SomaliNext;
        }

        // The same gate is used before crouching and before every shot.
        public static bool Necessary(BattleSimulation b, Soldier s, Contact c)
        {
            if (c == null || c.Confidence < .5f || c.SeenAt > b.Time || b.Time - c.SeenAt > 3) return false;
            float distance = V2.Distance(s.Position, c.Position);
            if (distance < 3 || distance > Math.Min(12f, b.WeaponRange(s))) return false;
            return s.Suppression >= .45f || (b.Time - s.LastIncoming <= 2.5f && s.LastIncoming <= b.Time);
        }

        public static bool CanAttempt(BattleSimulation b, Soldier s, Contact c)
        {
            if (!Necessary(b, s, c) || !Ready(b, s))
                return false;
            float d = V2.Distance(s.Position, c.Position);
            if (d < 3 || d > b.WeaponRange(s))
                return false;
            return Pose(b, s, c.Position, out var muzzle, out var low) && Safe(b, s, muzzle, c.Position);
        }

        public static bool Safe(BattleSimulation b, Soldier s, V2 muzzle, V2 target)
        {
            // Do not replace the crouch restriction with a friendly-fire loophole.
            foreach (var ally in b.Soldiers)
                if (ally.Id != s.Id && ally.Side == s.Side && ally.Alive && ally.Deployed && V2.SegmentDistance(ally.Position, muzzle, target) < .8f)
                    return false;
            return true;
        }

        public static void PreparePosture(BattleSimulation b, Soldier s)
        {
            if (!b.Rules.SomaliFire || !CombatTraining.Skill(s, 3, "blind") || s.Side != Side.Green || s.Moving || s.Motion.Fast || s.Busy || s.GunSniper || s.ReloadUntil > b.Time || s.Ammo < 8 || CombatCrouch.Standing(b, s) || s.MoveAuthorized && MotionCombat.HasRoute(s))
                return;
            foreach (var c in s.Contacts.Values)
                if (Necessary(b, s, c) && LowCoverAhead(b, s, c.Position, out var cover))
                {
                    s.Crouched = true;
                    if (!CombatCrouch.Automatic(s))
                    {
                        s.Crouch.Seated = true;
                        s.Crouch.StandUntil = 0;
                    }

                    return;
                }
        }

        public static Contact SelectTarget(BattleSimulation b, Soldier s, Contact normal)
        {
            if (normal != null && CanAttempt(b, s, normal))
                return normal;
            if (normal != null)
                return normal;
            Contact best = null;
            foreach (var c in s.Contacts.Values)
                if (CanAttempt(b, s, c) && (best == null || c.SeenAt > best.SeenAt))
                    best = c;
            return best;
        }

        public static bool Activate(BattleSimulation b, Soldier s, Contact c)
        {
            if (!CanAttempt(b, s, c))
                return false;
            Pose(b, s, c.Position, out s.SomaliMuzzle, out s.SomaliLow);
            if (s.SomaliRemaining <= 0 || b.Time > s.SomaliUntil)
            {
                s.SomaliRemaining = 10;
                s.SomaliStarts++;
                b.Stats.SomaliStarts++;
                s.SomaliUntil = b.Time + 2;
            }

            s.SomaliFiring = true;
            s.SomaliTarget = c.Position;
            return true;
        }

        public static void Finish(BattleSimulation b, Soldier s)
        {
            if (s.SomaliFiring && --s.SomaliRemaining <= 0)
                s.SomaliNext = b.Time + 2;
            s.SomaliFiring = false;
        }

        public static bool Visual(Soldier s, float time) => s.Rules.SomaliFire && s.Side == Side.Green && s.Alive && !s.Moving && time < s.SomaliUntil && time - s.LastFire < .28f;
        public static void Modifiers(ref float quality, ref float damage, ref float suppression, ref float morale)
        {
            quality *= .12f;
        }
    }
}
