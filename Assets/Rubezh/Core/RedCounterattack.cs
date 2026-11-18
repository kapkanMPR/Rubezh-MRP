using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public enum RedCounterattackPhase
    {
        Off,
        Assault,
        Retreat,
        Defense
    }

    // Only the original red squad is controlled by this state machine. Reinforcements
    // are excluded by their IDs and retain AdaptiveDefense / normal ammunition AI.
    public sealed class RedCounterattackState
    {
        public RedCounterattackPhase Phase;
        public int OriginalCount, LossThreshold, RetreatEvents, AssaultWing;
        public float LastMoveAt, RetreatSince, FirstContactAt = -1;
        public string ExitReason = "";
        public readonly HashSet<int> RetreatMovers = new HashSet<int>();
        public readonly Dictionary<int, float> SelectedAt = new Dictionary<int, float>();
        public int LastMoverId = -1, RetreatBoundDone = -1;
        public bool RetreatClear;
        public readonly Dictionary<int, V2> AssaultCheckpoints = new Dictionary<int, V2>();
        public readonly Dictionary<int, float> AssaultProgressAt = new Dictionary<int, float>();
        public readonly Dictionary<int, float> NextRoute = new Dictionary<int, float>();
        public readonly HashSet<int> AtBase = new HashSet<int>();
        public readonly RedReserveDeploymentState ReserveDeployment = new RedReserveDeploymentState();
        public void Begin(int original)
        {
            Phase = original > 0 ? RedCounterattackPhase.Assault : RedCounterattackPhase.Off;
            OriginalCount = original;
            LossThreshold = (original + 1) / 2; // 2/3, 2/4, 3/5
            FirstContactAt = -1;
            RetreatClear = false;
            RetreatBoundDone = -1;
            RetreatEvents = 0;
            AssaultWing = 0;
            LastMoveAt = 0;
            RetreatSince = 0;
            LastMoverId = -1;
            NextRoute.Clear();
            AtBase.Clear();
            RetreatMovers.Clear();
            SelectedAt.Clear();
            ReserveDeployment.Clear();
            AssaultCheckpoints.Clear();
            AssaultProgressAt.Clear();
        }
    }

    public static class RetreatVoices
    {
        static readonly string[] Lines =
        {
            "Кто куда, а я по съебам!",
            "Да ну нахер!",
            "Не, ну ты видел? Видел?"
        };
        // Unique speakers and unique phrases inside each event. Red text is marked for
        // the presentation layer; ordinary red chatter remains hidden as before.
        public static int Emit(BattleSimulation b, Side side)
        {
            if (side == Side.Red && !RedCounterattack.PanicAllowed(b))
                return 0;
            var available = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (s.Side == side && s.Alive && s.Deployed && !s.Incapacitated && (b.ActiveDefenderGroup == null || s.DefenderGroup == b.ActiveDefenderGroup))
                    available.Add(s);
            if (available.Count == 0)
                return 0;
            for (int i = available.Count - 1; i > 0; i--)
            {
                int j = b.VoiceRandom.Int(i + 1);
                var tmp = available[i];
                available[i] = available[j];
                available[j] = tmp;
            }

            int[] phrases =
            {
                0,
                1,
                2
            };
            for (int i = 2; i > 0; i--)
            {
                int j = b.VoiceRandom.Int(i + 1);
                int t = phrases[i];
                phrases[i] = phrases[j];
                phrases[j] = t;
            }

            int count = Math.Min(available.Count, 1 + b.VoiceRandom.Int(3));
            for (int i = 0; i < count; i++)
            {
                var s = available[i];
                var phrase = Lines[phrases[i]];
                s.Speech = phrase;
                s.SpeechShort = phrase;
                s.SpeechUrgent = true;
                s.SpeechAt = s.LastSpokenAt = b.Time;
                s.SpeechUntil = b.Time + 5f;
                s.RetreatQuoteUntil = b.Time + 5f;
                s.LastSpoken = phrase;
                s.SpeechCooldown = b.Time + 5f;
                b.Log(side, s.Name + ": " + phrase);
            }

            return count;
        }
    }

    public static class RedCounterattack
    {
        const int FirstRedId = 5;
        public const float CoverRetreatDistance = 25f;
        public static bool Orderly(BattleSimulation b) => b.ActiveDefenderGroup != null && b.ActiveDefenderGroup.Pullback;
        public static bool PanicAllowed(BattleSimulation b)
        {
            if (b.CampaignLevel == 19 || b.CampaignLevel == 20)
                return false;
            var g = b.ActiveDefenderGroup;
            return g == null ? b.Counterattack.OriginalCount > 0 : g.Attack && !g.Pullback && g.SpawnTime <= 0 && !g.VehicleArrival;
        }

        static int GroupKills(BattleSimulation b)
        {
            int n = 0;
            foreach (var s in b.ActiveDefenderGroup.Members)
                n += s.Kills;
            return n;
        }

        public static bool SequentialRetreat(BattleSimulation b)
        {
            if (!Orderly(b) || b.Counterattack.RetreatClear)
                return false;
            foreach (var s in b.ActiveDefenderGroup.Members)
                if (s.Available && s.Deployed)
                {
                    if (b.Time - s.LastIncoming < 8)
                        return true;
                    foreach (var c in Knowledge.Contacts(b, s))
                        if (c.Confidence >= .25f && b.Time - c.SeenAt <= 25)
                            return true;
                }

            return false; // Contacts expired: regroup normally, without claiming a measured 25-cell separation.
        }

        public static bool RetreatCover(BattleSimulation b, Soldier s)
        {
            var g = s.DefenderGroup;
            return g != null && g.Pullback && g.Counterattack.Phase == RedCounterattackPhase.Retreat && !g.Counterattack.RetreatClear && !g.Counterattack.RetreatMovers.Contains(s.Id);
        }

        static void CheckRetreatDistance(BattleSimulation b)
        {
            var g = b.ActiveDefenderGroup;
            if (g == null || !g.Pullback || g.Counterattack.RetreatClear)
                return;
            bool known = false;
            foreach (var s in g.Members)
                if (s.Available && s.Deployed)
                {
                    foreach (var observer in g.Members)
                        foreach (var c in Knowledge.Contacts(b, observer))
                        {
                            if (c.Confidence < .25f || b.Time - c.SeenAt > 25)
                                continue;
                            known = true;
                            if (V2.Distance(s.Position, c.Position) - c.Uncertainty <= CoverRetreatDistance)
                                return;
                        }
                }

            if (!known)
                return; // No omniscient distance to undiscovered enemies.
            g.Counterattack.RetreatClear = true;
            foreach (var s in g.Members)
                AreaSuppression.Stop(s);
            b.Log(Side.Red, g.Name + ": оторвались дальше 25 клеток от известных угроз, продолжаем отход");
        }

        static void SelectRetreatMover(BattleSimulation b)
        {
            var st = b.Counterattack;
            var members = b.ActiveDefenderGroup.Members;
            st.RetreatMovers.RemoveWhere(id => id < 0 || id >= b.Soldiers.Count || !b.Soldiers[id].Available || b.Time - st.SelectedAt[id] > .5f && (!MotionCombat.HasRoute(b.Soldiers[id]) || b.Time - b.Soldiers[id].LastMoved > 2));
            if (b.Time < st.LastMoveAt)
                return;
            int start = members.FindIndex(s => s.Id == st.LastMoverId);
            for (int i = 1; i <= members.Count && st.RetreatMovers.Count < GroupMovement.Maximum; i++)
            {
                var s = members[(start + i + members.Count) % members.Count];
                if (st.RetreatMovers.Contains(s.Id) || !s.Available || s.Busy || TacticalOrders.ServiceActive(s) || SniperRetreatCover.Holding(b, s) || s.ReloadUntil > b.Time || V2.Distance(s.Position, b.Red.DefenseCenter(b)) <= 4.2f)
                    continue;
                V2 goal = s.Position + (b.Red.DefenseCenter(b) - s.Position).Normalized * 6;
                var excluded = new HashSet<int>(st.RetreatMovers);
                excluded.Add(s.Id);
                bool safe = GroupMovement.Covered(b, s, goal, excluded);
                foreach (int id in st.RetreatMovers)
                    if (!GroupMovement.Covered(b, b.Soldiers[id], b.Soldiers[id].Destination, excluded))
                        safe = false;
                // A sole escape must still be possible under fire; extra movers require coverage.
                if (!safe && st.RetreatMovers.Count > 0)
                    continue;
                st.RetreatMovers.Add(s.Id);
                st.SelectedAt[s.Id] = b.Time;
                st.LastMoverId = s.Id;
                st.RetreatBoundDone = -1;
                AreaSuppression.Stop(s);
            }

            st.LastMoveAt = b.Time + 1;
        }

        public static bool Enabled(BattleSimulation b) => b.Counterattack.Phase != RedCounterattackPhase.Off;
        public static bool Original(BattleSimulation b, Soldier s) => b.ActiveDefenderGroup != null ? s.DefenderGroup == b.ActiveDefenderGroup : s.Side == Side.Red && s.Id >= FirstRedId && s.Id < FirstRedId + b.Counterattack.OriginalCount;
        public static int AliveOriginals(BattleSimulation b)
        {
            int n = 0;
            foreach (var s in b.Soldiers)
                if (Original(b, s) && s.Alive)
                    n++;
            return n;
        }

        public static bool Retreating(BattleSimulation b, Soldier s) => s.DefenderGroup != null ? s.DefenderGroup.Counterattack.Phase == RedCounterattackPhase.Retreat : Original(b, s) && b.Counterattack.Phase == RedCounterattackPhase.Retreat;
        public static void Configure(BattleSimulation b, int original)
        {
            // Only missions that really have pending reserves use the counterattack arc.
            b.Counterattack.Begin(b.CampaignReserve > 0 ? original : 0);
            if (!Enabled(b))
                return;
            b.Counterattack.AssaultWing = b.Random.Int(3) - 1; // independent center/left/right counterattack
            b.Red.NextPlan = b.Time;
            b.Red.CommanderOrder = "Первый отряд: ответный штурм с прикрытием";
            b.Log(Side.Red, "Первый отряд ГРАНИТ: контратаковать. Резерв удерживает базовый рубеж.");
        }

        public static void Tick(BattleSimulation b)
        {
            if (b.CustomDefenders && b.ActiveDefenderGroup == null)
                return;
            var st = b.Counterattack;
            if (st.Phase == RedCounterattackPhase.Off || st.Phase == RedCounterattackPhase.Defense)
                return;
            if (st.Phase == RedCounterattackPhase.Assault)
            {
                int lost = st.OriginalCount - AliveOriginals(b);
                bool pressure = false;
                if (Orderly(b))
                    foreach (var unit in b.ActiveDefenderGroup.Members)
                        if (unit.Available)
                        {
                            if (unit.Suppression > .3f || b.Time - unit.LastIncoming < 2)
                                pressure = true;
                            foreach (var c in Knowledge.Contacts(b, unit))
                                if (PositionLogic.Recent(b, c))
                                {
                                    if (st.FirstContactAt < 0)
                                        st.FirstContactAt = b.Time;
                                    if (V2.Distance(unit.Position, c.Position) < 12)
                                        pressure = true;
                                }
                        }

                if (Orderly(b) ? (lost >= 1 || GroupKills(b) > 0 && pressure && st.FirstContactAt >= 0 && b.Time - st.FirstContactAt >= 4) : PanicAllowed(b) && lost >= st.LossThreshold)
                {
                    st.Phase = RedCounterattackPhase.Retreat;
                    st.RetreatSince = b.Time;
                    st.LastMoveAt = b.Time;
                    st.RetreatEvents++;
                    st.NextRoute.Clear();
                    st.AtBase.Clear();
                    foreach (var s in b.Soldiers)
                        if (Original(b, s) && s.Alive)
                        {
                            if (s.Busy || TacticalOrders.ServiceActive(s))
                                continue;
                            TacticalOrders.CancelUnit(b, s);
                            if (!Orderly(b))
                                s.ReloadUntil = 0f; // Interrupted reload grants no ammunition.
                            s.MoveAuthorized = true;
                            s.ManeuverPaused = false;
                            s.Job = Job.Reposition;
                            s.NextDecision = b.Time;
                            s.Order = Orderly(b) ? "Оттяжка: отход поочерёдно, держать прикрытие" : "ДА НУ НАХЕР! К базовому рубежу!";
                            s.Reason = "Приказ на отрыв после потерь: " + lost + " из " + st.OriginalCount;
                        }

                    b.Red.NextPlan = b.Time;
                    b.Red.CommanderOrder = "Отход первого отряда к флагу!";
                    b.Log(Side.Red, Orderly(b) ? b.ActiveDefenderGroup.Name + ": организованная оттяжка к флагу." : "Первый отряд потерял " + lost + " из " + st.OriginalCount + ". Панический отход к базовому рубежу!");
                    if (!Orderly(b))
                        RetreatVoices.Emit(b, Side.Red);
                }
            }

            if (st.Phase == RedCounterattackPhase.Retreat)
            {
                CheckRetreatDistance(b);
                if (SequentialRetreat(b))
                    SelectRetreatMover(b);
                bool everyoneBack = true;
                foreach (var s in b.Soldiers)
                {
                    if (!Original(b, s) || !s.Alive || !s.Deployed || s.Incapacitated)
                        continue;
                    if (V2.Distance(s.Position, b.Red.DefenseCenter(b)) <= 4.2f)
                    {
                        st.AtBase.Add(s.Id);
                        continue;
                    }

                    everyoneBack = false;
                    st.AtBase.Remove(s.Id);
                }

                // If all survivors are already at the flag, resume the existing defender AI.
                // A blocked or medically incapacitated soldier cannot freeze command forever.
                if (everyoneBack || b.Time - st.RetreatSince > (Orderly(b) ? 120f : 45f))
                {
                    st.ExitReason = everyoneBack ? "Дошли до оборонительного рубежа" : "Отход не завершён: локальная оборона после ограничения времени";
                    st.Phase = RedCounterattackPhase.Defense;
                    st.NextRoute.Clear();
                    foreach (var s in b.Soldiers)
                        if (Original(b, s) && s.Alive && s.Available && !s.Busy && !TacticalOrders.ServiceActive(s))
                        {
                            TacticalAI.Stop(s);
                            s.OrderAnchor = s.Destination = s.Position;
                            s.MoveAuthorized = true;
                            s.Job = Job.Hold;
                            s.NextDecision = b.Time;
                            s.Order = "Базовый рубеж: оборона";
                        }

                    b.Red.Defense.Initialized = false;
                    b.Red.Defense.Active = false;
                    b.Red.Defense.Movers.Clear();
                    b.Red.Initialized = false;
                    b.Red.NextPlan = b.Time;
                    b.Red.CommanderOrder = everyoneBack ? "Вернулись к рубежу. Занять сектора обороны!" : "Отход задержан. Закрепиться на доступных позициях и восстановить взаимодействие!";
                    b.Log(Side.Red, b.Red.CommanderOrder);
                }
            }
        }

        // Team plan: original detachment attacks; reserves retain their own defensive plan.
        public static bool Plan(BattleSimulation b, Squad q)
        {
            if (q.Side != Side.Red || !Enabled(b))
                return false;
            if (b.ActiveDefenderGroup != null && b.Counterattack.Phase == RedCounterattackPhase.Defense)
                return false;
            if (b.Counterattack.Phase == RedCounterattackPhase.Defense)
            {
                var defensiveReserves = new List<Soldier>();
                foreach (var s in b.Soldiers)
                    if (s.Side == Side.Red && !Original(b, s) && s.Available)
                        defensiveReserves.Add(s);
                return RedReserveDeployment.Plan(b, q, defensiveReserves);
            }

            var st = b.Counterattack;
            // The original red leader may have fallen while the reserve is arriving.
            if (q.LeaderId < 0 || q.LeaderId >= b.Soldiers.Count || !b.Soldiers[q.LeaderId].Available)
            {
                Soldier successor = null;
                foreach (var candidate in b.Soldiers)
                    if (candidate.Side == Side.Red && candidate.Available && (b.ActiveDefenderGroup == null || Original(b, candidate)) && (successor == null || Original(b, candidate) && !Original(b, successor)))
                        successor = candidate;
                if (successor != null)
                    q.LeaderId = successor.Id;
            }

            if (st.Phase == RedCounterattackPhase.Assault)
            {
                var available = new List<Soldier>();
                foreach (var s in b.Soldiers)
                    if (Original(b, s) && s.Available && !s.Broken && !s.VehicleRally && !s.VehicleDisperse)
                        available.Add(s);
                bool threatened = false;
                foreach (var unit in available)
                {
                    if (b.Time - unit.LastIncoming < 3f || unit.Suppression > .2f)
                        threatened = true;
                    foreach (var c in unit.Contacts.Values)
                        if (c.Confidence >= .25f && b.Time - c.SeenAt < 8f)
                            threatened = true;
                }

                foreach (var c in q.Contacts.Values)
                    if (c.Confidence >= .25f && b.Time - c.SeenAt < 8f)
                        threatened = true;
                if (available.Count > 0 && b.Time >= st.LastMoveAt)
                {
                    foreach (var unit in available)
                        if (AssaultOwned(b, unit) && unit.PathCursor < unit.Path.Count)
                        {
                            // A squad timer must never cancel a progressing bound.
                            V2 checkpoint;
                            float progressAt;
                            if (!st.AssaultCheckpoints.TryGetValue(unit.Id, out checkpoint) || V2.Distance(checkpoint, unit.Position) > 1.2f)
                            {
                                st.AssaultCheckpoints[unit.Id] = unit.Position;
                                st.AssaultProgressAt[unit.Id] = b.Time;
                            }

                            st.AssaultProgressAt.TryGetValue(unit.Id, out progressAt);
                            if (b.Time - progressAt >= 8f)
                            {
                                TacticalAI.Stop(unit);
                                unit.Job = Job.Cover;
                                unit.Reason = "Маршрут контратаки заблокирован: выбираю новую позицию";
                            }
                        }

                    if (GroupMovement.Count(b, q) < GroupMovement.Maximum)
                    {
                        int start = available.FindIndex(unit => unit.Id == st.LastMoverId);
                        int last = st.LastMoverId;
                        for (int offset = 1; offset <= available.Count; offset++)
                        {
                            var unit = available[(start + offset + available.Count) % available.Count];
                            if (!AssaultOwned(b, unit) || unit.PathCursor < unit.Path.Count)
                                continue;
                            if (!GroupMovement.CanStart(b, unit, unit.Position + TacticalOrders.Axis(b, Side.Red) * 6))
                                continue;
                            if (AssaultBound(b, unit))
                            {
                                b.Stats.Bounds++;
                                last = unit.Id;
                                unit.Reason = threatened ? "Контратака: короткая перебежка, прикрытие проверяется на маршруте" : "Сближение без контакта: подтягиваюсь вместе с отрядом";
                                if (GroupMovement.Count(b, q) >= GroupMovement.Maximum)
                                    break;
                            }
                        }

                        st.LastMoverId = last;
                    }

                    st.LastMoveAt = b.Time + 1f;
                }

                string wing = st.AssaultWing < 0 ? "левым направлением" : st.AssaultWing > 0 ? "правым направлением" : "по центру";
                q.Plan = q.Execution = "Первый красный отряд: контратака " + wing;
                q.CommanderOrder = threatened ? "Контратака: перебежки с проверкой прикрытия" : "Сближаемся вместе, сохранять связь с отрядом";
            }
            else
            {
                q.Plan = q.Execution = "Первый отряд: отрыв от противника к флагу";
                q.CommanderOrder = Orderly(b) ? "Оттяжка: до трёх отходят, остальные прикрывают" : "ДА НУ НАХЕР! На базовый рубеж!";
            }

            // On reinforcement arrival run the *original* AdaptiveDefense on reserves only.
            if (b.ActiveDefenderGroup != null)
                return true;
            var reserves = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Red && !Original(b, s) && s.Available)
                    reserves.Add(s);
            if (reserves.Count > 0 && !RedReserveDeployment.Plan(b, q, reserves))
                DefenseBrain.Plan(b, q, reserves);
            return true;
        }

        static bool AssaultOwned(BattleSimulation b, Soldier s)
        {
            return !AreaSuppression.Reserved(b, s) && !CounterSniper.Reserved(b, s) && s.HelpUntil <= b.Time && s.HelpRetreatUntil <= b.Time && s.FlagResponseUntil <= b.Time && !s.Busy && s.SupplyReceiverId < 0 && s.LauncherPickupId < 0 && s.LootGroundId < 0 && s.LootTargetId < 0 && s.WeaponAidTargetId < 0 && (s.Job == Job.Advance || s.Job == Job.Cover || s.Job == Job.Hold || s.Job == Job.Command);
        }

        static bool AssaultBound(BattleSimulation b, Soldier s)
        {
            if (!s.Available || !AssaultOwned(b, s) || s.Broken || s.Suppression > .72f || s.ReloadUntil > b.Time)
                return false;
            Contact contact = TacticalOrders.ClosestKnown(b, s);
            V2 forward = TacticalOrders.Axis(b, Side.Red);
            V2 desired;
            if (contact != null)
            {
                float distance = V2.Distance(s.Position, contact.Position);
                if (distance <= b.WeaponRange(s) * .9f && PositionLogic.CanEngage(b, s, s.Position, contact))
                    return false;
                V2 to = (contact.Position - s.Position).Normalized;
                desired = s.Position + to * Math.Min(MotionCombat.Recommended(b.Team(s)), Math.Max(0f, distance - b.WeaponRange(s) * .76f));
            }
            else
                desired = s.Position + forward * MotionCombat.Recommended(b.Team(s));
            var lateral = new V2(-forward.Y, forward.X);
            V2 wanted = desired + lateral * (b.Counterattack.AssaultWing * 2f + (s.Id % 2 == 0 ? 1.3f : -1.3f));
            float rear = float.MaxValue;
            foreach (var mate in b.Soldiers)
                if (Original(b, mate) && mate.Available && !mate.Broken && !mate.Busy)
                    rear = Math.Min(rear, V2.Dot(mate.Position, forward));
            V2 post = s.Position;
            List<V2> path = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                V2 candidate = TacticalOrders.Open(b, wanted + lateral * (attempt < 2 ? 0 : attempt == 2 ? 2f : attempt == 3 ? -2f : 4f));
                V2 covered;
                if (attempt == 0 && PositionLogic.TryPost(b, s, candidate, 3f, false, out covered))
                    candidate = covered;
                if (V2.Distance(candidate, s.Position) < 1.1f || PositionLogic.Occupied(b, s, candidate))
                    continue;
                if (rear < float.MaxValue && V2.Dot(candidate, forward) > rear + 6f)
                    continue;
                if (contact == null && V2.Dot(candidate - s.Position, forward) < .8f)
                    continue;
                var route = LossMemory.Find(b, s, candidate, p => PositionLogic.Exposure(b, s, p) * 2f + (PositionLogic.Occupied(b, s, p) ? 12f : 0f));
                b.Stats.Paths++;
                if (route.Count == 0 || SupportAI.PathLength(s.Position, route) > 12f)
                    continue;
                path = route;
                post = candidate;
                break;
            }

            if (path == null)
                return false;
            b.Counterattack.AssaultCheckpoints[s.Id] = s.Position;
            b.Counterattack.AssaultProgressAt[s.Id] = b.Time;
            s.Path = path;
            s.PathCursor = 0;
            s.Destination = s.OrderAnchor = post;
            s.Job = Job.Advance;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Ответный штурм: короткая перебежка";
            s.Reason = "Контратака, остальные прикрывают";
            s.NextDecision = b.Time;
            s.LastMoved = b.Time;
            return true;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var previous = b.ActiveDefenderGroup;
            b.ActiveDefenderGroup = s.DefenderGroup;
            try
            {
                return DecideGroup(b, s);
            }
            finally
            {
                b.ActiveDefenderGroup = previous;
            }
        }

        static bool DecideGroup(BattleSimulation b, Soldier s)
        {
            if (RedReserveDeployment.Decide(b, s))
                return true;
            if (!Original(b, s) || !s.Alive || s.Busy || TacticalOrders.ServiceActive(s))
                return false;
            if (b.Counterattack.Phase == RedCounterattackPhase.Retreat)
            {
                if (!s.Incapacitated && b.Time >= s.NextDecision)
                    PlanRetreat(b, s, false);
                return true;
            }

            if (b.Counterattack.Phase != RedCounterattackPhase.Assault)
                return false;
            if (s.Job == Job.Advance && s.PathCursor >= s.Path.Count)
            {
                s.Job = Job.Cover;
            }

            if (s.Job == Job.Cover || s.Job == Job.Hold)
            {
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                return true;
            }

            if (s.PathCursor < s.Path.Count)
            {
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                return true;
            }

            // Other tasks (wounds, medical and ammunition assistance) remain available.
            return false;
        }

        static void PlanRetreat(BattleSimulation b, Soldier s, bool force)
        {
            if (!s.Alive || s.Incapacitated || !s.Deployed)
                return;
            if (s.Busy || s.ReloadUntil > b.Time)
                return;
            if (SniperRetreatCover.Hold(b, s))
                return;
            if (SequentialRetreat(b) && !b.Counterattack.RetreatMovers.Contains(s.Id))
                return;
            var st = b.Counterattack;
            if (V2.Distance(s.Position, b.Red.DefenseCenter(b)) <= 4.2f)
            {
                st.AtBase.Add(s.Id);
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Hold;
                return;
            }

            float next;
            if (!force && s.PathCursor < s.Path.Count && b.Time - s.LastMoved < 1.1f)
                return;
            if (!force && st.NextRoute.TryGetValue(s.Id, out next) && b.Time < next)
                return;
            st.NextRoute[s.Id] = b.Time + 1.6f;
            var dir = (b.Red.DefenseCenter(b) - s.Position).Normalized;
            if (dir.Length < .1f)
                return;
            var perpendicular = new V2(-dir.Y, dir.X);
            V2 target = TacticalOrders.Open(b, s.Position + dir * Math.Min(SequentialRetreat(b) ? 6f : 8f, V2.Distance(s.Position, b.Red.DefenseCenter(b))));
            List<V2> path = null;
            V2 goal = target;
            float best = float.MaxValue;
            float homeDistance = V2.Distance(s.Position, b.Red.DefenseCenter(b));
            for (int i = 0; i < 6; i++)
            {
                V2 candidate;
                if (i == 0)
                {
                    if (!PositionLogic.TryPost(b, s, target, 2, false, out candidate))
                        continue;
                }
                else
                    candidate = TacticalOrders.Open(b, target + perpendicular * (i == 1 ? 0 : i == 2 ? 1.5f : i == 3 ? -1.5f : i == 4 ? 3 : -3));
                if (!b.Map.Walkable(candidate) || PositionLogic.Occupied(b, s, candidate) || V2.Distance(candidate, b.Red.DefenseCenter(b)) > homeDistance - Math.Min(2f, Math.Max(.5f, homeDistance - 4.2f)))
                    continue;
                var route = LossMemory.Find(b, s, candidate, p => PositionLogic.Exposure(b, s, p) * 3);
                b.Stats.Paths++;
                float length = SupportAI.PathLength(s.Position, route);
                if (route.Count == 0 || length > 12)
                    continue;
                float peak = 0, total = 0;
                foreach (var point in route)
                {
                    float risk = PositionLogic.Exposure(b, s, point);
                    peak = Math.Max(peak, risk);
                    total += risk;
                }

                float score = V2.Distance(candidate, b.Red.DefenseCenter(b)) + length * .25f + peak * 2 + total / route.Count * 4 - (PositionLogic.Protected(b, s, candidate) ? 2 : 0);
                if (score < best)
                {
                    best = score;
                    path = route;
                    goal = candidate;
                }
            }

            if (path == null)
            {
                // The next safe waypoint may be sideways around a wall, not closer in a straight line.
                // Follow a bounded prefix of the actual home route instead of retrying unreachable direct posts.
                var homeRoute = LossMemory.Find(b, s, TacticalOrders.Open(b, b.Red.DefenseCenter(b)), p => PositionLogic.Exposure(b, s, p) * 3 + FireLaneSafety.PathCost(b, s, p));
                b.Stats.Paths++;
                var prefix = new List<V2>();
                V2 previous = s.Position;
                float travelled = 0;
                foreach (var point in homeRoute)
                {
                    travelled += V2.Distance(previous, point);
                    if (travelled > MotionCombat.Maximum(b.Team(s)))
                        break;
                    prefix.Add(point);
                    previous = point;
                }

                while (prefix.Count > 0)
                {
                    var end = prefix[prefix.Count - 1];
                    if (V2.Distance(s.Position, end) > .8f && !PositionLogic.Occupied(b, s, end) && PositionLogic.UsefulRoute(b, s, prefix) && (PositionLogic.Protected(b, s, end) || PositionLogic.Exposure(b, s, end) <= .12f || GroupMovement.Covered(b, s, end)))
                    {
                        path = prefix;
                        goal = end;
                        s.Motion.RetreatDetours++;
                        break;
                    }

                    prefix.RemoveAt(prefix.Count - 1);
                }
            }

            if (path == null)
            {
                st.NextRoute[s.Id] = b.Time + 0.8f;
                return;
            }

            s.Path = path;
            s.PathCursor = 0;
            s.Destination = s.OrderAnchor = goal;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Job = Job.Reposition;
            s.Order = Orderly(b) ? "Отхожу под прикрытием" : "ДА НУ НАХЕР! К флагу!";
            s.Reason = "Приказ отхода: игнорировать прежние боевые задачи";
            s.LastMoved = b.Time;
            TacticalOrders.TrySmoke(b, s, TacticalOrders.ClosestKnown(b, s));
        }

        // Called from movement before regular cover-wait AI, only for the original reds.
        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            var previous = b.ActiveDefenderGroup;
            b.ActiveDefenderGroup = s.DefenderGroup;
            try
            {
                return MoveGroup(b, s, dt);
            }
            finally
            {
                b.ActiveDefenderGroup = previous;
            }
        }

        static bool MoveGroup(BattleSimulation b, Soldier s, float dt)
        {
            if (!Retreating(b, s) || !s.Alive || !s.Deployed || s.Incapacitated || s.Busy || TacticalOrders.ServiceActive(s))
                return false;
            if (SniperRetreatCover.Hold(b, s))
                return false;
            if (b.Time < s.ConcussionReactUntil)
            {
                s.Moving = false;
                s.State = ActionState.Observe;
                return true;
            }

            if (V2.Distance(s.Position, b.Red.DefenseCenter(b)) <= 4.2f)
            {
                b.Counterattack.AtBase.Add(s.Id);
                TacticalAI.Stop(s);
                s.Job = Job.Hold;
                s.State = ActionState.Observe;
                return false;
            }

            if (SequentialRetreat(b) && !b.Counterattack.RetreatMovers.Contains(s.Id))
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.State = ActionState.Observe;
                s.SupportReady = PositionLogic.Able(s, b) && PositionLogic.HasSector(b, s, s.Position);
                return false;
            }

            PlanRetreat(b, s, false);
            s.SupportReady = false;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            if (s.PathCursor < s.Path.Count)
            {
                Locomotion.AdvanceWaypoint(b, s);
                if (s.PathCursor < s.Path.Count)
                {
                    var delta = s.Path[s.PathCursor] - s.Position;
                    float speed = CombatMovement.Speed(b, s);
                    V2 moved;
                    if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, speed * dt), out moved))
                    {
                        s.DistanceTravelled += V2.Distance(s.Position, moved);
                        s.Position = moved;
                        s.Facing = delta.Normalized;
                        s.Moving = true;
                        s.LastMoved = b.Time;
                        if (V2.Distance(moved, s.Path[s.PathCursor]) < .05f)
                            s.PathCursor++;
                    }
                    else if (b.Time - s.LastMoved > 1.5f && s.Motion.GroupWaitUntil <= b.Time)
                    {
                        s.Path.Clear();
                        s.PathCursor = 0;
                        b.Counterattack.NextRoute[s.Id] = 0;
                    }
                }
            }

            if (SequentialRetreat(b) && s.Path.Count > 0 && s.PathCursor >= s.Path.Count)
            {
                b.Counterattack.RetreatBoundDone = s.Id;
                b.Counterattack.RetreatMovers.Remove(s.Id);
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.State = ActionState.Observe;
                return false;
            }

            s.Crouched = s.Suppression > .55f;
            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }
    }
}
