using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Geometry is shared by every hand interaction. Smoke is not a physical wall.
    public static class InteractionReach
    {
        public static bool Can(BattleSimulation b, V2 from, V2 to, float radius)
        {
            return V2.Distance(from, to) <= radius && b.Map.LineOfSight(from, to) && Locomotion.WalkableSegment(b, from, to);
        }
    }

    public enum KnowledgeKind
    {
        Visual,
        Report,
        Memory,
        Inferred
    }

    public static class Knowledge
    {
        public static Contact Newer(Contact a, Contact z)
        {
            if (a == null)
                return z;
            if (z == null)
                return a;
            return z.SeenAt > a.SeenAt || z.SeenAt == a.SeenAt && (z.Confidence > a.Confidence || z.Visual && !a.Visual) ? z : a;
        }

        public static KnowledgeKind Kind(BattleSimulation b, Soldier s, Contact c)
        {
            if (c.Visual && s.Visible.Contains(c.EnemyId) && b.Time - c.SeenAt <= b.Config.PerceptionPeriod * 2)
                return KnowledgeKind.Visual;
            if (c.Inferred)
                return KnowledgeKind.Inferred;
            return b.Time - c.SeenAt <= 4 ? KnowledgeKind.Report : KnowledgeKind.Memory;
        }

        // Value-type iterator: exposure/path scoring does not allocate a list per cell.
        public struct ContactSet
        {
            readonly Dictionary<int, Contact> group, personal;
            public ContactSet(Dictionary<int, Contact> g, Dictionary<int, Contact> p)
            {
                group = g;
                personal = p;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(group, personal);
            }

            public struct Enumerator
            {
                Dictionary<int, Contact> group, personal;
                Dictionary<int, Contact>.Enumerator iterator;
                bool second;
                public Contact Current { get; private set; }

                public Enumerator(Dictionary<int, Contact> g, Dictionary<int, Contact> p)
                {
                    group = g;
                    personal = p;
                    iterator = g.GetEnumerator();
                    second = false;
                    Current = null;
                }

                public bool MoveNext()
                {
                    while (true)
                    {
                        if (iterator.MoveNext())
                        {
                            var pair = iterator.Current;
                            Contact c;
                            if (!second)
                            {
                                personal.TryGetValue(pair.Key, out c);
                                Current = Newer(pair.Value, c);
                                return true;
                            }

                            if (!group.ContainsKey(pair.Key))
                            {
                                Current = pair.Value;
                                return true;
                            }
                        }
                        else if (!second)
                        {
                            second = true;
                            iterator = personal.GetEnumerator();
                        }
                        else
                            return false;
                    }
                }
            }
        }

        public static ContactSet Contacts(BattleSimulation b, Soldier s)
        {
            return new ContactSet(b.Team(s).Contacts, s.Contacts);
        }
    }

    public enum AiTaskKind
    {
        None,
        Observe,
        Move,
        Support,
        Capture,
        Medical,
        Supply,
        Pickup,
        Retreat,
        Recover,
        EvadeGrenade
    }

    [Flags]
    public enum AiChannels
    {
        None = 0,
        Movement = 1,
        Weapon = 2,
        Hands = 4
    }

    public sealed class AiTaskState
    {
        public AiTaskKind Kind;
        public AiChannels Channels;
        public int Revision;
        public float Started, LastProgress, NextTrace;
        public V2 LastPosition, Anchor;
        public string Owner = "", Reason = "", Rejected = "";
    }

    public sealed class AiGroupState
    {
        public float NextKnowledge;
        public int Members = -1;
        public readonly List<int> Expired = new List<int>();
    }

    public sealed class AiEvent
    {
        public float Time;
        public int Soldier, Revision;
        public string Event, Task, Reason;
        public V2 Position, Destination;
    }

    public sealed class AiDiagnostics
    {
        public bool Enabled;
        public readonly List<AiEvent> Events = new List<AiEvent>(512);
        public int DroppedEvents;
        public int Transitions, Interruptions, RejectedOrders, SmokeRequests, SmokeRejected, SmokeLaunched, SmokeEffective, SmokeIneffective;
        public void Add(BattleSimulation b, Soldier s, string kind, string task, string reason)
        {
            if (!Enabled)
                return;
            if (Events.Count >= 8192)
            {
                Events.RemoveRange(0, 256);
                DroppedEvents += 256;
            }

            Events.Add(new AiEvent { Time = b.Time, Soldier = s == null ? -1 : s.Id, Revision = s == null ? 0 : s.OrderRevision, Event = kind, Task = task, Reason = reason, Position = s == null ? new V2() : s.Position, Destination = s == null ? new V2() : s.OrderAnchor });
        }
    }

    public static class AiTasks
    {
        public static void Maintain(BattleSimulation b)
        {
            Group(b, b.Green);
            if (!b.CustomDefenders)
                Group(b, b.Red);
            foreach (var g in b.DefenderGroups)
                if (g.Deployed)
                    Group(b, g.Command);
            foreach (var s in b.Soldiers)
            {
                if (!s.Alive || !s.Deployed)
                    continue;
                ProgressRecovery.Track(b, s);
                ExecutionReview.Sample(b, s);
                CommandHierarchy.Observe(b, s);
                CorpseEquipment.MaintainWeaponAid(b, s);
                // Expired help cannot reserve movement indefinitely (including sniper/medic branches
                // that never reach CombatCohesion.DecideHelper).
                if (s.HelpTargetId >= 0 && (s.HelpUntil <= b.Time || s.HelpTargetId >= b.Soldiers.Count || !b.Soldiers[s.HelpTargetId].Alive))
                {
                    s.HelpTargetId = -1;
                    s.HelpUntil = s.HelpRouteUntil = 0;
                    s.NextDecision = 0;
                    b.Team(s).NextPlan = 0;
                }

                // Recovery is a state transition, independent of tactic/decision frequency.
                if (s.Broken && MoraleDynamics.CanRecover(b, s))
                {
                    s.Broken = false;
                    s.NextDecision = 0;
                    s.MoveAuthorized = true;
                    s.SupportReady = false;
                    b.Diagnostics.Add(b, s, "finish", "Recover", "Мораль восстановлена; пересмотр задачи");
                }

                if (s.SupportReady && (!PositionLogic.Able(s, b) || !PositionLogic.HasSector(b, s, s.Position)))
                    s.SupportReady = false;
            }
        }

        public static void Group(BattleSimulation b, Squad q)
        {
            if (b.Time < q.Ai.NextKnowledge)
                return;
            q.Ai.NextKnowledge = b.Time + .5f;
            q.Ai.Expired.Clear();
            foreach (var c in q.Contacts)
                if (b.Time - c.Value.SeenAt > b.Config.ContactLifetime)
                    q.Ai.Expired.Add(c.Key);
            foreach (int id in q.Ai.Expired)
                q.Contacts.Remove(id);
            q.Observation.Update(b, q);
            CommandHierarchy.Maintain(b, q);
            int count = 0;
            float newest = -100;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && !s.Broken)
                    count++;
            foreach (var c in q.Contacts.Values)
                newest = Math.Max(newest, c.SeenAt);
            q.LastContact = Math.Max(q.LastContact, newest);
            if (q.Ai.Members != count)
            {
                q.NextPairReview = 0;
                q.NextPriorityPlan = 0;
                q.Defense.NextReview = 0;
                q.Ai.Members = count;
            }
        }

        public static bool MayAssign(BattleSimulation b, Soldier s)
        {
            bool emergency = TacticalOrders.Emergency(b.Team(s)) || RedCounterattack.Retreating(b, s);
            bool reserved = (SupplyRecovery.Active(s) || HandLauncher.Active(s)) || BuddyActions.Active(s) || ProgressRecovery.Active(b, s) || LastOpponentHunt.Owns(b, s) || AreaSuppression.Reserved(b, s) || CounterSniper.Reserved(b, s) || s.Busy || s.SupplyReceiverId >= 0 || s.RescuerId >= 0 || s.HelpTargetId >= 0 && s.HelpUntil > b.Time || CorpseEquipment.ValidWeaponAid(b, s) || s.LauncherPickupId >= 0 || s.LootTargetId >= 0 || s.LootGroundId >= 0;
            if (s.GrenadeEscape.Active || !s.Available || s.Broken || reserved && !emergency)
            {
                s.Ai.Rejected = "Приказ перемещения отклонён: состояние или действующая помощь";
                b.Diagnostics.RejectedOrders++;
                return false;
            }

            return true;
        }

        public static AiTaskKind Current(BattleSimulation b, Soldier s)
        {
            if (s.Alive && s.GrenadeEscape.Active)
                return AiTaskKind.EvadeGrenade;
            if (s.Broken || !s.Alive || s.Incapacitated)
                return AiTaskKind.Recover;
            if (s.RescueTargetId >= 0 || s.RescuerId >= 0 || s.TreatmentUntil > 0)
                return AiTaskKind.Medical;
            if ((SupplyRecovery.Active(s) || HandLauncher.Active(s)))
                return AiTaskKind.Pickup;
            if (BuddyActions.Active(s))
                return AiTaskKind.Support;
            if (s.SupplyReceiverId >= 0 || s.SupplyDonorId >= 0 || s.WeaponAidTargetId >= 0)
                return AiTaskKind.Supply;
            if (TacticalOrders.Emergency(b.Team(s)) || WithdrawalTactics.Active(b.Team(s)) || RedCounterattack.Retreating(b, s))
                return AiTaskKind.Retreat;
            if (ObjectiveOpportunity.IsCapturer(b, s))
                return AiTaskKind.Capture;
            if (s.LauncherPickupId >= 0 || s.LootTargetId >= 0 || s.LootGroundId >= 0)
                return AiTaskKind.Pickup;
            if (s.CounterSniper.Phase != CounterSniperPhase.None)
                return s.CounterSniper.Phase == CounterSniperPhase.Cover ? AiTaskKind.Support : AiTaskKind.Move;
            if (s.PathCursor < s.Path.Count)
                return AiTaskKind.Move;
            return s.Job == Job.Cover || s.Job == Job.Command ? AiTaskKind.Support : AiTaskKind.Observe;
        }

        public static void Observe(BattleSimulation b, Soldier s)
        {
            var a = s.Ai;
            var kind = Current(b, s);
            bool change = a.Kind != kind || a.Revision != s.OrderRevision || V2.Distance(a.Anchor, s.OrderAnchor) > .8f;
            if (change)
            {
                if (a.Kind != AiTaskKind.None)
                {
                    bool finished = s.PathCursor >= s.Path.Count && V2.Distance(s.Position, a.Anchor) < 1.2f;
                    b.Diagnostics.Add(b, s, finished ? "finish" : "interrupt", a.Kind.ToString(), s.Reason);
                    if (!finished)
                        b.Diagnostics.Interruptions++;
                }

                a.Kind = kind;
                a.Revision = s.OrderRevision;
                a.Started = a.LastProgress = b.Time;
                a.Anchor = s.OrderAnchor;
                a.Owner = kind.ToString();
                b.Diagnostics.Transitions++;
                b.Diagnostics.Add(b, s, "start", a.Owner, s.Reason);
            }

            a.Channels = BuddyActions.Active(s) ? AiChannels.Movement | AiChannels.Weapon | AiChannels.Hands : kind == AiTaskKind.Medical ? AiChannels.Movement | AiChannels.Hands : kind == AiTaskKind.Recover ? AiChannels.Movement : kind == AiTaskKind.Pickup || kind == AiTaskKind.Supply ? AiChannels.Movement | AiChannels.Hands : AiChannels.Movement | AiChannels.Weapon;
            if (V2.Distance(a.LastPosition, s.Position) > .25f)
            {
                a.LastPosition = s.Position;
                a.LastProgress = b.Time;
            }

            if (a.Reason != s.Reason && b.Time >= a.NextTrace)
            {
                a.NextTrace = b.Time + 1;
                b.Diagnostics.Add(b, s, "tick", a.Owner, s.Reason);
            }

            a.Reason = s.Reason;
            // A blocked assignment expires through the group planner; it never grants a
            // magical movement bypass. Medical and pickup have their own finite contracts.
            if (kind == AiTaskKind.Move && !ExecutionReview.ProtectedTask(b, s) && b.Time - s.Progress.ProgressAt > 14f && b.Time - a.Started > 14f && b.Time - a.LastProgress > 14f && b.Time >= s.Execution.NextReturn)
            {
                s.Execution.NextReturn = b.Time + 8;
                s.Execution.FailedGoal = s.OrderAnchor;
                s.Execution.FailedUntil = b.Time + 12;
                s.Execution.FailedMapRevision = b.Map.Revision;
                s.Execution.FailedOrderRevision = b.Team(s).Revision;
                s.Execution.ReturnedTasks++;
                s.RejectedPost = s.OrderAnchor;
                s.RejectedUntil = b.Time + 10;
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
                CommandHierarchy.Failed(b, s, "Маршрут исполнителя заблокирован");
                a.LastProgress = b.Time;
                s.Reason = "Задача заблокирована: возвращаю командиру";
                b.Diagnostics.Add(b, s, "abort", a.Owner, s.Reason);
            }
        }
    }
}
