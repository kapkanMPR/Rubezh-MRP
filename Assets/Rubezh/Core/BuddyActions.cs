using System;

namespace SilentOrders.Core
{
    [Serializable]
    public sealed class BuddyActionState
    {
        public int IncomingHelper = -1;
        public int Kind,
            Target = -1,
            Revision,
            Starts,
            Completed,
            Cancelled,
            ReloadStarts,
            ReassureStarts,
            ApproachStarts,
            ApproachArrivals,
            ReloadNearbyTicks,
            ReloadFreeTicks;
        public float Until,
            NextAt,
            Worked,
            SavedReloadSeconds,
            MoraleRestored;
        public bool ReloadAssisted,
            Approaching;
        public float GoalX,
            GoalY;
    }

    // Experimental cooperative actions. Costs are paid by a nearby stationary helper;
    // this module never creates ammunition, reveals enemies or changes red decisions.
    public static class BuddyActions
    {
        public const int FeatureMask = 7; // 1: loader; 2: reassurance; 4: safe short approach

        public static bool Active(Soldier s) => s.Side == Side.Green && s.BuddyAction.Target >= 0;

        static Soldier Unit(BattleSimulation b, int id) => id >= 0 && id < b.Soldiers.Count ? b.Soldiers[id] : null;

        static bool Body(BattleSimulation b, Soldier s) =>
            s != null
            && !(SupplyRecovery.Active(s) || HandLauncher.Active(s))
            && s.Side == Side.Green
            && s.Alive
            && s.Deployed
            && !s.Incapacitated
            && s.RescuerId < 0
            && s.RescueTargetId < 0
            && s.RescuePhase == 0
            && s.TreatmentUntil <= 0
            && s.SupplyDonorId < 0
            && s.SupplyReceiverId < 0
            && !s.GrenadeEscape.Active
            && !s.VehicleDisperse
            && !s.VehicleRally
            && !PairExchange.Active(b, s)
            && (!MotionCombat.HasRoute(s) || s.ReloadUntil > b.Time)
            && (!s.Moving || Active(s) && s.BuddyAction.Approaching);

        static bool Free(BattleSimulation b, Soldier s) =>
            Body(b, s)
            && !s.Broken
            && s.Suppression < .4f
            && !s.Bleeding
            && s.BuddyAction.IncomingHelper < 0
            && s.ReloadUntil <= b.Time
            && s.HandsBusyUntil <= b.Time
            && s.Ammo > 0
            && !Active(s)
            && b.Time >= s.BuddyAction.NextAt
            && !AreaSuppression.Reserved(b, s)
            && !CounterSniper.Reserved(b, s)
            && !ProgressRecovery.Active(b, s)
            && !(s.HelpTargetId >= 0 && s.HelpUntil > b.Time)
            && !(s.Assignment.Active && s.Assignment.IsBound);

        static bool Reach(BattleSimulation b, Soldier helper, Soldier target)
        {
            if (InteractionReach.Can(b, helper.Position, target.Position, 1.4f))
                return true;
            if (
                (FeatureMask & 4) == 0
                || V2.Distance(helper.Position, target.Position) > 4
                || b.Time - helper.LastIncoming < 3
                || b.Time - helper.LastFire < 1
            )
                return false;
            V2 goal = target.Position + (helper.Position - target.Position).Normalized * 1.1f;
            if (
                !InteractionReach.Can(b, goal, target.Position, 1.4f)
                || !Locomotion.WalkableSegment(b, helper.Position, goal)
            )
                return false;
            if (
                target.ReloadUntil > b.Time
                && target.ReloadUntil - b.Time
                    < V2.Distance(helper.Position, goal) / Math.Max(.1f, CombatMovement.Speed(b, helper)) + 2
            )
                return false;
            return SafeSegment(b, helper, goal);
        }

        static bool SafeSegment(BattleSimulation b, Soldier helper, V2 goal)
        {
            float original = PositionLogic.Exposure(b, helper, helper.Position);
            for (int i = 1; i <= 6; i++)
                if (
                    PositionLogic.Exposure(b, helper, helper.Position + (goal - helper.Position) * (i / 6f))
                    > Math.Min(1f, original + .05f)
                )
                    return false;
            return true;
        }

        public static bool CanStart(BattleSimulation b, Soldier helper, Soldier target, int kind)
        {
            // Cheap need check before any geometry; no search if there is no service to perform.
            if (helper == null || target == null)
                return false;
            if (kind == 1)
            {
                if (!CombatTraining.Skill(helper, 2, "assist") || !CombatTraining.Skill(target, 2, "assist"))
                    return false;
                if (
                    (FeatureMask & 1) == 0
                    || !target.GunMachineGunner
                    || target.Broken
                    || target.Suppression >= .55f
                    || target.ReloadUntil <= b.Time + 1.5f
                    || target.BuddyAction.ReloadAssisted
                )
                    return false;
            }
            else if (kind == 2)
            {
                if (
                    (FeatureMask & 2) == 0
                    || helper.Morale < .45f
                    || target.Morale >= .2f
                    || b.Time < target.MoraleRecoveryBlockedUntil
                    || target.Suppression >= .3f
                    || b.Time - target.LastIncoming <= 4
                    || b.Time - helper.LastIncoming <= 4
                    || b.Time - helper.LastFire <= 2
                )
                    return false;
            }
            else
                return false;
            if (
                helper == target
                || target.BuddyAction.IncomingHelper >= 0
                || !Free(b, helper)
                || !Body(b, target)
                || Active(target)
                || !b.Green.Contains(target)
                || TacticalOrders.Emergency(b.Green)
                || b.Green.Style == CombatStyle.Withdraw
                || helper.BuddyId != target.Id
                || !Reach(b, helper, target)
            )
                return false;
            return kind == 1
                || PositionLogic.Protected(b, target, target.Position)
                    && PositionLogic.Protected(b, helper, helper.Position);
        }

        public static bool TryStart(BattleSimulation b, Soldier helper, Soldier target, int kind)
        {
            if (!CanStart(b, helper, target, kind))
                return false;
            target.BuddyAction.IncomingHelper = helper.Id;
            var a = helper.BuddyAction;
            a.Kind = kind;
            a.Target = target.Id;
            a.Revision = b.Green.Revision;
            a.Until = b.Time + (kind == 1 ? 4 : 2.5f);
            a.Worked = 0;
            a.Starts++;
            a.Approaching = !InteractionReach.Can(b, helper.Position, target.Position, 1.4f);
            if (a.Approaching)
            {
                V2 goal = target.Position + (helper.Position - target.Position).Normalized * 1.1f;
                a.GoalX = goal.X;
                a.GoalY = goal.Y;
                a.Until = b.Time + 3;
                a.ApproachStarts++;
                CombatCrouch.Rise(b, helper);
            }

            if (kind == 1)
            {
                if (!CombatTraining.Skill(helper, 2, "assist") || !CombatTraining.Skill(target, 2, "assist"))
                    return false;
                if (!a.Approaching)
                    target.BuddyAction.ReloadAssisted = true;
                a.ReloadStarts++;
            }
            else
                a.ReassureStarts++;
            b.Diagnostics.Add(
                b,
                helper,
                "start",
                "BuddyAction",
                kind == 1 ? "Помощь с перезарядкой" : "Восстановление напарника"
            );
            helper.SupportReady = false;
            helper.Reason =
                kind == 1 ? "Помогаю напарнику перезарядить пулемёт" : "Помогаю напарнику прийти в себя в укрытии";
            return true;
        }

        public static void End(BattleSimulation b, Soldier helper, bool complete)
        {
            var a = helper.BuddyAction;
            if (!Active(helper))
                return;
            b.Diagnostics.Add(
                b,
                helper,
                complete ? "finish" : "cancel",
                "BuddyAction",
                "Kind=" + a.Kind + "; target=" + a.Target
            );
            if (complete)
                a.Completed++;
            else
                a.Cancelled++;
            var target = Unit(b, a.Target);
            if (target != null && target.BuddyAction.IncomingHelper == helper.Id)
                target.BuddyAction.IncomingHelper = -1;
            a.Target = -1;
            a.Kind = 0;
            a.Approaching = false;
            a.NextAt = b.Time + 8;
            helper.NextDecision = b.Time;
            helper.SupportReady = false;
        }

        public static bool Valid(BattleSimulation b, Soldier helper)
        {
            var a = helper.BuddyAction;
            var target = Unit(b, a.Target);
            return Active(helper)
                && Body(b, helper)
                && Body(b, target)
                && target.BuddyAction.IncomingHelper == helper.Id
                && !helper.Broken
                && !helper.Bleeding
                && helper.Suppression < .55f
                && helper.ReloadUntil <= b.Time
                && helper.HandsBusyUntil <= b.Time
                && b.Green.Revision == a.Revision
                && !TacticalOrders.Emergency(b.Green)
                && b.Green.Style != CombatStyle.Withdraw
                && (
                    a.Approaching
                        ? V2.Distance(helper.Position, target.Position) <= 4
                            && b.Time < a.Until
                            && b.Time - helper.LastIncoming > 3
                        : InteractionReach.Can(b, helper.Position, target.Position, 1.4f)
                )
                && (
                    a.Kind == 1
                        ? !target.Broken && target.Suppression < .65f
                        : helper.Morale >= .45f
                            && target.Morale < .2f
                            && b.Time >= target.MoraleRecoveryBlockedUntil
                            && target.Suppression < .3f
                            && b.Time - target.LastIncoming > 4
                            && b.Time - helper.LastIncoming > 4
                            && PositionLogic.Protected(b, target, target.Position)
                );
        }

        public static bool Hold(BattleSimulation b, Soldier s, float dt = 0)
        {
            if (!Active(s))
                return false;
            if (!Valid(b, s))
            {
                End(b, s, false);
                return false;
            }

            s.SupportReady = false;
            s.Moving = false;
            s.State = ActionState.Observe;
            s.Reason =
                s.BuddyAction.Kind == 1
                    ? "Помогаю напарнику перезарядить пулемёт"
                    : "Помогаю напарнику прийти в себя в укрытии";
            var a = s.BuddyAction;
            if (a.Approaching)
            {
                var target = Unit(b, a.Target);
                V2 goal = new V2(a.GoalX, a.GoalY);
                if (
                    !SafeSegment(b, s, goal)
                    || !Locomotion.WalkableSegment(b, s.Position, goal)
                    || a.Kind == 1 && target.ReloadUntil <= b.Time + 1
                )
                {
                    End(b, s, false);
                    return false;
                }

                if (InteractionReach.Can(b, s.Position, target.Position, 1.4f))
                {
                    a.Approaching = false;
                    a.ApproachArrivals++;
                    a.Until = b.Time + (a.Kind == 1 ? 4 : 2.5f);
                    if (a.Kind == 1)
                        target.BuddyAction.ReloadAssisted = true;
                }
                else if (GroupMovement.AllowStep(b, s) && dt > 0)
                {
                    V2 delta = goal - s.Position;
                    V2 next;
                    if (
                        Locomotion.TryStep(
                            b,
                            s,
                            delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt),
                            out next
                        )
                    )
                    {
                        float moved = V2.Distance(s.Position, next);
                        s.Position = next;
                        s.DistanceTravelled += moved;
                        s.Moving = moved > .00001f;
                        if (s.Moving)
                            s.LastMoved = b.Time;
                        s.State = ActionState.Move;
                    }
                }
            }

            return true;
        }

        public static void Tick(BattleSimulation b, float dt)
        {
            foreach (var helper in b.Soldiers)
                if (Active(helper))
                {
                    var a = helper.BuddyAction;
                    var target = Unit(b, a.Target);
                    if (!Valid(b, helper))
                    {
                        End(b, helper, false);
                        continue;
                    }

                    if (a.Approaching)
                        continue;
                    if (a.Kind == 1)
                    {
                        if (target.ReloadUntil <= b.Time)
                        {
                            End(b, helper, true);
                            continue;
                        }

                        float saved = Math.Min(
                            Math.Max(0, target.ReloadUntil - b.Time),
                            Math.Min(.5f * dt, Math.Max(0, 1.5f - a.Worked))
                        );
                        target.ReloadUntil -= saved;
                        a.SavedReloadSeconds += saved;
                        a.Worked += saved;
                        if (b.Time >= a.Until)
                            End(b, helper, true);
                    }
                    else if (b.Time >= a.Until)
                    {
                        float before = target.Morale;
                        target.Morale = Math.Min(.4f, target.Morale + .12f);
                        a.MoraleRestored += target.Morale - before;
                        End(b, helper, true);
                    }
                }

            foreach (var helper in b.Soldiers)
                if (helper.Side == Side.Green && !Active(helper))
                {
                    var target = Unit(b, helper.BuddyId);
                    if (target == null)
                        continue;
                    if (
                        target.GunMachineGunner
                        && target.ReloadUntil > b.Time
                        && V2.Distance(helper.Position, target.Position) <= 4
                    )
                    {
                        helper.BuddyAction.ReloadNearbyTicks++;
                        if (Free(b, helper))
                            helper.BuddyAction.ReloadFreeTicks++;
                    }

                    if (!TryStart(b, helper, target, 1))
                        TryStart(b, helper, target, 2);
                }
        }
    }
}
