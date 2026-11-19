using System;

namespace SilentOrders.Core
{
    public sealed class ExecutionMemory
    {
        public float NextSample,
            SampleAt,
            StalledSince = -1,
            NextRepair,
            FailedUntil,
            NextReturn;
        public int FailedMapRevision = -1,
            FailedOrderRevision = -1;
        public int Repairs,
            RejectedRepeats,
            ReturnedTasks,
            CommanderReviews,
            ReleasedSupport;
        public float WorkingSeconds,
            ServiceSeconds,
            PressureSeconds,
            ObservationSeconds,
            BlockedSeconds,
            WaitSeconds,
            NoSectorSeconds;
        public string Cause = "Начало",
            LastRepair = "";
        public V2 FailedGoal;
    }

    // Observe actual execution without issuing a competing route or reading hidden enemies.
    public static class ExecutionReview
    {
        public static bool RejectRepeatedGoal(BattleSimulation b, Soldier s, Job job, V2 goal)
        {
            var e = s.Execution;
            var q = b.Team(s);
            if (
                TacticalOrders.Emergency(q)
                || q.Style == CombatStyle.Withdraw
                || ExecutionReview.ProtectedTask(b, s)
                || job != Job.Advance
                    && job != Job.Flank
                    && job != Job.Reposition
                    && job != Job.Cover
                    && job != Job.Hold
                    && job != Job.Command
            )
                return false;
            bool reject =
                e.FailedUntil > b.Time
                && e.FailedMapRevision == b.Map.Revision
                && e.FailedOrderRevision == q.Revision
                && V2.Distance(goal, e.FailedGoal) < 1.2f;
            if (reject)
            {
                e.RejectedRepeats++;
                s.Ai.Rejected = "Этот маршрут недавно не дал продвижения";
            }

            return reject;
        }

        public static bool ReservesGoal(BattleSimulation b, Soldier s)
        {
            if (!s.Available || !s.Deployed)
                return false;
            bool route = s.PathCursor < s.Path.Count;
            return route
                    && (b.Time - s.Progress.ProgressAt < 8 || s.OrderUntil > b.Time || TacticalOrders.ServiceActive(s))
                || !route && s.OrderUntil > b.Time && V2.Distance(s.OrderAnchor, s.Position) > .6f;
        }

        public static bool WorkingSector(BattleSimulation b, Soldier s)
        {
            foreach (var c in s.Contacts.Values)
            {
                if (!PositionLogic.CanEngage(b, s, s.Position, c) || b.FriendlyInLane(s, c.Position))
                    continue;
                V2 muzzle;
                if (!Ballistics.TryMuzzle(b, s, c.Position, out muzzle))
                    continue;
                bool visual =
                    c.Visual
                    && !c.Inferred
                    && s.Visible.Contains(c.EnemyId)
                    && b.Time - c.SeenAt <= b.Config.PerceptionPeriod * 2;
                if (visual)
                    return true;
                if (
                    !s.Sniper
                    && c.Visual
                    && !c.Inferred
                    && b.Time - c.SeenAt <= (s.GunMachineGunner ? 7 : 4)
                    && CombatCoordination.CanSuppress(b, s)
                )
                    return true;
            }

            return false;
        }

        public static bool ProtectedTask(BattleSimulation b, Soldier s)
        {
            return ContactMovement.Active(b, s)
                || ContactMovement.Supporting(b, s)
                || (SupplyRecovery.Active(s) || HandLauncher.Active(s))
                || BuddyActions.Active(s)
                || !PositionLogic.ShelterEligible(b, s)
                || SniperTactics.OwnsSpecialPost(b, s)
                || EngagementPolicy.Active(b, s)
                || s.LootTargetId >= 0
                || s.LootGroundId >= 0
                || s.LauncherPickupId >= 0
                || s.WeaponAidTargetId >= 0
                || s.PriorityMovement.Transfer
                || AreaSuppression.Reserved(b, s)
                || ProgressRecovery.Active(b, s)
                || PositionLogic.OwnsShelter(s) && s.Tactical.ShelterUntil > b.Time && s.PathCursor < s.Path.Count
                || b.Team(s).Breakthrough.Active
                || b.Team(s).CommandTransition.Waiting
                || s.PairRegroupUntil > b.Time;
        }

        public static bool UsefulWatch(BattleSimulation b, Soldier s)
        {
            if (WorkingSector(b, s) || b.Time - s.LastFire < 8)
                return true;
            foreach (var a in b.Soldiers)
                if (
                    a != s
                    && b.Team(s).Contains(a)
                    && a.Available
                    && a.Moving
                    && PositionLogic.UsefulSupport(b, s, a, a.Destination)
                )
                    return true;
            return false;
        }

        public static void Sample(BattleSimulation b, Soldier s)
        {
            var e = s.Execution;
            if (b.Time < e.NextSample)
                return;
            float dt = e.NextSample == 0 ? 0 : Math.Min(1, b.Time - e.SampleAt);
            e.SampleAt = b.Time;
            e.NextSample = b.Time + .5f;
            bool stalled = false;
            if (TacticalOrders.ServiceActive(s) || s.Busy || s.HandsBusyUntil > b.Time || s.ReloadUntil > b.Time)
            {
                e.Cause = "Занят обслуживанием";
                e.ServiceSeconds += dt;
            }
            else if (!s.Available || s.Broken || s.Suppression > .6f || b.Time - s.LastIncoming < 3)
            {
                e.Cause = "Угроза или состояние";
                e.PressureSeconds += dt;
            }
            else if (b.Time - s.Progress.ProgressAt < 2 || UsefulWatch(b, s))
            {
                e.Cause = "Полезное действие или прикрытие";
                e.WorkingSeconds += dt;
            }
            else if (ProtectedTask(b, s))
            {
                e.Cause = "Специальная задача";
                e.ServiceSeconds += dt;
            }
            else if (s.PathCursor < s.Path.Count && s.MoveAuthorized)
            {
                e.Cause = "Нет продвижения по маршруту";
                e.BlockedSeconds += dt;
                stalled = true;
            }
            else if (s.PathCursor < s.Path.Count || s.State == ActionState.WaitCover)
            {
                e.Cause = "Нет подтверждённого прикрытия";
                e.WaitSeconds += dt;
                stalled = true;
            }
            else if (TacticalOrders.ClosestKnown(b, s) == null)
            {
                var q = b.Team(s);
                var pair = CommandHierarchy.Pair(q, s.Assignment.PairId);
                bool expected =
                    q.Side == Side.Green
                    && q.Style != CombatStyle.Passive
                    && q.Style != CombatStyle.Withdraw
                    && CommandHierarchy.Owns(q)
                    && pair != null
                    && (pair.Duty == PairDuty.Advance || pair.Duty == PairDuty.Maneuver)
                    && V2.Distance(s.Position, pair.Goal) > 3
                    && !StraightAdvance.Waiting(b, s);
                e.Cause = expected ? "Нет продвижения к задаче без контакта" : "Наблюдение без известной цели";
                if (expected)
                {
                    e.NoSectorSeconds += dt;
                    stalled = true;
                }
                else
                    e.ObservationSeconds += dt;
            }
            else
            {
                e.Cause = "Нет рабочего сектора";
                e.NoSectorSeconds += dt;
                stalled = true;
            }

            if (stalled)
            {
                if (e.StalledSince < 0)
                    e.StalledSince = b.Time;
            }
            else
                e.StalledSince = -1;
        }
    }
}
