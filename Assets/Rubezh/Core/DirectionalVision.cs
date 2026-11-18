using System;

namespace SilentOrders.Core
{
    public sealed class AwarenessState
    {
        public readonly SurpriseBurstState Surprise = new SurpriseBurstState();
        public readonly ContactMoveState ContactMove = new ContactMoveState();
        public readonly ReactionState Reaction = new ReactionState();
        public float MarchRouteUntil, ContactSprintUntil;
        public V2 Look, AlertPosition, Watch;
        public bool Initialized, Focused;
        public int SuppressedKnownSounds, FocusTarget = -1, FocusStarts, FocusBreaks, Scans, HeardAlerts, RearDetections, SideDetections;
        public float FocusWorkedAt = -100;
        public float UpdatedAt, NextScan, ScanUntil, ScanAngle, AlertUntil, LastHeard = -100, FocusSince, FocusLastSeen, FocusBlockedUntil, Health;
    }

    public static class DirectionalVision
    {
        const float Rad = (float)Math.PI / 180f;
        public static float FrontRange(BattleSimulation b, Soldier s)
        {
            if (s.Incapacitated)
                return b.Config.SightRange * .6f * SuppressionRange(b, s);
            float range = s.Sniper ? Math.Max(b.Config.SightRange, b.WeaponRange(s)) : b.Config.SightRange + (s.Role == Role.MachineGunner ? 6f : s.Role == Role.Grenadier ? 9f : 2f);
            // Kedr owns these binoculars personally; successors retain their own optics.
            if (s.Side == Side.Green)
            {
                if (s.Role == Role.Commander)
                    range = 16 + (s.ClassRank >= 3 ? 4 : s.ClassRank);
                else if (s.Role == Role.Sniper)
                    range = 20 + s.ClassRank;
                else if (s.Role == Role.Rifleman && s.GunSniper)
                    range = 16 + 2 * Util.Clamp((b.Time - s.ScopeSince - 2) / 2, 0, 1);
            }

            // Experimental minimum for both sides; keep original role optics and suppression.
            range = Math.Max(range, 17f);
            return range * SuppressionRange(b, s);
        }

        public static float SuppressionAngle(BattleSimulation b, Soldier s) => !b.SuppressionVisionEnabled || b.Historical01225 ? 1 : 1 - .35f * Util.Clamp(s.Suppression, 0, 1);
        public static float SuppressionRange(BattleSimulation b, Soldier s) => !b.SuppressionVisionEnabled || b.Historical01225 ? 1 : 1 - .15f * Util.Clamp(s.Suppression, 0, 1);
        static float Lerp(float a, float z, float t) => a + (z - a) * Util.Clamp(t, 0, 1);
        public static float Multiplier(float angle, bool focus)
        {
            angle = Math.Abs(angle);
            if (focus)
            {
                if (angle <= 20)
                    return 1;
                if (angle < 70)
                    return Lerp(1, .25f, (angle - 20) / 50);
                if (angle <= 110)
                    return .25f;
                if (angle < 140)
                    return Lerp(.25f, .15f, (angle - 110) / 30);
                return .15f;
            }

            if (angle <= 50)
                return 1;
            if (angle < 70)
                return Lerp(1, .75f, (angle - 50) / 20);
            if (angle <= 110)
                return .75f;
            if (angle < 130)
                return Lerp(.75f, .45f, (angle - 110) / 20);
            return .45f;
        }

        public static float Angle(V2 a, V2 z)
        {
            if (a.Length < .01f || z.Length < .01f)
                return 0;
            return (float)Math.Acos(Util.Clamp(V2.Dot(a.Normalized, z.Normalized), -1, 1)) / Rad;
        }

        static V2 Rotate(V2 a, float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new V2(a.X * c - a.Y * s, a.X * s + a.Y * c);
        }

        public static bool PeripheralFocus(BattleSimulation b) => b.PeripheralFocusEnabled && !b.Historical01225;
        public static float RangeAtAngle(BattleSimulation b, Soldier s, float degrees) => RangeAtAngle(b, s, degrees, FrontRange(b, s), DefenderRest.Multiplier(b, s));
        static float RangeAtAngle(BattleSimulation b, Soldier s, float degrees, float front, float rest)
        {
            float actual = Math.Abs(degrees), angle = Math.Min(180, actual / SuppressionAngle(b, s));
            if (!PeripheralFocus(b))
                return Math.Max(3f, front * Multiplier(angle, s.Awareness.Focused)) * rest;
            float range = Math.Max(3f, front * Multiplier(angle, false));
            // Existing directional regions: side 70..110, rear >=130. Smooth transition.
            float rear = Util.Clamp((actual - 110) / 20, 0, 1);
            range = Lerp(range, Math.Min(range, 4f * SuppressionRange(b, s)), rear);
            // Apply AFTER the proximity floor, otherwise the requested 2-cell rear view is impossible.
            if (s.Awareness.Focused)
                range *= Lerp(1, .5f, (angle - 50) / 20);
            return range * rest;
        }

        public static bool InView(BattleSimulation b, Soldier s, V2 point) => InView(b, s, point, V2.Distance(point, s.Position), FrontRange(b, s));
        internal static bool InView(BattleSimulation b, Soldier s, V2 point, float distance, float front)
        {
            // Resolve alert side effects even for an out-of-range target.
            float rest = DefenderRest.Multiplier(b, s);
            if (distance > Math.Max(3f, front) * rest || DefenderRest.LowCoverBlocks(b, s, point, distance, rest))
                return false;
            V2 delta = point - s.Position;
            float angle = Angle(s.Awareness.Initialized ? s.Awareness.Look : s.Facing, delta);
            // Only a nearby window crossed by this sight ray extends the lateral sector.
            bool window = false;
            if (angle > 18 && angle < 115 && b.Map.Studio != null)
            {
                var ray = delta.Normalized;
                for (float d = .25f; d <= 1.5f && d < distance; d += .25f)
                {
                    var at = s.Position + ray * d;
                    int wx = (int)Math.Floor(at.X), wy = (int)Math.Floor(at.Y);
                    if (!b.Map.Inside(wx, wy))
                        break;
                    var tile = b.Map.At(at);
                    if (tile == Tile.Wall)
                        break;
                    if (tile == Tile.LowCover)
                    {
                        window = RoofRules.Window(StudioRules.At(b.Map, wy * b.Map.Columns + wx));
                        break;
                    }
                }

                if (window)
                    angle -= 6f;
            }

            if (!PeripheralFocus(b))
            {
                float old = front * Multiplier(Math.Min(180, angle / SuppressionAngle(b, s)), s.Awareness.Focused);
                if ((s.Crouched || s.Crouch.Seated) && SomaliFire.LowCoverAhead(b, s, point, out var oldCover))
                    old *= .5f;
                return distance <= Math.Max(3f, old) * rest;
            }

            float range = RangeAtAngle(b, s, angle, front, rest);
            if ((s.Crouched || s.Crouch.Seated) && SomaliFire.LowCoverAhead(b, s, point, out var cover))
                range *= .5f;
            return distance <= range;
        }

        public static void WorkOnTarget(BattleSimulation b, Soldier s, Contact c)
        {
            if (!PeripheralFocus(b) || c == null || !s.Available || s.Broken || s.Motion.Fast || s.ReloadUntil > b.Time || s.Suppression > .65f || s.GrenadeEscape.Active || b.Time < s.Awareness.FocusBlockedUntil)
                return;
            if (s.GunSniper && MotionCombat.InMotion(b, s))
                return;
            if (!s.Visible.Contains(c.EnemyId) || !c.Visual || c.Reported || c.Inferred || b.Time - c.SeenAt > b.Config.PerceptionPeriod * 2 || !b.Map.LineOfSight(s.Position, c.Position) || b.SmokeTransmission(s.Position, c.Position) < .25f)
                return;
            var a = s.Awareness;
            if (!a.Focused || a.FocusTarget != c.EnemyId)
            {
                a.Focused = true;
                a.FocusTarget = c.EnemyId;
                a.FocusSince = b.Time;
                a.FocusStarts++;
            }

            a.FocusLastSeen = c.SeenAt;
            a.FocusWorkedAt = b.Time;
        }

        public static void BreakFocus(BattleSimulation b, Soldier s)
        {
            var a = s.Awareness;
            if (a.Focused)
                a.FocusBreaks++;
            a.Focused = false;
            a.FocusTarget = -1;
            a.FocusBlockedUntil = b.Time + 1.2f;
        }

        public static void Hear(BattleSimulation b, Soldier s, V2 source, float shotAt)
        {
            var a = s.Awareness;
            if (shotAt <= a.LastHeard || b.Time - shotAt > b.Config.PerceptionPeriod || V2.Distance(source, s.Position) > 18)
                return;
            foreach (var c in s.Contacts.Values)
                if (c.Visual && !c.Reported && !c.Inferred && b.Time - c.SeenAt < 1.2f && V2.Distance(c.Position, source) < 1)
                {
                    a.LastHeard = shotAt;
                    a.SuppressedKnownSounds++;
                    return;
                }

            if (a.AlertUntil > b.Time + 1.2f)
                return;
            a.LastHeard = shotAt;
            a.HeardAlerts++;
            DefenderRest.Confirm(b, s, "heard_shot");
            a.AlertPosition = new V2((float)Math.Floor(source.X / 6) * 6 + 3, (float)Math.Floor(source.Y / 6) * 6 + 3);
            a.AlertUntil = b.Time + 2;
            b.Team(s).Observation.ReportNoise(b, source);
            if (a.Focused && Angle(a.Look, a.AlertPosition - s.Position) > 60)
                BreakFocus(b, s);
        }

        public static void Update(BattleSimulation b, Soldier s)
        {
            if (!s.ScopeTracking || s.Moving || V2.Distance(s.ScopeAnchor, s.Position) > .015f || !s.GunSniper)
            {
                s.ScopeSince = b.Time;
                s.ScopeAnchor = s.Position;
                s.ScopeTracking = true;
            }

            var a = s.Awareness;
            if (s.ReloadUntil > b.Time || !s.Available)
                SurpriseBurst.Interrupt(s);
            if (!a.Initialized)
            {
                a.Initialized = true;
                a.Look = s.Facing.Length > .1f ? s.Facing : TacticalOrders.Axis(b, s.Side);
                a.Watch = a.Look;
                a.UpdatedAt = b.Time;
                a.Health = s.Health;
                a.NextScan = b.Time + 2 + (s.Id % 7) * .41f;
            }

            float dt = Math.Max(0, b.Time - a.UpdatedAt);
            a.UpdatedAt = b.Time;
            if (a.Focused && (!s.Available || s.Broken || (s.GunSniper || !PeripheralFocus(b) ? s.Moving : s.Motion.Fast) || s.ReloadUntil > b.Time || s.GrenadeEscape.Active || s.Health < a.Health || s.Suppression > .65f))
                BreakFocus(b, s);
            if (PeripheralFocus(b) && a.Focused && !s.GunSniper && b.Time - a.FocusWorkedAt > 3)
                BreakFocus(b, s);
            a.Health = s.Health;
            Contact focus = null;
            if (a.Focused)
            {
                if (s.Contacts.TryGetValue(a.FocusTarget, out focus) && focus.Visual && !focus.Reported && !focus.Inferred && b.Time - focus.SeenAt <= b.Config.PerceptionPeriod * 2)
                    a.FocusLastSeen = b.Time;
                else if (b.Time - a.FocusLastSeen > 1)
                    BreakFocus(b, s);
            }

            if (a.Watch.Length < .1f)
                a.Watch = a.Look.Length > .1f ? a.Look : s.Facing;
            if (s.Moving && V2.Distance(s.Position, s.Destination) > .2f)
                a.Watch = (s.Destination - s.Position).Normalized;
            if (b.Time - s.LastFire < .5f)
                a.Watch = s.Facing;
            V2 wanted = a.Watch;
            Contact threat = null;
            float score = float.MinValue;
            foreach (var c in Knowledge.Contacts(b, s))
                if (c.SeenAt <= b.Time && b.Time - c.SeenAt < 8 && c.Confidence > .35f)
                {
                    float value = c.Confidence * 4 - (b.Time - c.SeenAt) - V2.Distance(s.Position, c.Position) * .03f;
                    if (value > score)
                    {
                        score = value;
                        threat = c;
                    }

                    // A received warning may break tunnel vision, without revealing a hidden unit.
                    if (a.Focused && c.Reported && b.Time - c.SeenAt < 1 && c.EnemyId != a.FocusTarget && Angle(a.Look, c.Position - s.Position) > 80 && V2.Distance(s.Position, c.Position) < 10)
                        BreakFocus(b, s);
                }

            if (a.Focused && focus != null)
                wanted = (focus.Position - s.Position).Normalized;
            else if (a.AlertUntil > b.Time)
                wanted = (a.AlertPosition - s.Position).Normalized;
            else if (threat != null)
                wanted = (threat.Position - s.Position).Normalized;
            else if (!s.Moving && s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count && s.BuddyId != s.Id)
            {
                var buddy = b.Soldiers[s.BuddyId];
                if (buddy.Available && buddy.Awareness.Focused && V2.Distance(s.Position, buddy.Position) < 9)
                    wanted = Rotate(buddy.Awareness.Look, (s.Id % 2 == 0 ? 85 : -85) * Rad);
            }

            bool working = b.Time - s.LastFire < 3 || threat != null || a.Focused;
            if (!a.Focused && a.AlertUntil <= b.Time && b.Time >= a.NextScan)
            {
                a.NextScan = b.Time + (working ? 7f : 3.5f) + (s.Id % 5) * .33f;
                a.ScanUntil = b.Time + (working ? .45f : .75f);
                a.Scans++;
                a.ScanAngle = ((a.Scans + s.Id) % 4 == 0 ? 155f : 70f) * ((a.Scans + s.Id) % 2 == 0 ? 1 : -1);
                if (!working)
                {
                    var observation = b.Team(s).Observation;
                    float distance = Math.Min(8f, FrontRange(b,s)*.5f);
                    bool found = false;
                    for(int attempt=0;attempt<3;attempt++)
                    {
                        float angle = attempt==0 ? a.ScanAngle : attempt==1 ? -a.ScanAngle : 155f;
                        V2 point=s.Position+Rotate(wanted,angle*Rad)*distance;
                        if(observation.ClearedByMate(b,s,point)) continue;
                        a.ScanAngle=angle;found=true;break;
                    }
                    if(!found) { a.ScanUntil=b.Time; observation.AvoidedChecks++; }
                }
            }

            if (!a.Focused && a.AlertUntil <= b.Time && b.Time < a.ScanUntil)
                wanted = Rotate(wanted, a.ScanAngle * Rad);
            if (TacticalMarch.Watch(b, s, out var marchWatch) && threat == null && a.AlertUntil <= b.Time)
                wanted = marchWatch;
            if (PairTacticalPlan.Watch(b, s, out var planWatch) && threat == null && a.AlertUntil <= b.Time)
                wanted = planWatch;
            if (ContactMovement.Active(b, s) && !s.Motion.Fast)
                wanted = (s.Awareness.ContactMove.Center - s.Position).Normalized;
            if (s.Motion.Fast && MotionCombat.HasRoute(s))
                wanted = (s.Path[s.PathCursor] - s.Position).Normalized;
            if (QuietRecon.Watch(b, s, out var reconWatch) && !a.Focused && a.AlertUntil <= b.Time)
                wanted = reconWatch;
            if (ReconOpening.Watch(b, s, out var openingWatch) && a.AlertUntil <= b.Time)
                wanted = openingWatch;
            if (wanted.Length < .1f)
                wanted = a.Look;
            float turn = Angle(a.Look, wanted) * Rad, maxTurn = (s.Suppression > .6f ? 85f : 180f) * Rad * dt;
            float cross = a.Look.X * wanted.Y - a.Look.Y * wanted.X;
            if (turn <= maxTurn)
                a.Look = wanted.Normalized;
            else
                a.Look = Rotate(a.Look, (cross >= 0 ? 1 : -1) * maxTurn).Normalized;
        }

        public static bool ReadyToFire(BattleSimulation b, Soldier s, Contact c)
        {
            WorkOnTarget(b, s, c);
            if (!s.GunSniper)
                return true;
            var a = s.Awareness;
            if (MotionCombat.InMotion(b, s) || s.Motion.Fast || s.ReloadUntil > b.Time || s.Suppression > .65f || b.Time < a.FocusBlockedUntil)
                return false;
            if (!s.Visible.Contains(c.EnemyId) || !c.Visual || c.Inferred)
                return false;
            if (!a.Focused || a.FocusTarget != c.EnemyId)
            {
                a.Focused = true;
                a.FocusTarget = c.EnemyId;
                a.FocusSince = a.FocusLastSeen = b.Time;
                a.FocusStarts++;
            }

            s.FireStatus = "Сконцентрирован на цели";
            return b.Time - a.FocusSince >= .6f && Angle(a.Look, c.Position - s.Position) <= 20;
        }
    }
}
