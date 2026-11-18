using System;

namespace SilentOrders.Core
{
    public static class GroupCoordination
    {
        public static bool CanLend(BattleSimulation b, Soldier helper, Soldier victim)
        {
            var group = helper.DefenderGroup;
            if (group == null || group == victim.DefenderGroup)
                return true;
            if (group.Cooperation.Duty == GroupDuty.Recover || group.Cooperation.Mover == helper.Id)
                return false;
            int free = 0;
            foreach (var member in group.Members)
            {
                if (member.HelpTargetId >= 0 && member.HelpUntil > b.Time && member.Id != helper.Id)
                    return false;
                if (member.Available && !member.Busy)
                    free++;
            }

            return free >= 2;
        }

        public static void Share(BattleSimulation b, DefenderGroupState group)
        {
            foreach (var source in b.DefenderGroups)
            {
                if (source == group || !source.Deployed || source.Command.Side != group.Command.Side)
                    continue;
                foreach (var observer in source.Members)
                    if (observer.Alive && observer.Deployed)
                        foreach (var pair in observer.Contacts)
                        {
                            var c = pair.Value;
                            if (c.SeenAt > b.Time || b.Time - c.SeenAt > 8f || c.Confidence < .35f)
                                continue;
                            Contact old;
                            if (group.Command.Contacts.TryGetValue(pair.Key, out old) && old.SeenAt >= c.SeenAt)
                                continue;
                            var report = c.Copy();
                            report.Reported = true;
                            report.Visual = false;
                            report.Confidence *= .85f;
                            group.Command.Contacts[pair.Key] = report;
                        }
            }
        }

        static bool Free(BattleSimulation b, Soldier s)
        {
            return !AreaSuppression.Reserved(b, s) && !CounterSniper.Reserved(b, s) && s.Available && !s.Busy && !s.Broken && !s.VehicleDisperse && !s.VehicleRally && s.SupplyReceiverId < 0 && s.RescuerId < 0 && s.LootGroundId < 0 && s.LootTargetId < 0 && s.LauncherPickupId < 0 && s.WeaponAidTargetId < 0 && s.HandsBusyUntil <= b.Time;
        }

        public static void Assist(BattleSimulation b, DefenderGroupState group)
        {
            if (b.Time < group.NextAssist || group.Available.Count < 2 || group.Cooperation.Duty == GroupDuty.Recover)
                return;
            group.NextAssist = b.Time + 3f;
            foreach (var member in group.Members)
                if (member.HelpUntil > b.Time && member.HelpTargetId >= 0)
                    return;
            Soldier victim = null;
            float score = 0;
            foreach (var other in b.DefenderGroups)
            {
                if (other == group || !other.Deployed)
                    continue;
                int survivors = 0;
                foreach (var s in other.Members)
                    if (s.Available)
                        survivors++;
                bool losses = survivors * 2 <= other.Members.Count;
                foreach (var s in other.Members)
                    if (s.Available && b.Time - s.LastIncoming < 3f && (s.Suppression > .65f || losses && s.Suppression > .35f))
                    {
                        float value = s.Suppression + (losses ? .4f : 0);
                        if (value > score)
                        {
                            score = value;
                            victim = s;
                        }
                    }
            }

            if (victim == null)
                return;
            Soldier helper = null;
            float distance = 18f;
            foreach (var s in group.Members)
                if (Free(b, s) && group.Cooperation.Mover != s.Id && s.Suppression < .5f && s.Ammo + s.Reserve > 0 && s.FlagResponseUntil <= b.Time)
                {
                    float d = V2.Distance(s.Position, victim.Position);
                    if (d < distance)
                    {
                        distance = d;
                        helper = s;
                    }
                }

            if (helper == null)
                return;
            helper.HelpTargetId = victim.Id;
            helper.HelpUntil = b.Time + 7f;
            helper.HelpNextSmoke = helper.HelpNextRoute = 0;
            helper.HelpSmokePreferred = true;
            helper.NextDecision = b.Time;
            victim.HelpRetreatUntil = b.Time + 7f;
            victim.HelpRetreatNextRoute = 0;
            victim.HelpRetreatPlanned = false;
            victim.NextDecision = b.Time;
            // Standard planner validates enemy sectors, throw range, inventory and friendly lanes.
            SmokeTactics.Request(b, group.Command, victim, victim.OrderAnchor, SmokePurpose.Withdrawal, helper);
            b.Log(Side.Red, group.Name + ": прикрыть соседнюю группу и помочь сменить позицию");
        }

        public static bool DefendFlag(BattleSimulation b, DefenderGroupState group)
        {
            if (b.CampaignLevel != 19 && b.CampaignLevel != 20)
                return false;
            bool alarm = b.Capture > .001f;
            foreach (var c in group.Command.Contacts.Values)
                if (b.Time - c.SeenAt < 6f && c.Confidence > .35f && V2.Distance(c.Position, b.Map.Objective) < 7f)
                    alarm = true;
            if (!alarm)
                return false;
            group.Counterattack.Phase = RedCounterattackPhase.Defense;
            // At most two responders per group; other members retain their fire/aid tasks.
            int assigned = 0;
            foreach (var s in group.Members)
                if (s.FlagResponseUntil > b.Time && Free(b, s))
                    assigned++;
            while (assigned < 2)
            {
                Soldier chosen = null;
                float best = float.MaxValue;
                foreach (var s in group.Members)
                    if (Free(b, s) && s.FlagResponseUntil <= b.Time && s.Suppression < .72f && s.Health >= 30)
                    {
                        float d = V2.Distance(s.Position, b.Map.Objective) + (s.Role == Role.Medic ? 6 : 0);
                        if (d < best)
                        {
                            best = d;
                            chosen = s;
                        }
                    }

                if (chosen == null)
                    break;
                V2 goal = chosen.Position;
                bool found = false;
                for (int k = 0; k < 12; k++)
                {
                    float angle = (chosen.Id * 2 + k) * .5235988f;
                    V2 candidate = b.Map.NearestOpen(b.Map.Objective + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 1.55f);
                    if (V2.Distance(candidate, b.Map.Objective) > 2.1f || PositionLogic.Occupied(b, chosen, candidate))
                        continue;
                    var route = Pathfinder.Find(b.Map, chosen.Position, candidate, p => PositionLogic.Exposure(b, chosen, p) * 2 + (PositionLogic.Occupied(b, chosen, p) ? 12 : 0));
                    b.Stats.Paths++;
                    if (route.Count == 0 && V2.Distance(chosen.Position, candidate) > .6f)
                        continue;
                    chosen.Path = route;
                    chosen.PathCursor = 0;
                    goal = candidate;
                    found = true;
                    break;
                }

                if (!found)
                    break;
                chosen.FlagResponseUntil = b.Time + 8f;
                chosen.Destination = chosen.OrderAnchor = goal;
                chosen.Job = Job.Reposition;
                chosen.MoveAuthorized = true;
                chosen.ManeuverPaused = false;
                chosen.NextDecision = b.Time;
                chosen.Reason = "Противник у флага: выдвигаюсь оспаривать захват";
                assigned++;
            }

            group.Command.CommanderOrder = "Не дать захватить флаг: выдвижение под прикрытием";
            return assigned > 0;
        }
    }
}
