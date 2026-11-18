using System;

namespace SilentOrders.Core
{
    public static class MedicalInventory
    {
        public static bool HasAid(Soldier s)
        {
            return s.Tourniquets > 0 || s.Medkits > 0;
        }

        public static int Choose(Soldier helper, Soldier patient)
        {
            bool tourniquet = helper.Tourniquets > 0 || patient.Tourniquets > 0, kit = helper.Medkits > 0 || patient.Medkits > 0;
            if (patient.Bleeding && tourniquet)
                return 1;
            if (kit && patient.Health <= 65f)
                return 2;
            return tourniquet ? 1 : kit ? 2 : 0;
        }

        public static float AidTime(Soldier s) => s.AidSeconds * (s.Side == Side.Green && s.Role == Role.Medic ? .9f : 1f);
        public static float Seconds(Soldier helper, Soldier patient)
        {
            return AidTime(helper) * (Choose(helper, patient) == 2 ? 3f : 1f);
        }

        public static bool Start(BattleSimulation b, Soldier helper, Soldier patient)
        {
            int kind = Choose(helper, patient);
            if (helper.HandsBusyUntil > b.Time || kind == 0 || !helper.Alive || !patient.Alive || helper.Side != patient.Side || !InteractionReach.Can(b, helper.Position, patient.Position, 1.4f))
                return false;
            Soldier donor = kind == 1 ? (patient.Tourniquets > 0 ? patient : helper) : (helper.Medkits > 0 ? helper : patient);
            helper.TreatmentKind = kind;
            helper.TreatmentDonorId = donor.Id;
            helper.TreatmentUntil = b.Time + AidTime(helper) * (kind == 2 ? 3f : 1f);
            CareExperiments.Start(b, helper, patient);
            helper.Reason = (helper == patient ? "Помогаю себе: " : "Помогаю " + patient.Name + ": ") + (kind == 2 ? "аптечка, долгая перевязка" : "жгут, быстрая помощь");
            b.Say(helper, kind == 2 ? "Аптечку применяю, прикройте" : "Накладываю жгут", urgent: true);
            return true;
        }

        public static void Complete(BattleSimulation b, Soldier helper, Soldier patient)
        {
            if (helper.TreatmentUntil <= 0f || b.Time + 0.0001f < helper.TreatmentUntil || helper.TreatmentKind == 0 || !helper.Alive || !patient.Alive || helper.Side != patient.Side || !InteractionReach.Can(b, helper.Position, patient.Position, 1.4f))
                return;
            if (helper.TreatmentDonorId < 0 || helper.TreatmentDonorId >= b.Soldiers.Count)
                return;
            Soldier donor = b.Soldiers[helper.TreatmentDonorId];
            if (donor != helper && donor != patient)
                return;
            bool kit = helper.TreatmentKind == 2;
            if (kit ? donor.Medkits <= 0 : donor.Tourniquets <= 0)
                return;
            if (kit)
                donor.Medkits--;
            else
                donor.Tourniquets--;
            CareExperiments.Completed(patient, helper.TreatmentKind);
            helper.Care.Patient = -1;
            helper.TreatmentKind = 0;
            helper.TreatmentDonorId = -1;
            if (helper != patient)
            {
                helper.HelpActions++;
                if (patient.Incapacitated || patient.Bleeding)
                    helper.SavedComrades.Add(patient.Id);
            }
            else
                helper.SelfTreatments++;
            float restored = Math.Min((kit ? 50f : 22f) * (helper.Side == Side.Green && helper.Role == Role.Medic ? 1.1f + .05f * helper.ClassRank : 1f), 100f - patient.Health);
            patient.Bleeding = false;
            patient.CasualtyRecovery.Reset();
            patient.Stabilized = true;
            patient.Health += restored;
            if (patient.Health >= 25f)
                patient.Incapacitated = false;
            if (helper == patient)
            {
                patient.OrderAnchor = patient.Position;
                patient.Destination = patient.Position;
                patient.Job = Job.Cover;
                patient.MoveAuthorized = true;
            }

            patient.AidCooldown = b.Time + 18f;
            patient.RecoverUntil = b.Time + 3f;
            MoraleDynamics.Restore(b, patient, .08f);
            b.Stats.Treatments++;
            if (kit)
                b.Say(helper, "Перевязка готова", urgent: true);
            else
                Dialogue.Say(b, helper, 25, urgent: true);
            b.Log(helper.Side, helper.Name + ": помощь " + (helper == patient ? "себе" : patient.Name) + " (+" + restored.ToString("0") + " здоровья, 1 " + (kit ? "аптечка" : "жгут") + ").");
        }
    }
}

namespace SilentOrders.Core
{
    // Runs only at spawn; planning includes the mission's reinforcement roster.
    public static class EnemyInventory
    {
        public static int Cost(Soldier s)
        {
            return s.SupplyPack ? (s.PackMagazines+s.PackM4Magazines+s.PackM24Magazines) * 5 + (s.PackBoxes+s.PackM249Boxes) * 50 + s.PackRockets * 35 + s.PackRshg * 50 + s.PackSmokes * 20 + s.PackF1 * 30 + s.PackRGN * 35 + s.PackRGO * 40 + s.PackGrenades * 25 + s.PackUnderbarrel * 30 : s.Role == Role.Medic ? System.Math.Max(0, s.Tourniquets - 1) * 40 + s.Medkits * 60 : 0;
        }

        public static void Configure(Soldier s, System.Collections.Generic.IEnumerable<Soldier> roster)
        {
            AmmoLogistics.ClearTyped(s);
            s.PackRshg = s.PackRGN = s.PackRGO = s.PackF1 = s.PackMagazines = s.PackBoxes = s.PackRockets = s.PackSmokes = s.PackGrenades = s.PackUnderbarrel = 0;
            s.Tourniquets = s.Role == Role.Medic ? 3 : 1;
            s.Medkits = s.Role == Role.Medic ? 1 : 0;
            if (!s.SupplyPack)
                return;
            int rifles = 0, mg = 0, launch = 0, rshg = 0, smoke = 0, rgd = 0, ugl = 0, f1 = 0, rgn = 0, rgo = 0;
            foreach (var r in roster)
            {
                if (r.Role == Role.MachineGunner)
                    mg++;
                else
                    rifles++;
                if (r.Role == Role.Grenadier)
                {
                    if (r.LauncherKind == 1)
                        rshg++;
                    else
                        launch++;
                }

                if (r.SmokeCharges > 0)
                    smoke++;
                if (GrenadePockets.Count(r, 1) > 0)
                    rgd++;
                if (GrenadePockets.Count(r, 3) > 0)
                    f1++;
                if (GrenadePockets.Count(r, 4) > 0)
                    rgn++;
                if (GrenadePockets.Count(r, 5) > 0)
                    rgo++;
                if (GrenadePockets.Count(r, 2) > 0 && (r.WeaponVariant == 2 || r.WeaponVariant == 3))
                    ugl++;
            }

            int budget = 150;
            s.PackMagazines = System.Math.Min(6, rifles * 2);
            budget -= s.PackMagazines * 5;
            if (mg > 0 && budget >= 50)
            {
                s.PackBoxes = 1;
                budget -= 50;
            }

            if (launch > 0 && budget >= 35)
            {
                s.PackRockets = 1;
                budget -= 35;
            }

            if (rshg > 0 && budget >= 50)
            {
                s.PackRshg++;
                budget -= 50;
            }

            if (ugl > 0 && budget >= 30)
            {
                s.PackUnderbarrel = 1;
                budget -= 30;
            }

            if (f1 > 0 && budget >= 30)
            {
                s.PackF1++;
                budget -= 30;
            }

            if (rgn > 0 && budget >= 35)
            {
                s.PackRGN++;
                budget -= 35;
            }

            if (rgo > 0 && budget >= 40)
            {
                s.PackRGO++;
                budget -= 40;
            }

            if (rgd > 0 && budget >= 25)
            {
                s.PackGrenades = 1;
                budget -= 25;
            }

            if (smoke > 0 && budget >= 20)
            {
                s.PackSmokes = 1;
                budget -= 20;
            }

            // One more useful refill before topping up conventional ammunition.
            if (launch > 0 && budget >= 35)
            {
                s.PackRockets++;
                budget -= 35;
            }

            if (mg > 1 && budget >= 50)
            {
                s.PackBoxes++;
                budget -= 50;
            }

            if (rshg > 0 && budget >= 50)
            {
                s.PackRshg++;
                budget -= 50;
            }

            if (ugl > 0 && budget >= 30)
            {
                s.PackUnderbarrel++;
                budget -= 30;
            }

            if (f1 > 0 && budget >= 30)
            {
                s.PackF1++;
                budget -= 30;
            }

            if (rgn > 0 && budget >= 35)
            {
                s.PackRGN++;
                budget -= 35;
            }

            if (rgo > 0 && budget >= 40)
            {
                s.PackRGO++;
                budget -= 40;
            }

            if (rgd > 0 && budget >= 25)
            {
                s.PackGrenades++;
                budget -= 25;
            }

            s.PackMagazines += System.Math.Min(budget / 5, System.Math.Max(0, rifles * 3 - s.PackMagazines));
        }
    }
}
