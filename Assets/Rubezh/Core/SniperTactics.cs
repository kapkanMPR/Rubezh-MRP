using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class SniperTactics
    {
        public const float RangeBonus = 5f;
        // Increase over the v0.11.28.1 sniper bonus: 3.96 * 2.5.
        // The final probability is clamped in BattleSimulation.Accuracy.
        public const float AccuracyMultiplier = 3.96f * 2.5f;
        public const float FiringIntervalMultiplier = 2f;
        const float SearchRadius = 18f;
        const float PostHold = 7f;
        public static List<Contact> Known(BattleSimulation b, Soldier s)
        {
            var result = new Dictionary<int, Contact>();
            foreach (var c in Knowledge.Contacts(b, s))
                if (c.Visual && c.Confidence > .4f && b.Time - c.SeenAt < 7)
                    result[c.EnemyId] = c;
            return new List<Contact>(result.Values);
        }

        // A sniper assigned to a pair still operates on a distinct firing line,
        // but cannot treat a distant teammate as covering an entire map.
        public static Soldier Partner(BattleSimulation b, Soldier sniper)
        {
            if (sniper.BuddyId < 0 || sniper.BuddyId >= b.Soldiers.Count || sniper.BuddyId == sniper.Id)
                return null;
            var buddy = b.Soldiers[sniper.BuddyId];
            if (!buddy.Available || buddy.Broken || buddy.Side != sniper.Side || V2.Distance(sniper.Position, buddy.Position) > 9.5f)
                return null;
            var q = b.Team(sniper);
            if (TacticalOrders.Flanks(q) && q.FlankPlan.Active && FlankTactics.SideOf(q, sniper.Id) != FlankTactics.SideOf(q, buddy.Id))
                return null;
            return buddy;
        }

        public static bool BuddyCanCover(BattleSimulation b, Soldier buddy)
        {
            return buddy != null && !buddy.Moving && buddy.Ammo > 0 && buddy.Suppression < .65f && buddy.ReloadUntil <= b.Time && !buddy.Broken;
        }

        // An empty gun cannot lead an armed bound. Evaluate the current roster, not the
        // nominal number of survivors; an ally who reloads or picks up ammunition counts again.
        public static bool MobileLead(BattleSimulation b, Soldier s)
        {
            if (!s.Sniper || s.Side != Side.Green || !s.Deployed || !s.Available || s.Broken || s.Ammo + s.Reserve <= 0)
                return false;
            var q = b.Team(s);
            if (q.Style != CombatStyle.Balanced && q.Style != CombatStyle.Aggressive)
                return false;
            foreach (var u in b.Soldiers)
                if (u != s && q.Contains(u) && u.Deployed && u.Available && !u.Broken && u.Role != Role.Medic && u.Ammo + u.Reserve > 0)
                    return false;
            return true;
        }

        public static bool OwnsSpecialPost(BattleSimulation b, Soldier s)
        {
            return s.Sniper && (!MobileLead(b, s) || s.SniperRetreating || s.SniperRetreatUntil > b.Time || ExecutionReview.WorkingSector(b, s));
        }

        public const float PairPostLimit = 7f;
        public static float ThreatRange(BattleSimulation b, Contact c)
        {
            return c.TargetPriority == 0 ? SpecialWeapons.Range(b) + RangeBonus : c.TargetPriority == 1 ? b.Config.WeaponRange + 4 : c.TargetPriority == 2 ? SpecialWeapons.Range(b) : b.Config.WeaponRange;
        }

        public static bool ClearLane(BattleSimulation b, Soldier s, V2 from, V2 target)
        {
            V2 line = target - from;
            float len = line.Length;
            if (len < .1f)
                return false;
            V2 dir = line / len;
            foreach (var ally in b.Soldiers)
            {
                if (ally.Id == s.Id || ally.Side != s.Side || !ally.Alive || !ally.Deployed)
                    continue;
                float along = V2.Dot(ally.Position - from, dir);
                if (along > 0 && along < len && V2.Distance(ally.Position, from + dir * along) < .65f)
                    return false;
            }

            return true;
        }

        public static bool OpenSafe(BattleSimulation b, V2 p, List<Contact> known, float margin)
        {
            foreach (var c in known)
                if (V2.Distance(p, c.Position) < ThreatRange(b, c) + margin && b.Map.LineOfSight(p, c.Position) && b.SmokeTransmission(p, c.Position) > .3f)
                    return false;
            return true;
        }

        static bool Occupied(BattleSimulation b, Soldier s, V2 p)
        {
            foreach (var u in b.Soldiers)
                if (u.Id != s.Id && u.Alive && u.Deployed && (V2.Distance(p, u.Position) < 1.3f || u.Side == s.Side && V2.Distance(p, u.OrderAnchor) < 1.3f))
                    return true;
            return false;
        }

        public static bool CanShootFrom(BattleSimulation b, Soldier s, V2 point, Contact c)
        {
            // Uses known/visual reports and the same wall, cover, smoke and friendly-fire
            // checks as the shooting model. A clear geometric line alone is insufficient.
            return c.Visual && PositionLogic.CanEngage(b, s, point, c) && ClearLane(b, s, point, c.Position);
        }

        public static bool RetreatSmoke(BattleSimulation b, Soldier s, Contact threat)
        {
            return SmokeTactics.Request(b, b.Team(s), s, s.OrderAnchor, SmokePurpose.Withdrawal, s);
        }

        // Advance with the squad toward a first firing/observation post when there
        // are no reported targets. Movement is small, cover-first and time throttled.
        static bool SeekObservationPost(BattleSimulation b, Soldier s, V2 groupCenter, bool alone)
        {
            var partner = MobileLead(b, s) ? null : Partner(b, s);
            // No target/sector exists here: waiting to "cover" cannot help the partner.
            if (s.PathCursor < s.Path.Count && s.SniperMoving)
            {
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                return true;
            }

            if (b.Time < s.NextSniperPost)
                return false;
            var axis = TacticalOrders.Axis(b, s.Side);
            V2 entry = s.Side == Side.Green ? b.Map.Entry : b.Map.Objective;
            V2 objective = s.Side == Side.Green ? b.Map.Objective : b.Map.Entry;
            float myProgress = V2.Dot(s.Position - entry, axis);
            float frontProgress = myProgress;
            foreach (var ally in b.Soldiers)
                if (ally.Id != s.Id && b.Team(s).Contains(ally) && ally.Available && ally.Deployed)
                {
                    float position = V2.Dot(ally.Position - entry, axis);
                    if (position > frontProgress)
                        frontProgress = position;
                }

            float missionProgress = V2.Dot(objective - entry, axis);
            // A lone survivor has no friendly forward line to follow. Probe a short
            // distance toward the objective instead of treating his current position
            // as the entire group's front and waiting forever.
            float wantedProgress = alone ? Math.Min(missionProgress - .8f, myProgress + 6f) : Math.Min(missionProgress - 5f, Math.Max(3.5f, frontProgress - 2f));
            // A sniper does not outrun the group repeatedly or drift around the spawn.
            if (myProgress >= wantedProgress - 1.25f)
            {
                s.NextSniperPost = b.Time + 3.5f;
                return false;
            }

            float advance = Math.Min(partner != null ? 4f : 6f, wantedProgress - myProgress);
            var side = new V2(-axis.Y, axis.X);
            V2[] offsets =
            {
                new V2(),
                side * 2f,
                side * -2f,
                side * 3.5f,
                side * -3.5f
            };
            float best = float.MinValue;
            List<V2> bestPath = null;
            V2 bestPost = s.Position;
            foreach (var offset in offsets)
            {
                var post = TacticalOrders.Open(b, s.Position + axis * advance + offset);
                if (!b.Map.Walkable(post) || Occupied(b, s, post) || V2.Distance(post, s.Position) < 1.5f || partner != null && V2.Distance(post, partner.Position) > PairPostLimit)
                    continue;
                var cover = PositionLogic.Protected(b, s, post);
                float score = V2.Dot(post - s.Position, axis) * 1.4f + (cover ? 5f : 0f) - PositionLogic.Exposure(b, s, post) * (alone ? 6f : 4f) - (alone ? 0f : V2.Distance(post, groupCenter) * .15f);
                if (score <= best)
                    continue;
                var path = LossMemory.Find(b, s, post, p => TacticalAI.Risk(b, s, p) * 2f);
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > (partner != null ? 7f : 10f))
                    continue;
                best = score;
                bestPath = path;
                bestPost = post;
            }

            s.NextSniperPost = b.Time + 4f;
            if (bestPath == null)
                return false;
            s.Path = bestPath;
            s.PathCursor = 0;
            s.LastMoved = b.Time;
            s.Destination = s.OrderAnchor = bestPost;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.WasAtAnchor = false;
            s.SniperMoving = true;
            s.SniperRetreating = false;
            s.Order = "Снайпер: занимаю наблюдательный рубеж";
            s.Reason = "Иду за отделением; ищу укрытие и будущий сектор стрельбы";
            return true;
        }

        public static bool Plan(BattleSimulation b, Soldier s)
        {
            if (!s.Sniper || !s.Available || s.Broken)
                return false;
            var q = b.Team(s);
            var known = Known(b, s);
            V2 center = new V2();
            int allies = 0;
            foreach (var u in b.Soldiers)
                if (u.Id != s.Id && b.Team(s).Contains(u) && u.Available && u.Deployed)
                {
                    center += u.Position;
                    allies++;
                }

            bool alone = allies == 0 || MobileLead(b, s);
            var partner = MobileLead(b, s) ? null : Partner(b, s);
            if (alone)
                center = s.Position;
            else
                center /= allies;
            if (partner != null)
                center = partner.Position;
            Contact closest = null;
            float gap = float.MaxValue;
            foreach (var c in known)
            {
                float g = V2.Distance(s.Position, c.Position) - ThreatRange(b, c);
                if (g < gap)
                {
                    gap = g;
                    closest = c;
                }
            }

            // A contact inside the opponent's weapon range is NOT automatically a
            // reason to run: keep shooting from a viable post, particularly in cover.
            var currentShot = TacticalAI.FireTarget(b, s);
            bool usableNow = currentShot != null && CanShootFrom(b, s, s.Position, currentShot);
            bool incoming = b.Time - s.LastIncoming < 3f;
            bool coverHere = PositionLogic.Protected(b, s, s.Position);
            float distance = closest != null ? V2.Distance(s.Position, closest.Position) : float.MaxValue;
            bool danger = closest != null && (distance < 3.5f && !coverHere || incoming && (s.Suppression > .55f || s.Health < 35f) && !coverHere);
            if (usableNow && !danger)
            {
                s.SniperRetreatUntil = 0f;
                s.SniperRetreating = false;
            }

            bool retreat = danger || b.Time < s.SniperRetreatUntil || q.Style == CombatStyle.Withdraw;
            if (danger)
            {
                s.SniperRetreatUntil = b.Time + 4f;
                RetreatSmoke(b, s, closest);
            }

            if (q.Style == CombatStyle.Withdraw)
            {
                RetreatSmoke(b, s, closest);
                return false;
            }

            if (s.ReloadUntil > b.Time && !retreat)
                return false;
            // Cover a moving buddy only when this position actually provides a shot.
            if (partner != null && usableNow && (partner.Moving || partner.PathCursor < partner.Path.Count) && !retreat)
            {
                if (s.SniperMoving)
                {
                    TacticalAI.Stop(s);
                    s.SniperMoving = false;
                }

                s.Job = Job.Cover;
                s.MoveAuthorized = true;
                s.OrderAnchor = s.Position;
                s.Reason = "Снайпер: держу сектор, напарник перебегает";
                return true;
            }

            // Never wander between arbitrary "safe" positions without a reported target.
            // Let ordinary group coordination handle regrouping in that case.
            if (known.Count == 0)
            {
                if (!retreat && SeekObservationPost(b, s, center, alone))
                    return true;
                if (s.SniperMoving && s.PathCursor < s.Path.Count)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                s.SniperMoving = false;
                return false;
            }

            // If a firing line exists, use it. Cover increases the value of holding the
            // post but is not required to shoot (e.g. in a cleared open field).
            bool usable = usableNow;
            bool reportedLine = false;
            if (!usable)
                foreach (var c in known)
                    if (CanShootFrom(b, s, s.Position, c))
                    {
                        reportedLine = true;
                        break;
                    }

            bool idleRadioLine = reportedLine && !usable && b.Time - s.LastFire > 6f;
            if (!retreat && (usable || reportedLine && !idleRadioLine))
            {
                if (s.SniperMoving)
                    TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.SniperMoving = false;
                s.NextSniperPost = Math.Max(s.NextSniperPost, b.Time + PostHold);
                s.Reason = usable ? (b.Map.NearCover(s.Position) ? "Снайпер: работаю по цели из укрытия" : "Снайпер: вижу цель, веду огонь") : "Снайпер: держу прямую линию, жду подтверждения цели";
                return true;
            }

            if (partner != null && usableNow && !retreat && !BuddyCanCover(b, partner))
            {
                if (s.SniperMoving)
                {
                    TacticalAI.Stop(s);
                    s.SniperMoving = false;
                }

                s.Job = Job.Cover;
                s.OrderAnchor = s.Position;
                s.Reason = "Снайпер: напарник не готов, держу позицию";
                return true;
            }

            // Committed routes should not be rewritten every decision; suppression can
            // interrupt travel normally, but a genuinely dangerous position may replan.
            if (s.SniperMoving && s.PathCursor < s.Path.Count && !danger)
            {
                if (b.Time - s.LastMoved < 2.6f)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                // The waypoint cannot be reached; abandon it instead of repeatedly
                // committing to the same blocked route.
                TacticalAI.Stop(s);
                s.SniperMoving = false;
                s.NextSniperPost = b.Time;
            }

            if (b.Time < s.NextSniperPost && !danger)
                return false;
            s.NextSniperPost = b.Time + (retreat ? 2f : 4f);
            var posts = new List<KeyValuePair<float, V2>>(120);
            float currentGap = closest == null ? 0 : V2.Distance(s.Position, closest.Position);
            bool stalePost = !usableNow && b.Time - s.LastFire > 8f;
            float pairLimit = stalePost ? 10f : PairPostLimit;
            int minX = Math.Max(1, (int)Math.Floor(s.Position.X - SearchRadius));
            int maxX = Math.Min(b.Map.Columns - 2, (int)Math.Ceiling(s.Position.X + SearchRadius));
            int minY = Math.Max(1, (int)Math.Floor(s.Position.Y - SearchRadius));
            int maxY = Math.Min(b.Map.Rows - 2, (int)Math.Ceiling(s.Position.Y + SearchRadius));
            // Dense local search catches single-cell windows; farther cells remain sparse.
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    V2 p = new V2(x + .5f, y + .5f);
                    float travel = V2.Distance(p, s.Position), group = V2.Distance(p, center);
                    int parity = ((int)(b.Time / 4f)) & 1;
                    if (travel > 6f && ((x & 1) != parity || (y & 1) != parity)) continue;
                    if (travel < 1.3f || travel > SearchRadius || !alone && group > 19f || partner != null && (!retreat && V2.Distance(p, partner.Position) > pairLimit || travel > pairLimit) || !b.Map.Walkable(p) || Occupied(b, s, p))
                        continue;
                    bool cover = PositionLogic.Protected(b, s, p);
                    bool terrainCover = b.Map.NearCover(p);
                    float distanceGain = closest == null ? 0 : V2.Distance(p, closest.Position) - currentGap;
                    float firing = 0;
                    int lanes = 0;
                    foreach (var c in known)
                        if (CanShootFrom(b, s, p, c))
                        {
                            lanes++;
                            float d = V2.Distance(p, c.Position);
                            float ideal = b.WeaponRange(s) * .84f;
                            firing = Math.Max(firing, 36 + (6 - c.TargetPriority) * 1.5f + (b.Time - c.FireSeenAt < 3 ? 5 : 0) - Math.Abs(d - ideal) * .4f);
                        }

                    float score;
                    if (retreat)
                    {
                        if (!terrainCover && distanceGain < 1.5f)
                            continue;
                        score = (cover ? 18 : terrainCover ? 10 : 0) + Math.Min(distanceGain, 10) * 4 + (lanes > 0 ? 15 : 0) - travel * .35f - group * .25f;
                    }
                    else
                    {
                        if (known.Count > 0 && lanes == 0)
                            continue; // No line to fire: not a new sniper post.
                        score = firing + (cover ? 15 : terrainCover ? 8 : 0) + Math.Min(distanceGain, 8) * 1.5f - (distanceGain < -2 ? 9 : 0) - travel * .33f - group * .16f + CombatTraining.Cover(b, s, p, closest == null ? center : closest.Position) * 3f;
                    }

                    posts.Add(new KeyValuePair<float, V2>(score, p));
                }

            posts.Sort((a, z) => z.Key.CompareTo(a.Key));
            for (int i = 0; i < Math.Min(9, posts.Count); i++)
            {
                V2 destination = posts[i].Value;
                var path = LossMemory.Find(b, s, destination, p => TacticalAI.Risk(b, s, p) * 3);
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > (partner != null && !retreat ? (stalePost ? 16f : 10f) : 23f))
                    continue;
                if (!retreat)
                {
                    // Permit a short controlled crossing, rather than requiring an
                    // unbroken chain of cover all the way to an otherwise useful post.
                    if (!PositionLogic.UsefulRoute(b, s, path))
                        continue;
                }

                s.Path = path;
                s.PathCursor = 0;
                s.LastMoved = b.Time;
                s.Destination = s.OrderAnchor = destination;
                s.Job = Job.Reposition;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.WasAtAnchor = false;
                s.SniperMoving = true;
                s.SniperRetreating = retreat;
                s.Tactical.CommitUntil = b.Time + PostHold;
                s.PressureMoveUntil = b.Time + 3;
                s.Order = s.Reason = retreat ? "Снайпер: отхожу дальше от противника к огневой позиции" : "Снайпер: нашёл прямой прострел, занимаю позицию";
                return true;
            }

            s.SniperMoving = false;
            return false;
        }
    }
}
