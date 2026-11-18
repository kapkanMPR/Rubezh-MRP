using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class EngagementState
    {
        public float EmergencyUntil, EmergencyRetryAt, ApproachUntil, ApproachRetryAt, ProgressAt, Health;
        public int AbortDeadline, AbortTask, AbortOrder, AbortThreat, AbortTarget, AbortBuddyFire, AbortBlocked, EmergencyShots, EmergencyBursts, BurstShots, ApproachStarts, ApproachCompleted, ApproachAborted, Target = -1, Revision, Cursor;
        public V2 Goal, ProgressPosition;
        public List<V2> Route = new List<V2>();
        public string Reason = "";
    }

    public static class EngagementPolicy
    {
        public static bool Active(BattleSimulation b, Soldier s) => s.Engagement.ApproachUntil > b.Time;
        public static bool EmergencyAllowed(BattleSimulation b, Soldier s, Contact c)
        {
            if (c == null || s.GunSniper || !s.Available || s.Broken || s.Ammo <= 0 || s.Suppression > .8f || s.ReloadUntil > b.Time || s.HandsBusyUntil > b.Time)
                return false;
            float distance = V2.Distance(s.Position, c.Position), range = b.WeaponRange(s);
            if (distance <= range || distance > range + 2 || !s.Visible.Contains(c.EnemyId) || !c.Visual || c.Inferred || b.Time - c.SeenAt > b.Config.PerceptionPeriod * 2 || b.Time - c.FireSeenAt > 3)
                return false;
            var e = s.Engagement;
            if (e.EmergencyUntil > b.Time)
            {
                if (e.BurstShots >= 3)
                    return false;
            }
            else if (b.Time < e.EmergencyRetryAt)
                return false;
            if (!b.Map.LineOfSight(s.Position, c.Position) || b.SmokeTransmission(s.Position, c.Position) < .3f || b.FriendlyInLane(s, c.Position))
                return false;
            if (b.Time - s.LastIncoming < 2 && (s.Suppression > .35f || s.Health < 45))
                return true;
            foreach (var ally in b.Soldiers)
                if (ally != s && ally.Side == s.Side && ally.Alive && ally.Deployed && V2.Distance(ally.Position, s.Position) < 9 && b.Time - ally.LastIncoming < 2 && (ally.Suppression > .55f || ally.Incapacitated || WithdrawalTactics.Active(b.Team(ally)) || RedCounterattack.Retreating(b, ally)) && b.Map.LineOfSight(c.Position, ally.Position))
                    return true;
            return false;
        }

        public static void Fired(BattleSimulation b, Soldier s, Contact c)
        {
            if (V2.Distance(s.Position, c.Position) <= b.WeaponRange(s))
                return;
            var e = s.Engagement;
            if (e.EmergencyUntil <= b.Time)
            {
                e.EmergencyUntil = b.Time + 1.5f;
                e.EmergencyRetryAt = b.Time + 8;
                e.BurstShots = 0;
                e.EmergencyBursts++;
            }

            e.BurstShots++;
            e.EmergencyShots++;
            s.FireStatus = "Экстренный огонь: точность ×0.5";
            b.History.Event(b, "emergencyFire", s.Id, c.EnemyId, s.Position, c.Position, .5f, "Непосредственная угроза своим");
        }

        static bool Eligible(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            return s.Available && !s.Broken && !s.Busy && !s.Bleeding && !s.GunSniper && !s.GunMachineGunner && s.Ammo > 0 && s.Suppression < .3f && !s.GrenadeEscape.Active && !TacticalOrders.ServiceActive(s) && !CounterSniper.Reserved(b, s) && q.Style != CombatStyle.Passive && !TacticalOrders.Emergency(q) && !WithdrawalTactics.Active(q) && !RedCounterattack.Retreating(b, s) && !LastOpponentHunt.Owns(b, s) && !ObjectiveOpportunity.IsCapturer(b, s) && !q.Breakthrough.Active && !q.CommandTransition.Waiting && !s.PriorityMovement.Transfer && !q.SupportIds.Contains(s.Id) && s.FlagResponseUntil <= b.Time && s.HelpUntil <= b.Time && s.HelpRetreatUntil <= b.Time && s.HandsBusyUntil <= b.Time && s.ReloadUntil <= b.Time && s.LootTargetId < 0 && s.LootGroundId < 0 && s.LauncherPickupId < 0 && s.WeaponAidTargetId < 0;
        }

        static bool NewChoice(BattleSimulation b, Soldier s) => b.StealthChoiceEnabled && !b.Historical01225 && s.Side == Side.Green;
        static bool FriendNeedsFire(BattleSimulation b, Soldier s)
        {
            foreach (var a in b.Soldiers)
                if (a != s && a.Side == s.Side && a.Available && V2.Distance(a.Position, s.Position) < 10 && (NewChoice(b, s) ? b.Time - a.LastIncoming < 3 && (a.Suppression > .45f || a.Health < 45 || a.Broken) : (b.Time - a.LastFire < 3 || b.Time - a.LastIncoming < 3)))
                    return true;
            return false;
        }

        public static bool TryApproach(BattleSimulation b, Soldier s, Contact c)
        {
            if (s.Side == Side.Green && ReconCombatPhase.Active(b))
                return false;
            var e = s.Engagement;
            if (b.Time < e.ApproachRetryAt || !Eligible(b, s) || c == null || !c.Visual || c.Reported || c.Inferred || !s.Visible.Contains(c.EnemyId) || b.Time - c.SeenAt > b.Config.PerceptionPeriod * 2 || b.Time - s.LastIncoming < 6 || b.Time - s.LastFire < 6 || FriendNeedsFire(b, s) || s.Side != Side.Green && PositionLogic.Protected(b, s, s.Position))
                return false;
            if (TacticalOrders.Flanks(b.Team(s)) && s.BuddyId != s.Id && FlankTactics.MoverFor(b.Team(s), s) != s.Id)
                return false;
            float distance = V2.Distance(s.Position, c.Position), range = b.WeaponRange(s);
            float attention = V2.Dot(c.ObservedFacing.Normalized, (s.Position - c.Position).Normalized);
            if (attention > .35f)
                return false;
            bool rear = attention < -.3f;
            if (distance > range || distance < (NewChoice(b, s) && rear ? 6 : range * (rear ? .6f : .85f)) || b.Time - c.FireSeenAt < 3 && (!NewChoice(b, s) || !rear))
                return false;
            // Do not split a pair while its other member is already moving.
            if (s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count && s.BuddyId != s.Id)
            {
                var buddy = b.Soldiers[s.BuddyId];
                if (buddy.Available && (buddy.Moving || V2.Distance(buddy.Position, s.Position) > 8))
                    return false;
            }

            e.ApproachRetryAt = b.Time + 12;
            V2 wanted = s.Position + (c.Position - s.Position).Normalized * 2f, post;
            bool objective = s.Side == Side.Green && PositionLogic.Protected(b, s, s.Position) && V2.Distance(s.Position, b.Map.Objective) > 4;
            if (objective)
                wanted = s.Position + (b.Map.Objective - s.Position).Normalized * 2f;
            if (!PositionLogic.TryPost(b, s, wanted, 1.5f, true, out post) || !PositionLogic.Protected(b, s, post) || V2.Distance(s.Position, post) > 3 || !objective && V2.Distance(post, c.Position) > distance - .8f || V2.Distance(post, c.Position) < 5 || PositionLogic.Occupied(b, s, post))
                return false;
            var path = LossMemory.Find(b, s, post, p => PositionLogic.Exposure(b, s, p) * 5);
            b.Stats.Paths++;
            if (objective && (V2.Distance(post, b.Map.Objective) >= V2.Distance(s.Position, b.Map.Objective) - .7f || PositionLogic.Exposure(b, s, post) > PositionLogic.Exposure(b, s, s.Position) + .05f))
                return false;
            if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 4 || !PositionLogic.UsefulRoute(b, s, path))
                return false;
            TacticalAI.Stop(s);
            e.Route = path;
            e.Cursor = 0;
            e.Goal = post;
            e.Target = c.EnemyId;
            e.Revision = b.Team(s).Revision;
            e.Health = s.Health;
            e.ApproachUntil = b.Time + 4;
            e.ProgressAt = b.Time;
            e.ProgressPosition = s.Position;
            e.ApproachStarts++;
            s.Destination = s.OrderAnchor = post;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.SupportReady = false;
            s.Job = Job.Reposition;
            e.Reason = rear ? "Цель смотрит в другую сторону: коротко занимаю более выгодное укрытие" : "До первого выстрела занимаю ближайшее более выгодное укрытие";
            b.History.Event(b, "approachBeforeFire", s.Id, c.EnemyId, s.Position, post, 0, e.Reason);
            return true;
        }

        public static void End(BattleSimulation b, Soldier s, bool completed, string reason)
        {
            var e = s.Engagement;
            e.ApproachUntil = 0;
            e.Route.Clear();
            e.Cursor = 0;
            e.Target = -1;
            e.Reason = reason;
            e.ApproachRetryAt = Math.Max(e.ApproachRetryAt, b.Time + 8);
            if (completed)
                e.ApproachCompleted++;
            else
                e.ApproachAborted++;
            if (!TacticalOrders.ServiceActive(s) && !s.GrenadeEscape.Active && V2.Distance(s.OrderAnchor, e.Goal) < .1f)
            {
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Destination = s.Position;
            }

            s.NextDecision = b.Time;
            s.Reason = reason;
        }

        public static bool UpdateApproach(BattleSimulation b, Soldier s, float dt)
        {
            var e = s.Engagement;
            if (e.Target < 0)
                return false;
            Contact c;
            string abort = null;
            if (!Active(b, s))
            {
                e.AbortDeadline++;
                abort = "Истекло время короткого сближения";
            }
            else if (b.Team(s).Revision != e.Revision)
            {
                e.AbortOrder++;
                abort = "Командир изменил задачу";
            }
            else if (s.Health < e.Health || b.Time - s.LastIncoming < 3)
            {
                e.AbortThreat++;
                abort = "Обстрел: прекращаю подготовительное сближение";
            }
            else if (FriendNeedsFire(b, s))
            {
                e.AbortBuddyFire++;
                abort = "Товарищам требуется огонь";
            }
            else if (!Eligible(b, s))
            {
                e.AbortTask++;
                abort = "Более приоритетная задача или состояние";
            }

            if (abort != null)
            {
                End(b, s, false, abort);
                return false;
            }

            if (!s.Visible.Contains(e.Target) || !s.Contacts.TryGetValue(e.Target, out c) || b.Time - c.SeenAt > b.Config.PerceptionPeriod * 2)
            {
                e.AbortTarget++;
                End(b, s, false, "Личное наблюдение потеряно");
                return false;
            }

            if (V2.Dot(c.ObservedFacing.Normalized, (s.Position - c.Position).Normalized) > .35f || b.Time - c.FireSeenAt < 1 && (!NewChoice(b, s) || V2.Dot(c.ObservedFacing.Normalized, (s.Position - c.Position).Normalized) >= -.3f))
            {
                e.AbortThreat++;
                End(b, s, false, "Противник реагирует: готов к открытию огня");
                return false;
            }

            if (V2.Distance(s.Position, e.Goal) < .35f || e.Cursor >= e.Route.Count)
            {
                End(b, s, true, "Позиция занята: открываю огонь при наличии цели");
                return false;
            }

            if (V2.Distance(s.Position, e.ProgressPosition) > .3f)
            {
                e.ProgressPosition = s.Position;
                e.ProgressAt = b.Time;
            }

            if (b.Time - e.ProgressAt > 1.2f)
            {
                e.AbortBlocked++;
                End(b, s, false, "Путь перекрыт: прекращаю ожидание первого выстрела");
                return false;
            }

            V2 delta = e.Route[e.Cursor] - s.Position;
            if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt), out var next))
            {
                s.DistanceTravelled += V2.Distance(s.Position, next);
                s.Position = next;
                s.LastMoved = b.Time;
                s.Moving = true;
                s.State = ActionState.Move;
                s.Facing = (c.Position - s.Position).Normalized;
                if (V2.Distance(next, e.Route[e.Cursor]) < .06f)
                    e.Cursor++;
            }

            s.Reason = e.Reason;
            s.FireStatus = "Откладывает первый выстрел ради короткого смещения";
            return true;
        }
    }
}
