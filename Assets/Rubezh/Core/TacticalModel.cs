using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class PositionMemory
    {
        public readonly BoundDetourState Detour = new BoundDetourState();
        public int CoverId = -1, Revision = -1, OrderRevision = -1, LastEvent = -1, WaitReviews;
        // Per-soldier scratch space avoids allocating a new candidate list at every review.
        public readonly List<KeyValuePair<float, CoverSlot>> CandidateCache = new List<KeyValuePair<float, CoverSlot>>(32);
        public readonly HashSet<int> CandidateCells = new HashSet<int>();
        public float ShelterUntil, ShelterIssuedAt, NextShelterReview, NextUsefulReview;
        internal List<V2> ShelterRoute;
        internal V2 ShelterGoal;
        public float CommitUntil, NextReview, WaitSince = -1, NextNotice, LostSectorSince = -1;
        public string Status = "Получение задачи", Block = "", Knowledge = "Участок не наблюдался";
        public V2 LastAnchor;
        public float ProtectionAt = -100, SearchAt = -100;
        public V2 ProtectionPosition, SearchWanted, SearchResult;
        public int ProtectionRevision = -1, SearchRevision = -1, ProtectionContacts = -1;
        public bool ProtectionValue, SearchValue, SearchFiring;
        public float SearchRadius;
        public bool WasReady;
        public int AnchorMapRevision = -1;
        public V2 CheckedAnchor;
        public bool AnchorWasProtected;
    }

    public sealed class CoverSlot
    {
        public V2 Position;
        public int ObjectId;
    }

    public sealed class TacticalMap
    {
        public int Revision = -1;
        public readonly List<CoverSlot> Slots = new List<CoverSlot>();
        public int Corrections, Replans, LostSupport, VerifiedArrivals;
        public void Refresh(BattleMap map)
        {
            if (Revision == map.Revision)
                return;
            Revision = map.Revision;
            Slots.Clear();
            // Geometry only. Contact knowledge is evaluated separately for each side.
            for (int y = 0; y < map.Rows; y++)
                for (int x = 0; x < map.Columns; x++)
                {
                    if (!map.Walkable(x, y))
                        continue;
                    // Match NearCover: diagonal neighbours count, off-map cells never do.
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= map.Columns || ny >= map.Rows)
                                continue;
                            if (!map.Walkable(nx, ny))
                                Slots.Add(new CoverSlot { Position = new V2(x + .5f, y + .5f), ObjectId = ny * map.Columns + nx });
                        }
                }
        }
    }

    // Marginal group utility: unchanged teammates cancel out of before/after scoring.
    // Soft score only: never owns a route or blocks a commander/emergency action.
    public static class GroupPositionUtility
    {
        public static V2 Future(Soldier s)
        {
            return s.MoveAuthorized && s.PathCursor < s.Path.Count ? s.Path[s.Path.Count-1] : s.Position;
        }
        static float Crowd(V2 a, V2 b) => Math.Max(0, 2.2f-V2.Distance(a,b));
        // One evaluator per synchronous candidate search. Teammates and contacts cannot
        // change inside this search, so baseline LOS is computed at most once per contact.
        public sealed class Evaluation
        {
            readonly BattleSimulation b; readonly Soldier s;
            readonly List<Contact> contacts=new List<Contact>();
            readonly List<bool> before=new List<bool>();
            readonly List<int> covered=new List<int>();
            readonly bool enabled;
            public Evaluation(BattleSimulation battle,Soldier soldier) {
                b=battle;s=soldier;
                enabled=s.Side==Side.Green && PositionLogic.ShelterEligible(b,s) && !FormationEnvelope.AuthorizedManeuver(b,s);
                if(!enabled)return;
                foreach(var c in Knowledge.Contacts(b,s))if(PositionLogic.Recent(b,c)) {
                    contacts.Add(c);before.Add(PositionLogic.CanEngage(b,s,s.Position,c));covered.Add(-1);
                }
            }
            public float Delta(V2 candidate) {
            if(!enabled)return 0;
            float value=0;
            foreach(var mate in b.Soldiers)
            {
                if(mate==s || mate.Side!=s.Side || !mate.Available || !mate.Deployed) continue;
                V2 future=Future(mate);
                value += (Crowd(s.Position,future)-Crowd(candidate,future))*2;
                // Avoid selecting a destination inside an already committed travel corridor.
                if(V2.Distance(mate.Position,future)>1 && V2.Distance(candidate,s.Position)>.6f)
                    value -= Math.Max(0,.8f-V2.SegmentDistance(candidate,mate.Position,future))*2;
            }
            for(int i=0;i<contacts.Count;i++)
            {
                var c=contacts[i];bool after=PositionLogic.CanEngage(b,s,candidate,c);
                if(before[i]==after) continue;
                if(covered[i]<0) {
                    covered[i]=0;
                    foreach(var mate in b.Soldiers) {
                        if(mate==s || mate.Side!=s.Side || !mate.Available || !mate.Deployed || mate.Busy || mate.Broken || mate.Ammo<3 || mate.Suppression>=.62f || mate.SomaliFiring) continue;
                        if(PositionLogic.CanEngage(b,mate,Future(mate),c)){covered[i]=1;break;}
                    }
                }
                value += (after?1:-1)*(covered[i]==1?1f:4f);
            }
            return Math.Max(-8,Math.Min(8,value));
            }
        }
        public static float Delta(BattleSimulation b,Soldier s,V2 candidate) => new Evaluation(b,s).Delta(candidate);
    }

    public static class PositionLogic
    {
        public static bool ShelterEligible(BattleSimulation b, Soldier s)
        {
            return s.Available && s.Deployed && !s.VehicleRally && !s.VehicleDisperse && !s.Busy && !s.Broken && !s.Bleeding && !s.GrenadeEscape.Active && s.RescueTargetId < 0 && s.RescuerId < 0 && s.SupplyDonorId < 0 && s.SupplyReceiverId < 0 && s.HelpUntil <= b.Time && s.HelpRetreatUntil <= b.Time && s.HandsBusyUntil <= b.Time && s.CounterSniper.Phase == CounterSniperPhase.None && !LastOpponentHunt.Owns(b, s) && !TacticalOrders.Emergency(b.Team(s)) && !WithdrawalTactics.Active(b.Team(s)) && b.Team(s).Style != CombatStyle.Withdraw && !RedCounterattack.Retreating(b, s) && !ObjectiveOpportunity.IsCapturer(b, s) && s.FlagResponseUntil <= b.Time;
        }

        public static bool OwnsShelter(Soldier s)
        {
            return s.Tactical.ShelterRoute != null && object.ReferenceEquals(s.Path, s.Tactical.ShelterRoute) && V2.Distance(s.OrderAnchor, s.Tactical.ShelterGoal) < .2f;
        }

        public static bool ShelterMoving(BattleSimulation b, Soldier s)
        {
            return ProgressRecovery.Active(b, s) || (OwnsShelter(s) && s.Tactical.ShelterUntil > b.Time && s.PathCursor < s.Path.Count && ShelterEligible(b, s));
        }

        public static bool UsefulRoute(BattleSimulation b, Soldier s, List<V2> path)
        {
            float initial = Exposure(b, s, s.Position), openLength = 0;
            V2 previous = s.Position;
            foreach (var end in path)
            {
                float distance = V2.Distance(previous, end);
                int steps = Math.Max(1, (int)Math.Ceiling(distance * 2));
                for (int i = 1; i <= steps; i++)
                {
                    var p = previous + (end - previous) * (i / (float)steps);
                    float exposure = Exposure(b, s, p);
                    if (exposure > Math.Max(initial, 1.1f))
                        return false;
                    if (exposure > .85f && !Protected(b, s, p))
                        openLength += distance / steps;
                    foreach (var c in Knowledge.Contacts(b, s))
                        if (Recent(b, c) && V2.Distance(p, c.Position) < 6 && V2.Distance(p, c.Position) < V2.Distance(s.Position, c.Position) - .5f && b.Map.LineOfSight(p, c.Position))
                            return false;
                }

                previous = end;
            }

            return openLength <= 2.5f;
        }

        // A local correction must improve combat utility, not merely move to another tile.
        public static bool UsefulLocalChange(BattleSimulation b, Soldier s, V2 p)
        {
            if (s.Side != Side.Green || !ShelterEligible(b,s) || FormationEnvelope.AuthorizedManeuver(b,s)) return true;
            float distance=V2.Distance(s.Position,p);
            if(distance<.6f || distance>=3.5f) return true;
            bool currentCover=Protected(b,s,s.Position), nextCover=Protected(b,s,p);
            bool currentFire=HasSector(b,s,s.Position), nextFire=HasSector(b,s,p);
            if(nextCover && !currentCover || nextFire && !currentFire && nextCover) return true;
            if(Exposure(b,s,p)<Exposure(b,s,s.Position)-.2f) return true;
            // Leaving a genuinely crowded position is also useful, unlike cosmetic alignment.
            float currentGap=10,nextGap=10;
            foreach(var mate in b.Soldiers)
                if(mate!=s && mate.Side==s.Side && mate.Available && mate.Deployed)
                {currentGap=Math.Min(currentGap,V2.Distance(s.Position,mate.Position));nextGap=Math.Min(nextGap,V2.Distance(p,mate.Position));}
            return currentGap<1.5f && nextGap>currentGap+.8f && (!currentCover || nextCover);
        }

        public static float FightingPostBonus(BattleSimulation b, Soldier s, V2 p)
        {
            bool cover=Protected(b,s,p), fire=HasSector(b,s,p);
            return (cover ? (fire ? 18f : 8f) : (fire ? 2f : 0f));
        }

        public static bool UsefulPositionReview(BattleSimulation b, Soldier s)
        {
            if (!ShelterEligible(b, s) || s.LootTargetId >= 0 || s.LootGroundId >= 0 || s.WeaponAidTargetId >= 0 || s.LauncherPickupId >= 0 || s.Tactical.CommitUntil > b.Time || b.Time < s.Tactical.NextUsefulReview)
                return false;
            s.Tactical.NextUsefulReview = b.Time + 3;
            bool opening = b.Time < 25 && b.Time - s.LastMoved > 2 && s.PathCursor >= s.Path.Count;
            bool crowded = false;
            foreach (var u in b.Soldiers)
                if (u != s && u.Side == s.Side && u.Deployed && u.Alive && V2.Distance(u.Position, s.Position) < 2)
                    crowded = true;
            opening = opening && (!Protected(b, s, s.Position) || crowded);
            bool idle = b.Time - s.LastFire > 12 && !ExecutionReview.UsefulWatch(b, s);
            Contact threat = null;
            foreach (var c in Knowledge.Contacts(b, s))
                if (Recent(b, c) && (threat == null || V2.Distance(s.Position, c.Position) < V2.Distance(s.Position, threat.Position)))
                    threat = c;
            if (!opening && (!idle || threat == null))
                return false;
            if (s.PathCursor < s.Path.Count && b.Time - s.Progress.ProgressAt < 3.5f)
                return false;
            if (!opening && s.Job != Job.Flank && s.Job != Job.Cover && s.Job != Job.Hold && s.Job != Job.Command && s.Job != Job.Reposition)
                return false;
            b.Tactics.Refresh(b.Map);
            var choices = new List<KeyValuePair<float, V2>>();
            var seen = new HashSet<int>();
            foreach (var slot in b.Tactics.Slots)
            {
                var p = slot.Position;
                int cell = (int)p.Y * b.Map.Columns + (int)p.X;
                if (!seen.Add(cell) || V2.Distance(p, s.Position) < 1 || V2.Distance(p, s.Position) > (opening ? 5 : 8) || Occupied(b, s, p) || !Protected(b, s, p))
                    continue;
                bool tight = false;
                if (opening)
                    foreach (var u in b.Soldiers)
                        if (u != s && u.Side == s.Side && u.Alive && u.Deployed && (V2.Distance(u.Position, p) < 2 || V2.Distance(u.OrderAnchor, p) < 2))
                            tight = true;
                if (tight || !UsefulLocalChange(b,s,p) || !CommandHierarchy.WithinManeuver(b, s, p) || !opening && !HasSector(b, s, p))
                    continue;
                float score = -V2.Distance(s.Position, p) - Exposure(b, s, p) * 5 + FightingPostBonus(b,s,p) - FormationEnvelope.LaneCost(b, s, p) - FormationEnvelope.ShuffleCost(b, s, p);
                choices.Add(new KeyValuePair<float, V2>(score, p));
            }

            choices.Sort((a, z) => z.Key.CompareTo(a.Key));
            int attempts = 0;
            foreach (var choice in choices)
            {
                if (attempts++ >= 6)
                    break;
                var p = choice.Value;
                var path = LossMemory.Find(b, s, p, v => Exposure(b, s, v) * 4 + FireLaneSafety.PathCost(b, s, v) + (Occupied(b, s, v) ? 15 : 0));
                b.Stats.Paths++;
                if (path.Count == 0 || !MedicTactics.AllowRoute(b, s, path) || SupportAI.PathLength(s.Position, path) > (opening ? 7 : 10))
                    continue;
                if (threat != null && !UsefulRoute(b, s, path))
                    continue;
                s.Path = path;
                s.PathCursor = 0;
                s.OrderAnchor = s.Destination = p;
                s.Job = Job.Reposition;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.WasAtAnchor = false;
                s.SupportReady = false;
                s.Tactical.ShelterUntil = s.Tactical.CommitUntil = b.Time + 8;
                s.Tactical.ShelterIssuedAt = b.Time;
                s.Tactical.ShelterRoute = path;
                s.Tactical.ShelterGoal = p;
                s.Reason = opening ? "Рассредоточиваюсь на свободную защищённую позицию" : "Обход не даёт огня — перехожу на ближайший полезный рубеж";
                b.Diagnostics.Add(b, s, "replan", "Position", s.Reason);
                return true;
            }

            return false;
        }

        public static bool ShelterReview(BattleSimulation b, Soldier s)
        {
            if (ProgressRecovery.Active(b, s))
                return false;
            if (!ShelterEligible(b, s))
            {
                s.Tactical.ShelterUntil = 0;
                return false;
            }

            if (s.Tactical.ShelterRoute != null && !OwnsShelter(s))
            {
                s.Tactical.ShelterRoute = null;
                s.Tactical.ShelterUntil = 0;
                return false;
            }

            if (OwnsShelter(s) && !MedicTactics.AllowRoute(b, s, s.Path, s.PathCursor))
            {
                // The front changed after publication. Only stop the route owned by shelter.
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Destination = s.Position;
                s.Tactical.ShelterUntil = s.Tactical.CommitUntil = 0;
                s.Tactical.ShelterRoute = null;
                s.Tactical.SearchValue = false;
            }

            if (ShelterMoving(b, s))
            {
                if (b.Time - Math.Max(s.LastMoved, s.Tactical.ShelterIssuedAt) < 2.5f)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.Reason = "Ухожу с открытого места в ближайшее укрытие";
                    return true;
                }

                s.Tactical.ShelterUntil = 0;
                s.Tactical.CommitUntil = 0;
            }

            if (b.Time < s.Tactical.NextShelterReview)
                return false;
            s.Tactical.NextShelterReview = b.Time + 1.5f;
            if (Protected(b, s, s.Position) || Exposure(b, s, s.Position) <= .15f && b.Time - s.LastIncoming >= 4)
                return false;
            // A live bound is allowed to finish; a stale path must not disable shelter checks.
            if (s.PathCursor < s.Path.Count && b.Time - s.LastMoved < 2.5f)
                return false;
            V2 post;
            List<V2> path;
            if (!TryPostPlan(b, s, s.Position, 5, false, out post, out path) || V2.Distance(post, s.Position) < .6f)
                return false;
            if (path == null)
            {
                path = LossMemory.Find(b, s, post, v => Exposure(b, s, v) * 4 + FireLaneSafety.PathCost(b, s, v) + (Occupied(b, s, v) ? 15 : 0));
                b.Stats.Paths++;
            }
            if (path.Count == 0 || !MedicTactics.AllowRoute(b, s, path) || SupportAI.PathLength(s.Position, path) > 7)
                return false;
            float risk = Exposure(b, s, s.Position);
            V2 prev = s.Position;
            foreach (var end in path)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(V2.Distance(prev, end) * 2));
                for (int i = 1; i <= steps; i++)
                    if (Exposure(b, s, prev + (end - prev) * (i / (float)steps)) > risk + .15f)
                        return false;
                prev = end;
            }

            s.OrderAnchor = s.Destination = post;
            s.Path = path;
            s.PathCursor = 0;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.WasAtAnchor = false;
            s.SupportReady = false;
            s.Tactical.ShelterUntil = s.Tactical.CommitUntil = b.Time + 8;
            s.Tactical.ShelterIssuedAt = b.Time;
            s.Tactical.ShelterRoute = path;
            s.Tactical.ShelterGoal = post;
            s.Reason = "Ухожу с открытого места в ближайшее укрытие";
            b.Tactics.Corrections++;
            return true;
        }

        public static V2 Axis(BattleSimulation b)
        {
            var v = (b.Map.Objective - b.Map.Entry).Normalized;
            return v.Length < .1f ? new V2(1, 0) : v;
        }

        public static bool Recent(BattleSimulation b, Contact c)
        {
            return c.Confidence >= .2f && b.Time - c.SeenAt <= 12f;
        }

        public static bool Remembered(BattleSimulation b, Contact c)
        {
            return c.Confidence >= .15f && b.Time - c.SeenAt <= 25f;
        }

        public static bool Relevant(BattleSimulation b, Contact c, V2 p)
        {
            return Recent(b, c) && V2.Distance(p, c.Position) <= b.Config.WeaponRange + 7f;
        }

        public static float Exposure(BattleSimulation b, Soldier s, V2 p)
        {
            float sum = 0;
            if (b.RouteView.Owner == s)
            {
                foreach (var c in b.RouteView.Contacts(b))
                    if (V2.Distance(p, c.Position) <= b.Config.WeaponRange + 7f)
                        sum += VisibleRisk(b, s, p, c);
            }
            else
                foreach (var c in Knowledge.Contacts(b, s))
                    if (Relevant(b, c, p))
                        sum += VisibleRisk(b, s, p, c);
            return Math.Min(4, sum);
        }

        static float VisibleRisk(BattleSimulation b, Soldier s, V2 p, Contact c) => b.Map.LineOfSight(p, c.Position) ? c.Confidence * (1 - CombatTraining.Cover(b, s, p, c.Position)) * b.SmokeTransmission(p, c.Position) / (1 + Math.Max(0, b.Time - c.SeenAt) * .12f) : 0;
        public static bool SafeAgainst(BattleSimulation b, V2 p, Contact c, Soldier s = null)
        {
            return !b.Map.LineOfSight(p, c.Position) || CombatTraining.Cover(b, s, p, c.Position) >= .27f;
        }

        public static bool Protected(BattleSimulation b, Soldier s, V2 p)
        {
            var m = s.Tactical;
            bool own = V2.Distance(p, s.Position) < .01f;
            if (own && b.Time - m.ProtectionAt < .15f && m.ProtectionRevision == b.Map.Revision && m.ProtectionContacts == b.Team(s).Contacts.Count + s.Contacts.Count && V2.Distance(m.ProtectionPosition, p) < .05f)
                return m.ProtectionValue;
            bool value = ProtectedRaw(b, s, p);
            if (own)
            {
                m.ProtectionAt = b.Time;
                m.ProtectionPosition = p;
                m.ProtectionRevision = b.Map.Revision;
                m.ProtectionValue = value;
                m.ProtectionContacts = b.Team(s).Contacts.Count + s.Contacts.Count;
            }

            return value;
        }

        public static bool ProtectedRaw(BattleSimulation b, Soldier s, V2 p)
        {
            if (!b.Map.Walkable(p) || !b.Map.NearCover(p))
                return false;
            bool known = false;
            foreach (var c in Knowledge.Contacts(b, s))
                if (Relevant(b, c, p))
                {
                    known = true;
                    if (!SafeAgainst(b, p, c, s))
                        return false;
                }

            if (known)
                return true;
            // With no local observation, use the mission approach, not hidden enemies.
            V2 expected = p + (s.Side == Side.Red ? Axis(b) * -10f : Axis(b) * 10f);
            return CombatTraining.Cover(b, s, p, expected) >= .27f;
        }

        public static bool Occupied(BattleSimulation b, Soldier s, V2 p)
        {
            if (b.RouteView.Owner == s)
            {
                foreach (var v in b.RouteView.Positions(b))
                    if (V2.Distance(v, p) < 1.15f)
                        return true;
                return false;
            }

            foreach (var other in b.Soldiers)
                if (other.Id != s.Id && other.Side == s.Side && other.Alive && other.Deployed)
                {
                    if (V2.Distance(other.Position, p) < 1.15f)
                        return true;
                    if (other.Available && V2.Distance(other.Position, other.OrderAnchor) > .6f && ExecutionReview.ReservesGoal(b, other) && V2.Distance(other.OrderAnchor, p) < 1.15f)
                        return true;
                }

            return false;
        }

        public static bool AnchorLostCover(BattleSimulation b, Soldier s)
        {
            var m = s.Tactical;
            bool same = m.AnchorMapRevision >= 0 && V2.Distance(m.CheckedAnchor, s.OrderAnchor) < .05f;
            bool protectedNow = Protected(b, s, s.OrderAnchor);
            bool lost = same && m.AnchorMapRevision != b.Map.Revision && m.AnchorWasProtected && !protectedNow;
            m.AnchorMapRevision = b.Map.Revision;
            m.CheckedAnchor = s.OrderAnchor;
            m.AnchorWasProtected = protectedNow;
            return lost;
        }

        public static bool CanEngage(BattleSimulation b, Soldier s, V2 from, Contact c)
        {
            if (!Recent(b, c) || (s.Sniper && !SniperTactics.ClearLane(b, s, from, c.Position)) || V2.Distance(from, c.Position) > b.WeaponRange(s) || !b.Map.LineOfSight(from, c.Position) || b.SmokeTransmission(from, c.Position) < .3f)
                return false;
            // A hit on the target's cover is useful for suppression; an intervening wall is not.
            return V2.Distance(b.Map.BulletImpact(from, c.Position, true), c.Position) <= 1.8f;
        }

        public static bool HasSector(BattleSimulation b, Soldier s, V2 from)
        {
            foreach (var c in Knowledge.Contacts(b, s))
                if (CanEngage(b, s, from, c))
                    return true;
            return false;
        }

        public static bool Able(Soldier s, BattleSimulation b)
        {
            return !(SupplyRecovery.Active(s) || HandLauncher.Active(s)) && !BuddyActions.Active(s) && s.Deployed && s.Available && !s.Busy && !s.Broken && !s.Moving && s.Ammo >= 3 && s.Morale >= .2f && s.Suppression < .62f && s.ReloadUntil <= b.Time && s.HandsBusyUntil <= b.Time && ConcussionEffects.Current(b, s) < .3f;
        }

        public static bool UsefulSupport(BattleSimulation b, Soldier supporter, Soldier mover, V2 end)
        {
            if (!Able(supporter, b) || !SquadCoordination.SupportPosition(b, supporter))
                return false;
            bool danger = false;
            foreach (var c in Knowledge.Contacts(b, mover))
            {
                if (!Recent(b, c))
                    continue;
                bool intersects = false;
                for (int i = 0; i < 4; i++)
                {
                    V2 p = mover.Position + (end - mover.Position) * (i / 3f);
                    if (Relevant(b, c, p) && b.Map.LineOfSight(c.Position, p) && b.SmokeTransmission(c.Position, p) > .3f)
                    {
                        intersects = true;
                        break;
                    }
                }

                if (intersects)
                {
                    danger = true;
                    if (AreaSuppression.Covers(b, supporter, c))
                        return true;
                    if ((!c.Inferred || CounterSniper.AreaFire(b, supporter, c) && CounterSniper.SafeFireLane(b, supporter, c)) && CanEngage(b, supporter, supporter.Position, c) && !b.FriendlyInLane(supporter, c.Position))
                        return true;
                }
            }

            // No known threat: observation is allowed, but not falsely counted as fire support.
            return !danger && V2.Distance(supporter.Position, end) <= b.WeaponRange(supporter) && b.Map.LineOfSight(supporter.Position, end) && b.SmokeTransmission(supporter.Position, end) > .3f;
        }

        public static bool TryPost(BattleSimulation b, Soldier s, V2 wanted, float radius, bool firing, out V2 result)
        {
            List<V2> ignored;
            return TryPostPlan(b, s, wanted, radius, firing, out result, out ignored);
        }

        // The route is valid only for this synchronous query; never stored across ticks.
        // A cached position has no route and must be searched afresh by its consumer.
        static bool TryPostPlan(BattleSimulation b, Soldier s, V2 wanted, float radius, bool firing, out V2 result, out List<V2> route)
        {
            route = null;
            var memory = s.Tactical;
            if (b.Time - memory.SearchAt < 1f && memory.SearchRevision == b.Map.Revision && memory.SearchFiring == firing && memory.SearchRadius == radius && V2.Distance(memory.SearchWanted, wanted) < .3f)
            {
                if (memory.SearchValue && (V2.Distance(wanted,s.Position)>3.5f || UsefulLocalChange(b,s,memory.SearchResult)) && MedicTactics.AllowStep(b, s, memory.SearchResult) && !Occupied(b, s, memory.SearchResult) && Protected(b, s, memory.SearchResult) && (!firing || HasSector(b, s, memory.SearchResult)))
                {
                    result = memory.SearchResult;
                    return true;
                }
            }

            memory.SearchAt = b.Time;
            memory.SearchRevision = b.Map.Revision;
            memory.SearchFiring = firing;
            memory.SearchRadius = radius;
            memory.SearchWanted = wanted;
            memory.SearchValue = false;
            b.Tactics.Refresh(b.Map);
            result = s.Position;
            float best = float.MinValue;
            var candidates = memory.CandidateCache;
            candidates.Clear();
            memory.CandidateCells.Clear();
            var groupEvaluation=new GroupPositionUtility.Evaluation(b,s);
            foreach (var slot in b.Tactics.Slots)
            {
                V2 p = slot.Position;
                if (!memory.CandidateCells.Add((int)p.Y * b.Map.Columns + (int)p.X))
                    continue;
                float d = V2.Distance(p, wanted);
                if (d > radius || V2.Distance(wanted,s.Position)<=3.5f && !UsefulLocalChange(b,s,p) || !MedicTactics.AllowStep(b, s, p) || Occupied(b, s, p) || !Protected(b, s, p))
                    continue;
                if (b.Time < s.RejectedUntil && V2.Distance(p, s.RejectedPost) < 1.3f)
                    continue;
                float spread = FormationEnvelope.LaneCost(b, s, p) + FormationEnvelope.ShuffleCost(b, s, p);
                if (s.Side == Side.Red)
                    foreach (var o in b.Soldiers)
                        if (o.Side == s.Side && o.Available && o.Deployed && o.Id != s.Id)
                            spread += Math.Max(0, 2.5f - V2.Distance(p, o.Position)) * 2;
                float score = -b.Team(s).Observation.Uncertainty(b, p) - d * 1.6f - V2.Distance(s.Position, p) * .25f - Exposure(b, s, p) * 5 - spread;
                score += FightingPostBonus(b,s,p) + groupEvaluation.Delta(p) * .35f;
                if (V2.Distance(p, s.Position) < .6f)
                    score += 4f;
                candidates.Add(new KeyValuePair<float, CoverSlot>(score, slot));
            }

            candidates.Sort((a, z) => z.Key.CompareTo(a.Key));
            int attempts = 0;
            V2 last = new V2(-100, -100);
            foreach (var candidate in candidates)
            {
                V2 p = candidate.Value.Position;
                if (V2.Distance(last, p) < .1f)
                    continue;
                last = p;
                if (attempts++ >= 6)
                    break;
                if (V2.Distance(s.Position, p) > .6f)
                {
                    var path = LossMemory.Find(b, s, p, v => Exposure(b, s, v) * 2f);
                    b.Stats.Paths++;
                    if (path.Count == 0 || !MedicTactics.AllowRoute(b, s, path) || SupportAI.PathLength(s.Position, path) > V2.Distance(s.Position, p) * 2f + 6)
                        continue;
                    route = path;
                }

                best = candidate.Key;
                result = p;
                memory.SearchValue = true;
                memory.SearchResult = p;
                return true;
            }

            return best > float.MinValue;
        }

        public static void Bind(BattleSimulation b, Soldier s)
        {
            b.Tactics.Refresh(b.Map);
            s.Tactical.CoverId = -1;
            s.Tactical.Revision = b.Map.Revision;
            foreach (var slot in b.Tactics.Slots)
                if (V2.Distance(slot.Position, s.Position) < .65f)
                {
                    var center = b.Map.Center(slot.ObjectId);
                    bool useful = false;
                    foreach (var c in b.Team(s).Contacts.Values)
                        if (Relevant(b, c, s.Position) && V2.SegmentDistance(center, s.Position, c.Position) < .8f)
                        {
                            useful = true;
                            break;
                        }

                    if (useful || s.Tactical.CoverId < 0)
                        s.Tactical.CoverId = slot.ObjectId;
                    if (useful)
                        break;
                }
        }

        public static bool Arrived(BattleSimulation b, Soldier s)
        {
            bool capture = s.Side == Side.Green && V2.Distance(s.Position, b.Map.Objective) < 1.9f;
            if (!Protected(b, s, s.Position) && !capture)
            {
                s.Tactical.Status = "Позиция не защищена";
                return false;
            }

            Bind(b, s);
            s.Tactical.Status = HasSector(b, s, s.Position) ? "Позиция проверена" : "Наблюдаю, огневого контакта нет";
            b.Tactics.VerifiedArrivals++;
            return true;
        }

        public static bool Correct(BattleSimulation b, Soldier s, float radius)
        {
            V2 post;
            List<V2> route;
            if (!TryPostPlan(b, s, s.Position, radius, false, out post, out route) || V2.Distance(post, s.Position) < .6f)
                return false;
            if (route == null)
            {
                route = LossMemory.Find(b, s, post, v => Exposure(b, s, v) * 2f);
                b.Stats.Paths++;
            }

            if (route.Count == 0)
                return false; // Do not publish an unreachable cached position.
            s.OrderAnchor = post;
            s.Destination = post;
            s.Path.Clear();
            s.PathCursor = 0;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.WasAtAnchor = false;
            s.ManeuverPaused = false;
            s.Tactical.CommitUntil = b.Time + 3;
            s.Tactical.Status = "Занимаю защищённую сторону";
            s.Tactical.Block = "";
            s.Path = route;
            s.LastMoved = b.Time;
            s.SupportReady = false;
            b.Tactics.Corrections++;
            EventVoice.Emit(b, s, 1);
            return true;
        }

        public static bool Review(BattleSimulation b, Soldier s)
        {
            var m = s.Tactical;
            if (!s.Available || s.Busy)
                return false;
            int knowledge = b.Team(s).Observation.State(b, s.OrderAnchor);
            m.Knowledge = knowledge == 0 ? "Участок не наблюдался" : knowledge == 1 ? "Данные об участке устарели" : "Участок недавно наблюдался";
            if (m.OrderRevision != s.OrderRevision || V2.Distance(m.LastAnchor, s.OrderAnchor) > .8f)
            {
                m.OrderRevision = s.OrderRevision;
                m.LastAnchor = s.OrderAnchor;
                m.Status = "Выполняю задачу";
            }

            bool protectedNow = SquadCoordination.SupportPosition(b, s);
            if (m.Revision != b.Map.Revision)
            {
                m.Revision = b.Map.Revision;
                if (m.CoverId >= 0 && b.Map.Walkable(m.CoverId % b.Map.Columns, m.CoverId / b.Map.Columns))
                {
                    m.CoverId = -1;
                    m.NextReview = 0;
                    EventVoice.Emit(b, s, 2);
                }
            }

            if (s.SupportReady && !SquadBrain.Ready(b, s, b.Team(s)))
            {
                s.SupportReady = false;
                b.Tactics.LostSupport++;
                EventVoice.Emit(b, s, 3);
            }

            if (b.Time < m.NextReview)
                return false;
            m.NextReview = b.Time + .8f + (s.Id % 5) * .08f;
            bool threatened = Exposure(b, s, s.Position) > .15f || b.Time - s.LastIncoming < 4;
            if (!protectedNow && threatened && b.Time >= m.CommitUntil && s.PathCursor >= s.Path.Count && s.ReloadUntil <= b.Time)
            {
                if (Correct(b, s, 5))
                    return true;
                m.Status = "Открытая позиция";
                m.Block = "Доступного укрытия нет";
            }

            if (s.State == ActionState.WaitCover)
            {
                if (m.WaitSince < 0)
                    m.WaitSince = b.Time;
                if (TacticalAI.SafeApproach(b, s))
                {
                    m.WaitSince = -1;
                    m.Block = "";
                    m.Status = "На подходе нет известных угроз";
                    s.MoveAuthorized = true;
                    return false;
                }

                m.Block = "Нет подтверждённой поддержки";
                if (b.Time - m.WaitSince > 7)
                {
                    m.WaitSince = b.Time;
                    m.WaitReviews++;
                    b.Team(s).NextPlan = 0;
                    b.Team(s).Defense.NextReview = 0;
                    b.Tactics.Replans++;
                    V2 p;
                    if (TryPost(b, s, s.OrderAnchor, 5, true, out p) && V2.Distance(p, s.OrderAnchor) > 1.2f)
                    {
                        s.RejectedPost = s.OrderAnchor;
                        s.RejectedUntil = b.Time + 8;
                        s.OrderAnchor = p;
                        s.Destination = p;
                        s.Path.Clear();
                        s.PathCursor = 0;
                        s.WasAtAnchor = false;
                    }

                    EventVoice.Emit(b, s, 4);
                    if (m.WaitReviews >= 2)
                    {
                        s.RejectedPost = s.OrderAnchor;
                        s.RejectedUntil = b.Time + 12;
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Position;
                        s.Job = Job.Hold;
                        s.MoveAuthorized = true;
                        m.WaitReviews = 0;
                        m.WaitSince = -1;
                        m.Status = "Задача возвращена командиру";
                        CommandHierarchy.Failed(b, s, "Ожидание прикрытия исчерпано");
                    }
                }
            }
            else
            {
                m.WaitSince = -1;
                m.WaitReviews = 0;
                if (m.Block == "Нет подтверждённой поддержки")
                    m.Block = "";
            }

            if (protectedNow && V2.Distance(s.Position, s.OrderAnchor) < .6f)
            {
                m.Status = HasSector(b, s, s.Position) ? "Позиция проверена" : "Наблюдаю, огневого контакта нет";
                if (m.Block == "Доступного укрытия нет" || m.Block == "Нет доступного защищённого места")
                    m.Block = "";
            }

            bool support = s.Role == Role.MachineGunner || s.Sniper || b.Team(s).SupportIds.Contains(s.Id);
            if (support && !s.Broken && s.Ammo > 0 && s.PathCursor >= s.Path.Count && !HasSector(b, s, s.Position) && b.Team(s).Contacts.Count > 0)
            {
                if (m.LostSectorSince < 0)
                    m.LostSectorSince = b.Time;
                if (b.Time - m.LostSectorSince > 5 && b.Time > m.CommitUntil)
                {
                    m.LostSectorSince = b.Time;
                    V2 post;
                    if (TryPost(b, s, s.Position, 7, true, out post) && V2.Distance(post, s.Position) > 1 && HasSector(b, s, post))
                    {
                        if (SquadBrain.GiveOrder(b, b.Team(s), s, Job.Reposition, post, false))
                        {
                            m.CommitUntil = b.Time + 4;
                            m.Status = "Ищу полезный сектор";
                            EventVoice.Emit(b, s, 5);
                        }
                    }
                }
            }
            else
                m.LostSectorSince = -1;
            if (s.Ammo + s.Reserve == 0)
            {
                m.Block = "Нет патронов";
                EventVoice.Emit(b, s, 6);
            }

            return false;
        }
    }

    public static class EventVoice
    {
        static readonly string[][] Lines =
        {
            new[]
            {
                "На месте. Проверяю сектор",
                "Добрался. Проверяю позицию",
                "Позицию занял, осматриваюсь"
            },
            new[]
            {
                "Здесь простреливают, смещаюсь",
                "С этой стороны не защищает",
                "Ищу защищённое место"
            },
            new[]
            {
                "Укрытие разбито, меняю место",
                "Защиту потерял",
                "Здесь больше не укрыться"
            },
            new[]
            {
                "Прикрытие снято, пока не готов",
                "Сектор потерял, не прикрываю",
                "Не рассчитывайте на моё прикрытие"
            },
            new[]
            {
                "Поддержки нет, нужен новый порядок",
                "Ожидание затянулось. Запрашиваю решение",
                "Задача заблокирована, сообщаю"
            },
            new[]
            {
                "Отсюда не достаю. Меняю позицию",
                "Ищу полезный сектор",
                "Нужна другая огневая позиция"
            },
            new[]
            {
                "Патроны закончились, нужно пополнение",
                "Нет БК, запрашиваю помощь",
                "Без патронов. Ищу запас"
            },
            new[]
            {
                "Позиция проверена, готов",
                "Есть защита и сектор",
                "Готов поддерживать группу"
            },
            new[]
            {
                "Контакт потерян, позицию помним",
                "Новых наблюдений нет",
                "Цель скрылась, продолжаем наблюдать"
            },
            new[]
            {
                "Состав изменился, задачи перераспределяю",
                "Проверить своих. Уточняю задачи",
                "Оставшиеся, работаем по новому назначению"
            },
            new[]
            {
                "Проход закрыт, нужен другой маршрут",
                "Не могу пройти к позиции",
                "Маршрут недоступен, сообщаю"
            }
        };
        public static void Emit(BattleSimulation b, Soldier s, int kind)
        {
            if (!s.Alive || b.Time < s.Tactical.NextNotice)
                return;
            if (kind == 7 && !SquadBrain.Ready(b, s, b.Team(s)))
                return;
            s.Tactical.NextNotice = b.Time + 8;
            s.Tactical.LastEvent = kind;
            b.Say(s, Lines[kind][b.VoiceRandom.Int(Lines[kind].Length)], true);
        }

        public static bool Handle(BattleSimulation b, Soldier s, int category)
        {
            if (category == 1)
            {
                if (SquadBrain.Ready(b, s, b.Team(s)))
                    Emit(b, s, 7);
                return true;
            }

            if (category == 3)
            {
                Emit(b, s, 0);
                return true;
            }

            if (category == 7)
            {
                b.Say(s, "Зарядил. Проверяю сектор");
                return true;
            }

            if (category == 6)
            {
                if (!PositionLogic.Able(s, b) || !PositionLogic.HasSector(b, s, s.Position))
                    return true;
                b.Say(s, "Готов поддержать, пока меняешь магазин");
                return true;
            }

            if (category == 5 || category == 31)
            {
                b.Say(s, category == 31 ? "Меняю короб. Пока не прикрываю" : "Меняю магазин. Пока не прикрываю", true);
                return true;
            }

            if (category == 4)
            {
                if (s.State != ActionState.WaitCover)
                    return true;
                b.Say(s, "Жду подтверждения поддержки");
                return true;
            }

            return false;
        }
    }
}

namespace SilentOrders.Core
{
    public static class AdaptiveDefense
    {
        public static void Plan(BattleSimulation b, Squad q, List<Soldier> units)
        {
            var d = q.Defense;
            V2 axis = PositionLogic.Axis(b), lateral = new V2(-axis.Y, axis.X);
            Contact threat = DefenseBrain.SelectThreat(b, q);
            V2 target = threat != null ? threat.Position : b.Map.Entry;
            q.RequiredSupport = 1;
            q.ReadyCount = SquadBrain.CountReady(b, q);
            q.Plan = "Подвижная оборона • " + d.Mode;
            if (!d.Initialized)
            {
                d.Initialized = true;
                d.NextReview = 0;
            }

            if (d.Active)
            {
                bool abort = b.Time - d.Started > 22;
                foreach (int id in d.Movers)
                {
                    var s = b.Soldiers[id];
                    if (!s.Available || s.Broken)
                        abort = true;
                }

                if (abort)
                {
                    foreach (int id in d.Movers)
                    {
                        var s = b.Soldiers[id];
                        if (!s.Available)
                            continue;
                        s.RejectedPost = s.OrderAnchor;
                        s.RejectedUntil = b.Time + 8;
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Position;
                        s.Job = Job.Hold;
                        s.Tactical.Block = "Задача пересмотрена";
                    }

                    d.Active = false;
                    d.Aborted++;
                    b.Stats.PlanAborts++;
                    q.ManeuverIds.Clear();
                    d.NextReview = b.Time + 1;
                    q.CommanderOrder = "Задача остановлена. Проверить позиции и поддержку";
                    b.Log(q.Side, q.CommanderOrder);
                    return;
                }

                bool complete = true;
                foreach (int id in d.Movers)
                {
                    var s = b.Soldiers[id];
                    if (V2.Distance(s.Position, s.OrderAnchor) < .6f && PositionLogic.Protected(b, s, s.Position))
                    {
                        if (!s.WasAtAnchor)
                        {
                            s.WasAtAnchor = true;
                            PositionLogic.Arrived(b, s);
                            EventVoice.Emit(b, s, 0);
                        }

                        s.Job = Job.Hold;
                        s.MoveAuthorized = true;
                        continue;
                    }

                    complete = false;
                    s.MoveAuthorized = TacticalAI.SafeApproach(b, s) || DefenseBrain.Covering(b, s, s.OrderAnchor) || CommanderPriorities.SmokeSafe(b, s);
                    if (b.Time < q.SmokeWaitUntil || q.PlannedSmoke.Count > 0)
                        s.MoveAuthorized = false;
                    q.Execution = s.MoveAuthorized ? s.Name + ": занимает проверенную позицию" : s.Name + ": нет подтверждённой поддержки";
                }

                if (complete)
                {
                    d.Completed++;
                    d.Active = false;
                    q.ManeuverIds.Clear();
                    q.Phase = ManeuverPhase.Regroup;
                    q.PhaseSince = b.Time;
                    d.NextReview = b.Time + 3;
                    q.CommanderOrder = "Позиции проверены. Закрепиться, проверить своих";
                    b.Log(q.Side, q.CommanderOrder);
                }

                return;
            }

            if (b.Time < d.NextReview)
                return;
            d.NextReview = b.Time + 3;
            q.SupportIds.Clear();
            foreach (var s in units)
                if (PositionLogic.Able(s, b) && PositionLogic.HasSector(b, s, s.Position))
                    q.SupportIds.Add(s.Id);
            V2 front = q.DefenseCenter(b) - axis * (q.DefenderGroup == null ? 6f : Math.Min(3f, q.DefenderGroup.Radius * .4f));
            int bad = 0;
            foreach (var s in units)
                if (s.Health < 35 || s.Broken || s.Suppression > .75f)
                    bad++;
            bool withdraw = bad >= 2;
            if (threat != null)
            {
                // Shift attention, but keep positions within the objective's responsibility area.
                float offset = Util.Clamp(V2.Dot(threat.Position - q.DefenseCenter(b), lateral), -5, 5);
                front += lateral * offset;
                if (V2.Dot(threat.Position - front, axis) > 0)
                    front = q.DefenseCenter(b) - axis * 2 + lateral * offset;
            }

            if (withdraw)
                front = q.DefenseCenter(b) - axis * 2;
            d.LineX = front.X;
            d.LineY = front.Y;
            Soldier chosen = null;
            V2 destination = front;
            float highest = float.MinValue;
            for (int i = 0; i < units.Count; i++)
            {
                var s = units[i];
                if (!s.Available || CounterSniper.Reserved(b, s) || s.HelpUntil > b.Time || s.HelpRetreatUntil > b.Time || s.FlagResponseUntil > b.Time || s.VehicleRally || s.VehicleDisperse || s.Busy || s.Broken || s.Suppression > .78f || s.Health < 25 || b.Time < s.Tactical.CommitUntil)
                    continue;
                bool rear = s.Role == Role.Medic || s.Sniper;
                float spread = (i - (units.Count - 1) * .5f) * 2.8f;
                V2 wanted = front + lateral * spread + (rear ? axis * 2.5f : new V2());
                bool sheltered = PositionLogic.Protected(b, s, s.Position), useful = PositionLogic.HasSector(b, s, s.Position);
                float nearest = 100;
                foreach (var o in units)
                    if (o.Id != s.Id)
                        nearest = Math.Min(nearest, V2.Distance(s.Position, o.Position));
                bool isolated = units.Count > 1 && nearest > 9;
                // Stable useful positions stay occupied; unobserved sectors retain sentries.
                if (nearest >= 2f && sheltered && !isolated && V2.Distance(s.Position, q.DefenseCenter(b)) < (q.DefenderGroup == null ? 15f : q.DefenderGroup.Radius) && (useful || threat == null || rear))
                    continue;
                V2 post;
                if (!PositionLogic.TryPost(b, s, wanted, 6, !rear, out post) || V2.Distance(post, s.Position) < 1)
                    continue;
                if (V2.Distance(post, q.DefenseCenter(b)) > (q.DefenderGroup == null ? 17f : q.DefenderGroup.Radius))
                    continue;
                if (sheltered && threat != null && !rear && !PositionLogic.HasSector(b, s, post))
                    continue;
                float priority = (nearest < 2f ? 18f : 0) + (!sheltered ? 20 : 0) + (isolated ? 10 : 0) + (s.Role == Role.MachineGunner && !useful ? 8 : 0) - (s.Busy ? 100 : 0) - s.Suppression * 5 - V2.Distance(post, s.Position) * .2f;
                if (priority > highest)
                {
                    highest = priority;
                    chosen = s;
                    destination = post;
                }
            }

            if (chosen == null)
            {
                d.Mode = "Наблюдение участков";
                q.Execution = "Удерживаем пригодные позиции; проверяем изменения";
                q.Phase = ManeuverPhase.Hold;
                return;
            }

            string mode = withdraw ? "Восстановить оборону" : !PositionLogic.Protected(b, chosen, chosen.Position) ? "Занять защищённую сторону" : chosen.Role == Role.MachineGunner ? "Восстановить огневую поддержку" : "Перераспределить позиции";
            int priorRevision = q.Revision;
            q.Revision++;
            if (!SquadBrain.GiveOrder(b, q, chosen, Job.Reposition, destination, false))
            {
                q.Revision = priorRevision;
                return;
            }

            d.Mode = mode;
            d.Movers.Clear();
            d.Movers.Add(chosen.Id);
            q.ManeuverIds.Clear();
            q.ManeuverIds.Add(chosen.Id);
            chosen.Tactical.CommitUntil = b.Time + 4;
            d.Active = true;
            d.Started = b.Time;
            d.Target = threat != null ? threat.EnemyId : -1;
            d.Plans++;
            q.Phase = ManeuverPhase.Adapt;
            q.Stage = destination;
            q.CommanderOrder = d.Mode + ". " + chosen.Name + ", доложить о готовности";
            b.Say(b.Soldiers[q.LeaderId], q.CommanderOrder, true);
            if (withdraw)
                SmokePlanner.Plan(b, q, destination);
        }
    }

    public static class TaskReview
    {
        public static void Group(BattleSimulation b, Squad q)
        {
            q.Observation.Update(b, q);
            int signature = 0;
            float newest = -100;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && s.Deployed && !s.Broken)
                    signature ^= (s.Id + 1) * 397;
            foreach (var c in q.Contacts.Values)
                if (c.SeenAt > newest)
                    newest = c.SeenAt;
            if (q.TacticalComposition != signature)
            {
                if (q.TacticalComposition != -1)
                {
                    q.Defense.NextReview = 0;
                    q.NextPriorityPlan = 0;
                    EventVoice.Emit(b, b.Soldiers[q.LeaderId], 9);
                }

                q.TacticalComposition = signature;
            }

            bool fresh = b.Time - newest < 8;
            if (q.TacticalHadFresh && !fresh)
            {
                EventVoice.Emit(b, b.Soldiers[q.LeaderId], 8);
                q.Execution = "Свежего наблюдения нет; учитываем последние контакты";
            }

            q.TacticalHadFresh = fresh;
        }
    }
}

namespace SilentOrders.Core
{
    public sealed class ObservationMemory
    {
        public readonly Dictionary<int, Contact> LastContacts = new Dictionary<int, Contact>();
        public readonly Dictionary<int, float> SeenCells = new Dictionary<int, float>();
        public sealed class ClearReport { public float At; public int Sender, Revision; }
        public readonly Dictionary<int, ClearReport> ClearCells = new Dictionary<int, ClearReport>();
        public int AvoidedChecks;
        static int Cell(V2 p) => ((int)p.Y/4)*10000+(int)p.X/4;
        bool Threat(BattleSimulation b, V2 p)
        {
            foreach(var c in LastContacts.Values)
                if(b.Time-c.SeenAt<=b.Config.ContactLifetime && V2.Distance(c.Position,p)<6) return true;
            foreach(var c in NoiseAreas)
                if(b.Time-c.SeenAt<=12 && V2.Distance(c.Position,p)<6) return true;
            return false;
        }
        public bool ClearedByMate(BattleSimulation b, Soldier s, V2 p)
        {
            if(p.X<0 || p.Y<0 || p.X>=b.Map.Columns || p.Y>=b.Map.Rows) return false;
            if(!ClearCells.TryGetValue(Cell(p),out var report) || report.Sender==s.Id || report.Revision!=b.Map.Revision || b.Time-report.At<b.Config.RadioDelay || b.Time-report.At>8 || Threat(b,p)) return false;
            foreach(var c in Knowledge.Contacts(b,s))
                if(PositionLogic.Recent(b,c) && V2.Distance(c.Position,p)<6) return false;
            return true;
        }
        void ConfirmClear(BattleSimulation b, Soldier s, int x, int y)
        {
            V2 center=new V2(x*4+2,y*4+2);
            if(Threat(b,center)) return;
            // Inspect every tile, not just a ray to the block centre. Walls/smoke invalidate the claim.
            for(int dy=0;dy<4;dy++) for(int dx=0;dx<4;dx++)
            {
                V2 p=new V2(x*4+dx+.5f,y*4+dy+.5f);
                if(p.X>=b.Map.Columns || p.Y>=b.Map.Rows || !DirectionalVision.InView(b,s,p) || !b.Map.LineOfSight(s.Position,p) || b.SmokeTransmission(s.Position,p)<=.4f) return;
            }
            foreach(var c in s.Contacts.Values)
                if(PositionLogic.Recent(b,c) && V2.Distance(c.Position,center)<6) return;
            int key=y*10000+x;
            // Keep the original sender/timestamp until expiry so radio delivery can complete.
            if(!ClearCells.TryGetValue(key,out var old) || b.Time-old.At>8 || old.Revision!=b.Map.Revision)
                ClearCells[key]=new ClearReport {At=b.Time,Sender=s.Id,Revision=b.Map.Revision};
        }
        public float NextScan;
        public readonly List<ThreatArea> NoiseAreas = new List<ThreatArea>();
        public void ReportNoise(BattleSimulation b, V2 source)
        {
            V2 area = new V2((float)Math.Floor(source.X / 6) * 6 + 3, (float)Math.Floor(source.Y / 6) * 6 + 3);
            foreach (var old in NoiseAreas)
                if (V2.Distance(old.Position, area) < 1)
                {
                    old.SeenAt = b.Time;
                    return;
                }

            if (NoiseAreas.Count >= 8)
                NoiseAreas.RemoveAt(0);
            NoiseAreas.Add(new ThreatArea { Position = area, SeenAt = b.Time, Confidence = .4f, Inferred = true });
        }

        readonly List<int> expiredContacts = new List<int>();
        public void Update(BattleSimulation b, Squad q)
        {
            NoiseAreas.RemoveAll(c => b.Time - c.SeenAt > 12);
            foreach (var c in q.Contacts.Values)
            {
                Contact old;
                LastContacts.TryGetValue(c.EnemyId, out old);
                if (old == null || Knowledge.Newer(old, c) == c && c.SeenAt > old.SeenAt)
                    LastContacts[c.EnemyId] = c.Copy();
            }

            var expired = expiredContacts;
            expired.Clear();
            foreach (var c in LastContacts)
                if (b.Time - c.Value.SeenAt > 30)
                    expired.Add(c.Key);
            foreach (int key in expired)
                LastContacts.Remove(key);
            if (b.Time < NextScan)
                return;
            NextScan = b.Time + 1;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && s.Deployed && !s.Broken)
                {
                    int sx = (int)s.Position.X / 4, sy = (int)s.Position.Y / 4, scan = (int)Math.Ceiling(DirectionalVision.FrontRange(b, s) / 4);
                    for (int y = Math.Max(0, sy - scan); y <= Math.Min((b.Map.Rows - 1) / 4, sy + scan); y++)
                        for (int x = Math.Max(0, sx - scan); x <= Math.Min((b.Map.Columns - 1) / 4, sx + scan); x++)
                        {
                            int cell = y * 10000 + x;
                            if (SeenCells.TryGetValue(cell, out var seenAt) && seenAt == b.Time)
                                continue;
                            V2 p = new V2(Math.Min(b.Map.Columns - .5f, x * 4 + 2), Math.Min(b.Map.Rows - .5f, y * 4 + 2));
                            if (DirectionalVision.InView(b, s, p) && b.Map.LineOfSight(s.Position, p) && b.SmokeTransmission(s.Position, p) > .4f)
                            {
                                SeenCells[y * 10000 + x] = b.Time;
                                ConfirmClear(b,s,x,y);
                            }
                        }
                }
        }

        public int State(BattleSimulation b, V2 p)
        {
            float at;
            return !SeenCells.TryGetValue(((int)p.Y / 4) * 10000 + (int)p.X / 4, out at) ? 0 : b.Time - at <= 8 ? 2 : 1;
        }

        public float Uncertainty(BattleSimulation b, V2 p)
        {
            int state = State(b, p);
            float value = state == 0 ? .7f : state == 1 ? .3f : 0;
            foreach (var c in LastContacts.Values)
                if (b.Time - c.SeenAt > 12 && V2.Distance(p, c.Position) < 6)
                    value += .4f;
            return value;
        }
    }
}
