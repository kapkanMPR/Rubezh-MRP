using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class DefenderGroupState
    {
        public string Name;
        public V2 Spawn;
        public float SpawnTime,
            Radius = 6f;
        public bool Pullback;
        public float NextAssist;
        public readonly HuntState Hunt = new HuntState();
        public readonly GroupCommandState Cooperation = new GroupCommandState();
        public bool Attack,
            LegacyReserveTrigger,
            Deployed,
            VehicleArrival;
        public readonly List<Soldier> Members = new List<Soldier>(5);
        public readonly List<Soldier> Available = new List<Soldier>(5);
        public readonly Squad Command = new Squad { Side = Side.Red };
        public readonly RedCounterattackState Counterattack = new RedCounterattackState();

        public DefenderGroupState()
        {
            Command.DefenderGroup = this;
        }
    }

    public static class DefenderGroupsRuntime
    {
        public static int Pending(BattleSimulation b)
        {
            int n = 0;
            foreach (var g in b.DefenderGroups)
                if (!g.Deployed)
                    n += g.Members.Count;
            return n;
        }

        public static void Update(BattleSimulation b)
        {
            if (!b.CustomDefenders)
                return;
            var previous = b.ActiveDefenderGroup;
            try
            {
                foreach (var g in b.DefenderGroups)
                {
                    if (!g.Deployed)
                    {
                        bool due = b.Time >= g.SpawnTime;
                        if (g.LegacyReserveTrigger && b.Time > 0f)
                        {
                            due |= b.Alive(Side.Red) <= 1;
                            foreach (var s in b.Soldiers)
                                if (s.Side == Side.Green && s.Alive && V2.Distance(s.Position, b.Map.Objective) < 12f)
                                    due = true;
                        }

                        if (!due)
                            continue;
                        if (g.VehicleArrival)
                        {
                            var transport = VehicleArrivals.Start(b, Side.Red, g);
                            if (!transport.Stopped)
                                continue;
                        }

                        Deploy(b, g);
                    }

                    b.ActiveDefenderGroup = g;
                    if (!LastOpponentHunt.GroupActive(b, g))
                        RedCounterattack.Tick(b);
                    if (b.Time < g.Command.NextPlan)
                        continue;
                    var q = g.Command;
                    CommandHierarchy.Maintain(b, q);
                    g.Available.Clear();
                    foreach (var s in g.Members)
                        if (s.Available && !s.VehicleRally && !s.VehicleDisperse)
                            g.Available.Add(s);
                    if (g.Available.Count == 0)
                        continue;
                    if (q.LeaderId < 0 || q.LeaderId >= b.Soldiers.Count || !b.Soldiers[q.LeaderId].Available)
                        q.LeaderId = g.Available[0].Id;
                    q.Contacts.Clear();
                    foreach (var s in g.Members)
                        if (s.Alive)
                            foreach (var c in s.Contacts)
                            {
                                Contact old;
                                if (!q.Contacts.TryGetValue(c.Key, out old) || old.SeenAt < c.Value.SeenAt)
                                    q.Contacts[c.Key] = c.Value;
                            }

                    GroupCoordination.Share(b, g);
                    CommanderCooperation.Review(b, g);
                    CommanderOrders.Review(b, q);
                    TaskReview.Group(b, q);
                    SmokePlanner.Update(b, q);
                    SupportAI.Plan(b, q);
                    SupplyAI.Plan(b, q);
                    if (!LastOpponentHunt.Plan(b, g))
                    {
                        if (!GroupCoordination.DefendFlag(b, g))
                            SquadBrain.Plan(b, q);
                        else
                            q.CommandPlan.Active = false;
                        GroupCoordination.Assist(b, g);
                    }
                    else
                        q.CommandPlan.Active = false;
                    q.NextPlan = b.Time + b.Config.SquadPeriod;
                }
            }
            finally
            {
                b.ActiveDefenderGroup = previous;
            }

            b.CampaignReserve = Pending(b);
        }

        static void Deploy(BattleSimulation b, DefenderGroupState g)
        {
            g.Deployed = true;
            g.Counterattack.Begin(g.Attack ? g.Members.Count : 0);
            g.Counterattack.AssaultWing = 0;
            int disembarkIndex = 0;
            foreach (var s in g.Members)
            {
                s.Rules = b.Rules;
                s.Id = b.Soldiers.Count;
                s.DefenderGroup = g;
                s.Side = Side.Red;
                s.Deployed = true;
                V2 origin = g.VehicleArrival && b.RedVehicle != null ? b.RedVehicle.Center : g.Spawn;
                V2 preferred = g.VehicleArrival
                    ? origin + new V2((s.Id % 2 == 0 ? -2f : 2f), (s.Id % 3 - 1) * 2f)
                    : origin;
                V2 point = g.VehicleArrival ? VehicleArrivals.SafeNear(b, preferred, origin) : OpenSpawn(b, origin);
                s.VehicleRally = g.VehicleArrival && V2.Distance(point, g.Spawn) > 1.05f;
                s.VehicleRouteAt = b.Time;
                s.Position = s.Destination = s.OrderAnchor = point;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
                s.Facing = (b.Map.Entry - point).Normalized;
                s.NextDecision = b.Time;
                WeaponProgression.Assign(
                    b,
                    s,
                    b.EnemyWeaponMission > 0 ? b.EnemyWeaponMission : b.CampaignLevel,
                    b.EnemyWeaponEndless
                );
                b.Soldiers.Add(s);
                if (g.VehicleArrival && b.RedVehicle != null)
                    VehicleArrivals.BeginDispersal(b, s, b.RedVehicle, disembarkIndex++);
            }

            g.Command.LeaderId = g.Members[0].Id;
            for (int i = 0; i < g.Members.Count; i++)
            {
                var s = g.Members[i];
                s.BuddyId = (i % 2 == 0 ? (i + 1 < g.Members.Count ? g.Members[i + 1].Id : s.Id) : g.Members[i - 1].Id);
                s.Element = i % 2;
            }

            b.Log(
                Side.Red,
                g.Name
                    + ": "
                    + g.Members.Count
                    + " бойцов, "
                    + (
                        g.Pullback ? "оттяжка"
                        : g.Attack ? "атака"
                        : "удержание своего рубежа"
                    )
            );
        }

        static V2 OpenSpawn(BattleSimulation b, V2 origin)
        {
            int best = -1;
            float score = float.MaxValue;
            for (int i = 0; i < b.Map.Tiles.Length; i++)
            {
                var p = b.Map.Center(i);
                if (!b.Map.Walkable(p))
                    continue;
                float dist = V2.Distance(p, origin);
                if (dist >= score)
                    continue;
                bool occupied = false;
                foreach (var s in b.Soldiers)
                    if (s.Alive && s.Deployed && V2.Distance(p, s.Position) < .8f)
                    {
                        occupied = true;
                        break;
                    }

                if (!occupied)
                {
                    best = i;
                    score = dist;
                }
            }

            return best < 0 ? b.Map.NearestOpen(origin) : b.Map.Center(best);
        }
    }
}
