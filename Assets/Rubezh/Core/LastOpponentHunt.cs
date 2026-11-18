using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class HuntState
    {
        public bool Active;
        public Contact Target;
        public int CoverId = -1;
        public float NextReview, CooldownUntil, BlockedSince = -1;
        public int FailedBounds, Repositions, Releases;
        public readonly Dictionary<int, int> Wings = new Dictionary<int, int>();
        public readonly Dictionary<int, float> PauseUntil = new Dictionary<int, float>(), BoundUntil = new Dictionary<int, float>();
    }

    public static class LastOpponentHunt
    {
        public static bool Scope(BattleSimulation b) => b.CampaignLevel == 19 || b.CampaignLevel == 20;
        static bool LastEnemy(BattleSimulation b, out int id)
        {
            id = -1;
            foreach (var e in b.Soldiers)
                if (e.Side == Side.Green && e.Alive && e.Deployed)
                {
                    if (id >= 0)
                        return false;
                    id = e.Id;
                }

            return id >= 0;
        }

        public static bool GroupActive(BattleSimulation b, DefenderGroupState g)
        {
            int id;
            return g != null && Scope(b) && g.Hunt.Active && LastEnemy(b, out id) && g.Hunt.Target != null && g.Hunt.Target.EnemyId == id && b.Time - g.Hunt.Target.SeenAt <= 45;
        }

        static bool Free(BattleSimulation b, Soldier s) => s.Side == Side.Red && s.Deployed && s.Available && !s.Busy && !s.Broken && !s.Bleeding && !s.GrenadeEscape.Active && !s.VehicleDisperse && !s.VehicleRally && s.Health >= 35 && s.Suppression < .65f && s.RescueTargetId < 0 && s.RescuerId < 0 && s.SupplyDonorId < 0 && s.SupplyReceiverId < 0 && s.HelpUntil <= b.Time && s.HelpRetreatUntil <= b.Time && s.CounterSniper.IncomingUntil <= b.Time && s.CounterSniper.Phase == CounterSniperPhase.None && s.LootTargetId < 0 && s.LootGroundId < 0 && s.LauncherPickupId < 0 && s.WeaponAidTargetId < 0 && s.HandsBusyUntil <= b.Time && s.Ammo + s.Reserve > 0 && !TacticalOrders.Emergency(b.Team(s)) && !WithdrawalTactics.Active(b.Team(s));
        public static bool Owns(BattleSimulation b, Soldier s) => GroupActive(b, s.DefenderGroup) && s.DefenderGroup.Hunt.Wings.ContainsKey(s.Id) && Free(b, s);
        public static bool IsCover(BattleSimulation b, Soldier s) => Owns(b, s) && s.DefenderGroup.Hunt.CoverId == s.Id;
        public static bool Sprint(BattleSimulation b, Soldier s) => Owns(b, s) && !IsCover(b, s) && s.PathCursor < s.Path.Count;
        static void End(BattleSimulation b, DefenderGroupState g)
        {
            var h = g.Hunt;
            if (!h.Active)
                return;
            h.Active = false;
            foreach (var s in g.Members)
                if (h.Wings.ContainsKey(s.Id))
                {
                    if (Free(b, s) && (s.Order == "Добивание: обход" || s.Order == "Добивание: подавление"))
                    {
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Position;
                        s.Job = Job.Cover;
                        s.NextDecision = 0;
                    }

                    AreaSuppression.Stop(s);
                }

            h.Wings.Clear();
            h.PauseUntil.Clear();
            h.BoundUntil.Clear();
            h.CoverId = -1;
            g.Command.NextPlan = 0;
            b.Log(Side.Red, g.Name + ": охота прекращена — пересмотр задачи");
        }

        public static bool Plan(BattleSimulation b, DefenderGroupState g)
        {
            int enemy;
            Contact target = null;
            if (Scope(b) && LastEnemy(b, out enemy))
            {
                foreach (var c in g.Command.Contacts.Values)
                    if (c.EnemyId == enemy && c.Confidence >= .35f && c.Uncertainty <= 5 && b.Time - c.SeenAt <= 8)
                        target = c;
            }

            if (target == null && Scope(b) && LastEnemy(b, out enemy) && g.Hunt.Target != null && g.Hunt.Target.EnemyId == enemy && b.Time - g.Hunt.Target.SeenAt <= 45)
                target = g.Hunt.Target;
            if (target == null)
            {
                End(b, g);
                return false;
            }

            if (b.Time < g.Hunt.CooldownUntil)
                return false;
            var h = g.Hunt;
            if (!h.Active)
            {
                h.Active = true;
                h.BlockedSince = -1;
                h.CoverId = -1;
                h.NextReview = 0;
                h.Wings.Clear();
                b.Log(Side.Red, g.Name + ": последний противник — прикрытие и обход с двух сторон");
            }

            h.Target = target.Copy();
            int count = 0;
            foreach (var s in g.Members)
                if (Free(b, s))
                {
                    if (!h.Wings.ContainsKey(s.Id))
                        h.Wings[s.Id] = (++count % 2 == 0 ? 1 : -1);
                    else
                        count++;
                }

            if (count < 2)
            {
                End(b, g);
                return false;
            }

            if (b.Time >= h.NextReview || h.CoverId < 0 || h.CoverId >= b.Soldiers.Count || !Free(b, b.Soldiers[h.CoverId]) || b.Soldiers[h.CoverId].Ammo == 0)
            {
                h.NextReview = b.Time + 4;
                int selected = -1;
                float score = float.MinValue;
                foreach (var s in g.Members)
                    if (Free(b, s) && !s.GunSniper && s.Ammo >= 6 && s.ReloadUntil <= b.Time && PositionLogic.CanEngage(b, s, s.Position, target) && !b.FriendlyInLane(s, target.Position))
                    {
                        float v = (s.GunMachineGunner ? 100 : 0) + (s.Id == h.CoverId ? 10 : 0) - V2.Distance(s.Position, target.Position);
                        if (v > score)
                        {
                            score = v;
                            selected = s.Id;
                        }
                    }

                h.CoverId = selected;
                // Assign opposite wings after selecting the suppressor, so removing him cannot leave only one wing.
                int wing = 0;
                foreach (var s in g.Members)
                    if (Free(b, s) && s.Id != selected)
                        h.Wings[s.Id] = wing++ % 2 == 0 ? -1 : 1;
            }

            foreach (var s in g.Members)
                if (Owns(b, s))
                {
                    if (s.Order != "Добивание: обход" && s.Order != "Добивание: подавление")
                    {
                        TacticalAI.Stop(s);
                        s.Order = "Добивание: обход";
                        s.Job = Job.Cover;
                    }

                    if (IsCover(b, s))
                    {
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Position;
                        s.Job = Job.Cover;
                        s.Order = "Добивание: подавление";
                        s.Reason = "Прижимаю последнюю известную цель, прикрываю обход";
                    }
                    else
                        PlanBound(b, s, h);
                }

            bool progress = false;
            foreach (var member in g.Members)
                if (Free(b, member) && (member.PathCursor < member.Path.Count || b.Time - member.LastFire < 3))
                    progress = true;
            if (progress)
                h.BlockedSince = -1;
            else if (h.BlockedSince < 0)
                h.BlockedSince = b.Time;
            else if (b.Time - h.BlockedSince > 10)
            {
                h.CooldownUntil = b.Time + 12;
                h.Releases++;
                End(b, g);
                g.Command.TaskReplanRequested = true;
                return false;
            }

            g.Command.CommanderOrder = "Последний противник: подавлять и обходить с двух сторон";
            return true;
        }

        static void PlanBound(BattleSimulation b, Soldier s, HuntState h)
        {
            if (s.ReloadUntil > b.Time || s.PathCursor < s.Path.Count)
                return;
            float pause;
            if (h.PauseUntil.TryGetValue(s.Id, out pause) && pause > b.Time)
                return;
            float distance = V2.Distance(s.Position, h.Target.Position);
            if (distance <= 5)
            {
                s.Job = Job.Cover;
                s.Reason = "Добивание: достиг огневого рубежа, работаю с остановки";
                return;
            }

            V2 forward = (h.Target.Position - s.Position).Normalized, side = new V2(-forward.Y, forward.X);
            int wing = h.Wings[s.Id];
            for (int i = 0; i < 3; i++)
            {
                var desired = s.Position + forward * Math.Min(2.5f, distance - 5) + side * (wing * (i == 0 ? 2.5f : i == 1 ? 1.5f : 3.5f));
                V2 goal = b.Map.NearestOpen(desired);
                if (V2.Distance(goal, s.Position) < 1 || V2.Distance(goal, h.Target.Position) < 4.5f || PositionLogic.Occupied(b, s, goal))
                    continue;
                var path = LossMemory.Find(b, s, goal, p => PositionLogic.Exposure(b, s, p) * 4 + (PositionLogic.Occupied(b, s, p) ? 15 : 0));
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 7)
                    continue;
                // A shorter bound may cross an exposed cell only under actual recent covering fire.
                bool covered = h.CoverId >= 0 && b.Time - b.Soldiers[h.CoverId].LastFire < 1.2f;
                if (!covered && !CounterSniper.SafeRoute(b, s, path, h.Target))
                    continue;
                s.Path = path;
                s.PathCursor = 0;
                s.Destination = s.OrderAnchor = goal;
                s.Job = Job.Flank;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Order = "Добивание: обход";
                s.Reason = wing < 0 ? "Обхожу слева короткой перебежкой" : "Обхожу справа короткой перебежкой";
                h.BoundUntil[s.Id] = b.Time + 8;
                return;
            }

            if (TryNavigationBound(b, s, h))
                return;
            h.FailedBounds++;
            h.PauseUntil[s.Id] = b.Time + 2;
            s.Job = Job.Cover;
            s.Reason = h.CoverId < 0 ? "Добивание: нет доступной огневой позиции, возвращаю отказ командиру" : "Добивание: обход сейчас недоступен, проверяю другой маршрут";
        }

        static bool TryNavigationBound(BattleSimulation b, Soldier s, HuntState h)
        {
            if (GroupMovement.Count(b, b.Team(s)) >= 3)
                return false;
            V2 axis = (h.Target.Position - s.Position).Normalized, lat = new V2(-axis.Y, axis.X);
            // Search reachable firing posts, not three Euclidean points on the same wall.
            for (int k = 0; k < 5; k++)
            {
                float flank = k == 0 ? 0 : k % 2 == 0 ? -1 : 1;
                V2 aim = h.Target.Position - axis * Math.Max(5, b.WeaponRange(s) - 2) + lat * (flank * (k <= 2 ? 4 : 7));
                var full = LossMemory.Find(b, s, b.Map.NearestOpen(aim), p => PositionLogic.Exposure(b, s, p) * 5 + FireLaneSafety.PathCost(b, s, p));
                b.Stats.Paths++;
                var path = new List<V2>();
                V2 from = s.Position;
                float len = 0;
                foreach (var point in full)
                {
                    len += V2.Distance(from, point);
                    if (len > 6)
                        break;
                    path.Add(point);
                    from = point;
                }

                if (path.Count == 0 || V2.Distance(from, s.Position) < .8f || PositionLogic.Occupied(b, s, from) || !CounterSniper.SafeRoute(b, s, path, h.Target))
                    continue;
                s.Path = path;
                s.PathCursor = 0;
                s.OrderAnchor = s.Destination = from;
                s.Job = Job.Reposition;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Order = "Добивание: обход";
                s.Reason = "Добивание: выхожу на достижимый огневой сектор";
                h.BoundUntil[s.Id] = b.Time + 8;
                h.Repositions++;
                return true;
            }

            return false;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!Owns(b, s))
                return false;
            if (!IsCover(b, s))
                PlanBound(b, s, s.DefenderGroup.Hunt);
            return true;
        }

        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            if (!Owns(b, s))
                return false;
            var h = s.DefenderGroup.Hunt;
            if (IsCover(b, s))
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                return true;
            }

            if (s.PathCursor >= s.Path.Count)
            {
                s.Moving = false;
                s.Job = Job.Cover;
                return true;
            }

            float until;
            if (h.BoundUntil.TryGetValue(s.Id, out until) && b.Time >= until)
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                h.PauseUntil[s.Id] = b.Time + 1;
                s.Reason = "Обход заблокирован — перестраиваю маршрут";
                return true;
            }

            Locomotion.AdvanceWaypoint(b, s);
            if (s.PathCursor < s.Path.Count)
            {
                var d = s.Path[s.PathCursor] - s.Position;
                V2 next;
                if (Locomotion.TryStep(b, s, d.Normalized * Math.Min(d.Length, CombatMovement.Speed(b, s) * dt), out next))
                {
                    s.DistanceTravelled += V2.Distance(s.Position, next);
                    s.Position = next;
                    s.Facing = d.Normalized;
                    s.LastMoved = b.Time;
                    s.Moving = true;
                    if (V2.Distance(next, s.Path[s.PathCursor]) < .05f)
                        s.PathCursor++;
                }
            }

            Locomotion.AdvanceWaypoint(b, s);
            if (s.PathCursor >= s.Path.Count)
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                h.PauseUntil[s.Id] = b.Time + Math.Max(1.3f, WeaponBalance.For(s).FireSeconds);
                s.Reason = "Остановка после обхода: веду огонь";
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }
    }
}
