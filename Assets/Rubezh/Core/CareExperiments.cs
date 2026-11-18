using System;

namespace SilentOrders.Core
{
    [Serializable]
    public sealed class CareState
    {
        public float TourniquetProgress, KitProgress, Started, Duration, Initial, NextReport;
        public int Patient = -1, Partner = -1, Leader = -1, Revision, Checkpoints, Resumptions, PairStarts, PairCancels, WoundedReports;
        public float PairDistance, NextPair;
    }

    public static class CareExperiments
    {
        // Legacy defaults for future battles; live decisions read b.Rules.
        public static int Mask = 7;
        public static bool Enabled(int bit) => (Mask & (1 << bit)) != 0;
        public static bool Enabled(BattleSimulation b, int bit) => b.Rules.CareEnabled(bit);
        public static bool Conscious(Soldier s) => s.Alive && s.Deployed && s.Incapacitated && !s.Broken && s.Health > 3 && s.Suppression < .85f && s.RescuerId < 0 && s.TreatmentUntil <= 0;
        public static void Start(BattleSimulation b, Soldier helper, Soldier patient)
        {
            if (helper.Side != Side.Green || patient.Side != Side.Green || !CombatTraining.Skill(helper, 2, "resume") || !Enabled(b, 0))
                return;
            var st = helper.Care;
            st.Patient = patient.Id;
            st.Started = b.Time;
            st.Duration = MedicalInventory.AidTime(helper) * (helper.TreatmentKind == 2 ? 3 : 1);
            st.Initial = helper.TreatmentKind == 2 ? patient.Care.KitProgress : patient.Care.TourniquetProgress;
            helper.TreatmentUntil = b.Time + st.Duration * (1 - st.Initial);
            if (st.Initial > 0)
                st.Resumptions++;
        }

        public static void Completed(Soldier patient, int kind)
        {
            if (kind == 2)
                patient.Care.KitProgress = 0;
            else
                patient.Care.TourniquetProgress = 0;
        }

        public static void Tick(BattleSimulation b)
        {
            foreach (var h in b.Soldiers)
            {
                if (h.Side == Side.Green && CombatTraining.Skill(h, 2, "resume") && Enabled(b, 0) && h.Alive && h.TreatmentUntil > 0 && h.TreatmentKind > 0 && h.Care.Patient >= 0 && h.Care.Patient < b.Soldiers.Count)
                {
                    var p = b.Soldiers[h.Care.Patient];
                    if (p.Alive && h.Care.Duration > 0 && InteractionReach.Can(b, h.Position, p.Position, 1.4f))
                    {
                        float progress = Math.Min(2f / 3, (float)Math.Floor((h.Care.Initial + (b.Time - h.Care.Started) / h.Care.Duration) * 3 + .00001f) / 3);
                        float previous = h.TreatmentKind == 2 ? p.Care.KitProgress : p.Care.TourniquetProgress;
                        if (progress > previous)
                        {
                            p.Care.Checkpoints++;
                            if (h.TreatmentKind == 2)
                                p.Care.KitProgress = progress;
                            else
                                p.Care.TourniquetProgress = progress;
                        }
                    }
                }

                if (h.Care.Partner >= 0 && !DuoCarry.Valid(b, h))
                    DuoCarry.Release(b, h);
                if (h.Care.Leader >= 0 && (h.Care.Leader >= b.Soldiers.Count || b.Soldiers[h.Care.Leader].Care.Partner != h.Id))
                    h.Care.Leader = -1;
            }
        }

        public static bool WoundedSense(BattleSimulation b, Soldier s)
        {
            if (s.Side != Side.Green || !Enabled(b, 2) || !Conscious(s) || b.Time < s.Care.NextReport)
                return false;
            s.Care.NextReport = b.Time + 1.5f;
            int before = b.Radio.Count;
            Perception.Observe(b, s);
            s.Care.WoundedReports += b.Radio.Count - before;
            return true;
        }
    }

    public static class DuoCarry
    {
        static bool Ready(BattleSimulation b, Soldier s) => s.Side == Side.Green && CombatTraining.Skill(s, 3, "evac") && s.Alive && s.Deployed && !s.Incapacitated && !s.Broken && !s.Bleeding && s.Health >= 35 && s.Suppression < .65f && !s.GrenadeEscape.Active && s.HandsBusyUntil <= b.Time && !TacticalOrders.Emergency(b.Team(s));
        public static bool Valid(BattleSimulation b, Soldier h)
        {
            if (!CareExperiments.Enabled(b, 1) || h.Care.Partner < 0 || h.Care.Partner >= b.Soldiers.Count || h.RescuePhase != RescuePhase.Drag || h.RescueTargetId < 0 || !Ready(b, h))
                return false;
            var a = b.Soldiers[h.Care.Partner];
            return Ready(b, a) && a.Care.Leader == h.Id && a.Care.Revision == b.Team(a).Revision && b.Soldiers[h.RescueTargetId].Alive && V2.Distance(a.Position, h.Position) < 1.8f && Locomotion.WalkableSegment(b, a.Position, h.Position);
        }

        public static void Release(BattleSimulation b, Soldier h)
        {
            int id = h.Care.Partner;
            h.Care.Partner = -1;
            if (id >= 0)
                h.Care.NextPair = b.Time + 4;
            if (id < 0 || id >= b.Soldiers.Count)
                return;
            var a = b.Soldiers[id];
            if (a.Care.Leader == h.Id)
            {
                a.Care.Leader = -1;
                a.NextDecision = b.Time;
                TacticalAI.Stop(a);
                a.Care.PairCancels++;
            }
        }

        public static bool Join(BattleSimulation b, Soldier h)
        {
            if (Valid(b, h))
                return true;
            if (h.Care.Partner >= 0)
                Release(b, h);
            if (!CareExperiments.Enabled(b, 1) || b.Time < h.Care.NextPair || h.PathCursor >= h.Path.Count || h.RescuePhase != RescuePhase.Drag || h.RescueTargetId < 0 || !Ready(b, h))
                return false;
            foreach (var a in b.Soldiers)
                if (a != h && a.Side == h.Side && a.Available && Ready(b, a) && !TacticalOrders.ServiceActive(a) && !a.VehicleDisperse && !a.VehicleRally && !BuddyActions.Active(a) && !PairExchange.Active(b, a) && !HandLauncher.Active(a) && !SupplyRecovery.Active(a) && a.ReloadUntil <= b.Time && a.Care.Partner < 0 && a.RescueTargetId < 0 && InteractionReach.Can(b, a.Position, h.Position, 1.4f))
                {
                    h.Care.Partner = a.Id;
                    a.Care.Leader = h.Id;
                    a.Care.Revision = b.Team(a).Revision;
                    a.Care.PairStarts++;
                    TacticalAI.Stop(a);
                    a.SupportReady = false;
                    return true;
                }

            return false;
        }

        public static bool Hold(BattleSimulation b, Soldier s)
        {
            if (s.Care.Leader < 0)
                return false;
            var h = b.Soldiers[s.Care.Leader];
            if (!Valid(b, h))
            {
                Release(b, h);
                s.Care.Leader = -1;
                return false;
            }

            s.Moving = s.LastMoved >= b.Time - .0001f;
            s.SupportReady = false;
            s.State = ActionState.Drag;
            s.Reason = "Переносим раненого вдвоём с " + h.Name;
            return true;
        }

        public static float Speed(BattleSimulation b, Soldier h)
        {
            return Valid(b, h) ? Math.Min(CombatMovement.Speed(b, h), CombatMovement.Speed(b, b.Soldiers[h.Care.Partner])) * .70f : CombatMovement.Speed(b, h) * .48f;
        }

        public static bool CanStep(BattleSimulation b, Soldier h, V2 delta)
        {
            if (!Valid(b, h))
                return true;
            var a = b.Soldiers[h.Care.Partner];
            V2 goal = a.Position + delta;
            if (!Locomotion.WalkableSegment(b, a.Position, goal))
                return false;
            foreach (var u in b.Soldiers)
                if (u.Alive && u.Deployed && u != h && u != a && u.Id != h.RescueTargetId && V2.Distance(u.Position, goal) < .5f)
                    return false;
            return true;
        }

        public static void Moved(BattleSimulation b, Soldier h, V2 delta)
        {
            if (!Valid(b, h))
                return;
            var a = b.Soldiers[h.Care.Partner];
            a.Position += delta;
            a.DistanceTravelled += delta.Length;
            a.Moving = delta.Length > 0;
            a.LastMoved = b.Time;
            a.Facing = h.Facing;
            a.Care.PairDistance += delta.Length;
        }
    }

    public static class BodyArmor
    {
        public static readonly string[] Names =
        {
            "Лёгкий бронежилет",
            "Армейский бронежилет",
            "Штурмовой бронежилет"
        };
        public static float DamageMultiplier(int level) => level >= 2 ? .80f : level == 1 ? .88f : level == 0 ? .95f : 1f;
        public static bool BlocksFragment(int level, bool fragment, Rng random) => fragment && level >= 1 && random.Value() < (level >= 2 ? .50f : .15f);
        public static float Multiplier(int level) => 1 - .05f * Math.Max(0, Math.Min(2, level));
    }
}
