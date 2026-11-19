using System;

namespace SilentOrders.Core
{
    [Serializable]
    public sealed class GreenMechanicsState
    {
        public int RecoveryKind,
            MedicalPickups,
            LauncherPickups,
            TemporaryShots;
        public bool MedicalBag,
            MedicalTaken,
            HandLauncher;
        public float HandUntil,
            HandNextPlan;
        public int HandRevision;
        public int RecoveryTarget = -1,
            PackOwner = -1,
            RecoveryRevision;
        public float RecoveryUntil,
            RecoveryNext,
            UnpackAt = -1,
            GoalX,
            GoalY;
        public float AreaStartAt,
            AreaStartX,
            AreaStartY,
            AreaProgress;
        public int AreaUseful,
            AreaStalled;
        public int AreaStarts,
            AreaShots,
            RecoveryStarts,
            RecoveryCompleted,
            RecoveryCancelled,
            RecoveredMagazines,
            RecoveredBoxes;
    }

    public static class GreenMechanics
    {
        public const int FeatureMask = 11;

        public static bool SuppressEnabled(BattleSimulation b, Soldier s)
        {
            return (FeatureMask & 1) != 0
                && s.Side == Side.Green
                && (!s.GunMachineGunner || s.Role == Role.MachineGunner || s.Role == Role.Commander)
                && CombatTraining.Skill(s, 2, "suppress")
                && b.Team(s).Style == CombatStyle.Aggressive;
        }

        public static bool TaskBound(Soldier s) => (FeatureMask & 8) != 0 && s.Side == Side.Green;

        public static bool SuppressionTaskReady(BattleSimulation b, Soldier s)
        {
            if (!TaskBound(s))
                return true;
            return s.Ammo + s.Reserve >= s.MagazineSize * .5f && FireSupportTask.Beneficiary(b, s) != null;
        }

        public static bool RelevantTask(BattleSimulation b, Soldier s, Contact c)
        {
            if (!TaskBound(s))
                return true;
            var mover = FireSupportTask.Beneficiary(b, s);
            return mover != null && FireSupportTask.Threatens(b, c, mover);
        }

        public static void AreaStart(BattleSimulation b, Soldier s, Soldier mover)
        {
            if (s.Side != Side.Green)
                return;
            var a = s.Mechanics;
            a.AreaStarts++;
            a.AreaStartAt = b.Time;
            a.AreaProgress = 0;
            a.AreaStartX = mover == null ? s.Position.X : mover.Position.X;
            a.AreaStartY = mover == null ? s.Position.Y : mover.Position.Y;
        }

        public static bool AreaContinue(BattleSimulation b, Soldier s)
        {
            if (s.Side != Side.Green)
                return true;
            var mover = FireSupportTask.Assigned(b, s);
            var a = s.Mechanics;
            if (mover != null)
                a.AreaProgress = Math.Max(
                    a.AreaProgress,
                    V2.Distance(mover.Position, new V2(a.AreaStartX, a.AreaStartY))
                );
            if (!TaskBound(s))
                return true;
            if (mover == null)
                return false;
            if (b.Time - a.AreaStartAt >= 3 && a.AreaProgress < .6f)
            {
                a.AreaStalled++;
                s.AreaSuppression.Next = Math.Max(s.AreaSuppression.Next, b.Time + 4);
                return false;
            }

            return true;
        }

        public static void AreaEnd(Soldier s)
        {
            if (s.Side == Side.Green && s.AreaSuppression.Firing && s.Mechanics.AreaProgress >= 1)
                s.Mechanics.AreaUseful++;
        }
    }
}
