using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class SniperRetreatCover
    {
        public static IEnumerable<Contact> Sources(BattleSimulation b, Soldier s)
        {
            foreach (var c in Knowledge.Contacts(b, s))
                yield return c;
            if (s.GunSniper)
                foreach (var noise in b.Team(s).Observation.NoiseAreas)
                    if (b.Time - noise.SeenAt <= 8)
                        yield return new Contact
                        {
                            EnemyId = -1,
                            Position = noise.Position,
                            SeenAt = noise.SeenAt,
                            FireSeenAt = noise.SeenAt,
                            Confidence = noise.Confidence,
                            Uncertainty = 4.25f,
                            Inferred = true,
                            Reported = true
                        };
        }

        public static bool Holding(BattleSimulation b, Soldier s)
        {
            var g = s.DefenderGroup;
            if (!s.GunSniper || g == null || !g.Pullback || g.Counterattack.Phase != RedCounterattackPhase.Retreat || g.Counterattack.RetreatClear || !AreaSuppression.Enabled(b, s) || !s.Available || !s.Deployed || s.Busy || s.Broken || s.GrenadeEscape.Active || s.CounterSniper.IncomingUntil > b.Time || s.CounterSniper.Phase != CounterSniperPhase.None || s.Suppression >= .6f || s.Ammo + s.Reserve <= 0 || s.HelpRetreatUntil > b.Time || s.RescueTargetId >= 0 || s.SupplyReceiverId >= 0)
                return false;
            // Use only fresh sightings/reports, never the actual hidden enemy position.
            foreach (var c in Sources(b, s))
            {
                float d = V2.Distance(s.Position, c.Position), range = b.WeaponRange(s);
                if (c.Confidence < .35f || b.Time - c.SeenAt > 8 || c.Uncertainty > 5 || d < range * .85f || d > range || !b.Map.LineOfSight(s.Position, c.Position))
                    continue;
                if (b.Time - s.LastIncoming < 1 && !PositionLogic.Protected(b, s, s.Position))
                    continue;
                foreach (var mate in g.Members)
                    if (mate != s && mate.Alive && mate.Deployed && !mate.Incapacitated && V2.Distance(mate.Position, c.Position) < d - 1)
                        return true;
            }

            return false;
        }

        public static bool Hold(BattleSimulation b, Soldier s)
        {
            if (!Holding(b, s))
                return false;
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Destination = s.Position;
            s.Job = Job.Cover;
            s.MoveAuthorized = false;
            s.Reason = "Снайпер прикрывает отход: жду группу у дальней границы стрельбы";
            s.SupportReady = PositionLogic.Able(s, b) && PositionLogic.HasSector(b, s, s.Position);
            return true;
        }
    }
}
