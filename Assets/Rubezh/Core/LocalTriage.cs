using System;

namespace SilentOrders.Core
{
    // Emergency nearby treatment is checked before the slower squad planner.
    // A valid rescuer takes over instead of leaving the casualty waiting for orders.
    public static class LocalTriage
    {
        public static void Update(BattleSimulation b)
        {
            foreach (var patient in b.Soldiers)
            {
                if (
                    !patient.Alive
                    || !patient.Deployed
                    || patient.RescuerId >= 0
                    || patient.TreatmentUntil > 0f
                    || (!patient.Bleeding && patient.Health > 40f)
                )
                    continue;
                var team = b.Team(patient);
                if (TacticalOrders.Emergency(team) || RedCounterattack.Retreating(b, patient))
                    continue;
                Soldier helper = null;
                float best = 1.1f;
                foreach (var candidate in b.Soldiers)
                {
                    if (
                        candidate.Id == patient.Id
                        || candidate.Side != patient.Side
                        || RedCounterattack.Retreating(b, candidate)
                        || !candidate.Available
                        || candidate.Broken
                        || candidate.Suppression > .85f
                        || candidate.Health < 25f
                        || !candidate.Deployed
                        || MedicalInventory.Choose(candidate, patient) == 0
                    )
                        continue;
                    float distance = V2.Distance(candidate.Position, patient.Position);
                    if (
                        distance < best
                        && !candidate.Busy
                        && candidate.SupplyReceiverId < 0
                        && InteractionReach.Can(b, candidate.Position, patient.Position, 1.1f)
                    )
                    {
                        best = distance;
                        helper = candidate;
                    }
                }

                if (helper == null)
                    continue;
                TacticalAI.Stop(helper);
                helper.ReloadUntil = 0f; // interrupted reload does not add bullets
                helper.HelpTargetId = -1;
                helper.HelpUntil = 0f;
                helper.RescueTargetId = patient.Id;
                helper.RescuePhase = RescuePhase.Treat;
                helper.RescueSince = b.Time;
                helper.RescueDestination = patient.Position;
                patient.RescuerId = helper.Id;
                helper.SupportReady = false;
                patient.SupportReady = false;
                if (!MedicalInventory.Start(b, helper, patient))
                {
                    helper.RescueTargetId = -1;
                    helper.RescuePhase = RescuePhase.None;
                    patient.RescuerId = -1;
                    continue;
                }

                helper.NextDecision = b.Time;
                patient.NextDecision = b.Time;
                b.Stats.RescueAttempts++;
                b.Log(patient.Side, helper.Name + ": срочно помогает " + patient.Name);
            }
        }
    }
}
