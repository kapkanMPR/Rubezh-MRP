using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class BattleSimulation
    {
        public MissionMortarSupport StoryMortars;
        public int EnemyWeaponMission;
        public bool EnemyWeaponEndless;
        public readonly System.Collections.Generic.List<BlastCrater> Craters = new System.Collections.Generic.List<BlastCrater>();
        public bool OpeningAllowOpenGround = true;
        public bool ReconCombatTransitionEnabled = true;
        public bool OpeningFixedAccuracyEnabled = true;
        public bool OpeningIgnoreCoverEnabled = false, OpeningDoubleAccuracyEnabled = false;
        public bool OpeningHorizonEnabled = true, OpeningCriticalEnabled = true;
        public bool ReconOpeningEnabled = true;
        public bool DefenderRestEnabled = true, QuietReconEnabled = true, UsefulReconEnabled = true;
        public float ReconBudget = 14, ReconStandOff = 10;
        // Archived showcase battles use the pre-cover-denial rules of AI-0.12.25.
        public bool Historical01225;
        public readonly BattleVisibility Fog = new BattleVisibility();
        public readonly BattleAudioEvents AudioEvents = new BattleAudioEvents();
        public readonly LossMemoryState LossLearning = new LossMemoryState();
        internal readonly RouteSnapshot RouteView = new RouteSnapshot();
        internal readonly System.Collections.Generic.Dictionary<(Side, DefenderGroupState), int> MovementCounts = new System.Collections.Generic.Dictionary<(Side, DefenderGroupState), int>();
        public readonly AiDiagnostics Diagnostics = new AiDiagnostics();
        public readonly BattleHistory History = new BattleHistory();
        public readonly CriticalOutcomeReview Outcomes = new CriticalOutcomeReview();
        public readonly BallisticState Ballistics = new BallisticState();
        public readonly System.Collections.Generic.List<SmokeEffect> SmokeEffects = new System.Collections.Generic.List<SmokeEffect>();
        public readonly TacticalMap Tactics = new TacticalMap();
        public readonly SilentOrders.Core.Tuning Config;
        public readonly SilentOrders.Core.BattleMap Map;
        public readonly SilentOrders.Core.Rng Random;
        public readonly SilentOrders.Core.Rng VoiceRandom;
        public readonly int Seed;
        public int CombatSeed { get; private set; }
        // Observational counters: never read by simulation decisions or serialized state.
        public long SquadPlanCalls { get; private set; }
        public long SoldierDecisionCalls { get; private set; }

        internal void CountSquadPlan() => SquadPlanCalls++;
        // Call once after scenario, loadout and level overrides, before the first tick.
        public void InitializeCombatRandom(int seed)
        {
            if (Time != 0 || TickNumber != 0)
                throw new System.InvalidOperationException("Combat already started");
            CombatSeed = seed;
            Random.Reseed(seed);
            VoiceRandom.Reseed(seed ^ 0x1648D);
            foreach (var soldier in Soldiers)
                soldier.NextShot = Random.Range(.2f, 1f);
        }

        public readonly System.Collections.Generic.List<SilentOrders.Core.Soldier> Soldiers = new System.Collections.Generic.List<SilentOrders.Core.Soldier>();
        public readonly System.Collections.Generic.List<BloodTrace> BloodTraces = new System.Collections.Generic.List<BloodTrace>();
        // Pure visual particles; never consulted by movement, cover, collision, vision or targeting.
        public int CoverDebrisRevision;
        public readonly System.Collections.Generic.Dictionary<int, CoverDebrisPiece> CoverRemnants = new System.Collections.Generic.Dictionary<int, CoverDebrisPiece>();
        public readonly System.Collections.Generic.List<CoverDebrisPiece> CoverDebris = new System.Collections.Generic.List<CoverDebrisPiece>();
        public readonly System.Collections.Generic.List<CoverDebrisPiece> ShellCasings = new System.Collections.Generic.List<CoverDebrisPiece>();
        public readonly SilentOrders.Core.Squad Green = new SilentOrders.Core.Squad
        {
            Side = SilentOrders.Core.Side.Green,
            LeaderId = 0
        };
        private readonly SilentOrders.Core.Squad defaultRed = new SilentOrders.Core.Squad
        {
            Side = SilentOrders.Core.Side.Red,
            LeaderId = 5
        };
        public readonly System.Collections.Generic.List<DefenderGroupState> DefenderGroups = new System.Collections.Generic.List<DefenderGroupState>();
        public readonly CounterSniperIntel SniperIntel = new CounterSniperIntel();
        public bool CustomDefenders;
        public VehicleArrivalState GreenVehicle, RedVehicle;
        internal DefenderGroupState ActiveDefenderGroup;
        public Squad Red
        {
            get
            {
                return ActiveDefenderGroup == null ? defaultRed : ActiveDefenderGroup.Command;
            }
        }

        public Squad Team(Soldier soldier)
        {
            return soldier.DefenderGroup == null ? Team(soldier.Side) : soldier.DefenderGroup.Command;
        }

        public readonly System.Collections.Generic.List<SilentOrders.Core.BattleEvent> Events = new System.Collections.Generic.List<SilentOrders.Core.BattleEvent>();
        public readonly System.Collections.Generic.List<SilentOrders.Core.ShotTrace> Traces = new System.Collections.Generic.List<SilentOrders.Core.ShotTrace>();
        public readonly System.Collections.Generic.List<SilentOrders.Core.SmokeCloud> Smoke = new System.Collections.Generic.List<SilentOrders.Core.SmokeCloud>();
        public readonly System.Collections.Generic.List<SilentOrders.Core.ArcProjectile> Projectiles = new System.Collections.Generic.List<SilentOrders.Core.ArcProjectile>();
        public SceneEquipmentState SceneEquipment;
        public readonly System.Collections.Generic.List<GroundWeapon> GroundWeapons = new System.Collections.Generic.List<GroundWeapon>();
        public readonly System.Collections.Generic.List<GroundTourniquet> GroundTourniquets = new System.Collections.Generic.List<GroundTourniquet>();
        public float NextTourniquetPlan;
        public readonly System.Collections.Generic.List<GroundAmmo> GroundAmmo = new System.Collections.Generic.List<GroundAmmo>();
        public readonly System.Collections.Generic.List<GroundLauncher> GroundLaunchers = new System.Collections.Generic.List<GroundLauncher>();
        public readonly System.Collections.Generic.List<SilentOrders.Core.BlastEffect> Blasts = new System.Collections.Generic.List<SilentOrders.Core.BlastEffect>();
        public readonly SilentOrders.Core.Statistics Stats = new SilentOrders.Core.Statistics();
        public readonly System.Collections.Generic.List<SilentOrders.Core.RadioReport> Radio = new System.Collections.Generic.List<SilentOrders.Core.RadioReport>();
        public int CampaignLevel;
        public int CampaignReserve;
        private readonly RedCounterattackState defaultCounterattack = new RedCounterattackState();
        public RedCounterattackState Counterattack
        {
            get
            {
                return ActiveDefenderGroup == null ? defaultCounterattack : ActiveDefenderGroup.Counterattack;
            }
        }

        public float EnemyClearedAt = -1f;
        public bool VictoryByElimination;
        public bool RaidMode;
        public int CampaignTotal;
        public float Time;
        public float Capture;
        public bool Contested;
        public int TickNumber;
        public SilentOrders.Core.Outcome Outcome;
        public readonly GameplayRules Rules;
        public BattleSimulation(int seed = 410, SilentOrders.Core.Tuning tuning = null, GameplayRules rules = null)
        {
            Rules = rules == null ? GameplayRules.CaptureDefaults() : rules.Copy();
            Seed = seed;
            CombatSeed = seed;
            Config = tuning ?? new SilentOrders.Core.Tuning();
            Map = new SilentOrders.Core.BattleMap();
            Random = new SilentOrders.Core.Rng(seed);
            VoiceRandom = new SilentOrders.Core.Rng(seed ^ 0x1648D);
            string[] array = new string[10]
            {
                "КЕДР",
                "ЛИС",
                "СОКОЛ",
                "БАРС",
                "ТИХИЙ",
                "ГРАНИТ",
                "СЕВЕР",
                "ДЫМ",
                "ШИП",
                "ЯСТРЕБ"
            };
            SilentOrders.Core.V2[] array2 = new SilentOrders.Core.V2[10]
            {
                new SilentOrders.Core.V2(6.5f, 17.5f),
                new SilentOrders.Core.V2(6.5f, 15.5f),
                new SilentOrders.Core.V2(6.5f, 20.5f),
                new SilentOrders.Core.V2(5.5f, 15.5f),
                new SilentOrders.Core.V2(5.5f, 20.5f),
                new SilentOrders.Core.V2(50.5f, 18.5f),
                new SilentOrders.Core.V2(44.5f, 15.5f),
                new SilentOrders.Core.V2(44.5f, 20.5f),
                new SilentOrders.Core.V2(50.5f, 15.5f),
                new SilentOrders.Core.V2(50.5f, 21.5f)
            };
            for (int i = 0; i < 10; i++)
            {
                int num = i % 5;
                int num2 = ((i >= 5) ? 5 : 0);
                SilentOrders.Core.Soldier soldier = new SilentOrders.Core.Soldier(Rules)
                {
                    Id = i,
                    Name = array[i],
                    KedrBinoculars = i == 0,
                    Side = ((i >= 5) ? SilentOrders.Core.Side.Red : SilentOrders.Core.Side.Green),
                    Role = (SilentOrders.Core.Role)num,
                    Element = ((num == 0) ? (-1) : ((num <= 2) ? 1 : 0)),
                    BuddyId = num2 + ((num == 0) ? 1 : ((num % 2 == 1) ? (num + 1) : (num - 1))),
                    Position = Map.NearestOpen(array2[i]),
                    NextSense = (float)i * 0.023f,
                    NextDecision = (float)i * 0.055f,
                    Facing = new SilentOrders.Core.V2((i < 5) ? 1 : (-1), 0f),
                    NextShot = Random.Range(0.2f, 1f)
                };
                soldier.SupplyPack = num == 4;
                soldier.PackRockets = ((num == 4) ? 2 : 0);
                soldier.PackBoxes = ((num == 4) ? 1 : 0);
                soldier.PackMagazines = ((num == 4) ? 10 : 0);
                soldier.Tourniquets = ((num != 2) ? 1 : 5);
                soldier.Rockets = ((num == 3) ? 1 : 0);
                soldier.SmokeCharges = ((num == 0) ? 3 : 0);
                if (num == 1)
                {
                    soldier.WeaponVariant = 1;
                    soldier.Ammo = 100;
                    soldier.Magazines.Reset(soldier, 300);
                }

                soldier.Destination = (soldier.OrderAnchor = soldier.Position);
                Soldiers.Add(soldier);
            }

            foreach (var enemy in Soldiers)
                if (enemy.Side == SilentOrders.Core.Side.Red)
                    SilentOrders.Core.EnemyInventory.Configure(enemy, Soldiers.FindAll(x => x.Side == SilentOrders.Core.Side.Red));
            Green.Character = (SilentOrders.Core.CommanderCharacter)(System.Math.Abs((long)seed) % 3);
            Red.Character = (SilentOrders.Core.CommanderCharacter)((System.Math.Abs((long)seed) + 1) % 3);
            Log(SilentOrders.Core.Side.Green, "Отделение КЕДР: занять точку «Рубеж». Беречь людей.");
            Log(SilentOrders.Core.Side.Red, "Отделение ГРАНИТ: удерживать подходы к точке.");
        }

        public SilentOrders.Core.Squad Team(SilentOrders.Core.Side side)
        {
            return (side == SilentOrders.Core.Side.Green) ? Green : Red;
        }

        public int Alive(SilentOrders.Core.Side side)
        {
            int num = 0;
            foreach (SilentOrders.Core.Soldier soldier in Soldiers)
            {
                if (soldier.Side == side && soldier.Alive)
                {
                    num++;
                }
            }

            return num;
        }

        public void Log(SilentOrders.Core.Side side, string message)
        {
            string b = message.Trim().TrimEnd('.');
            int num = Events.Count - 1;
            while (num >= 0 && Time - Events[num].Time < 4f)
            {
                if (Events[num].Side == side && Events[num].Text.Trim().TrimEnd('.') == b)
                {
                    return;
                }

                num--;
            }

            History.Event(this, "journal", -1, -1, new V2(), new V2(), 0, side + ": " + message);
            Events.Add(new SilentOrders.Core.BattleEvent { Time = Time, Side = side, Text = message });
            if (Events.Count > 100)
            {
                Events.RemoveAt(0);
            }
        }

        public void Say(SilentOrders.Core.Soldier s, string phrase, bool urgent = false)
        {
            // Preserve the unique panic quote during its brief on-screen lifetime.
            bool grenadeWarning = urgent && (phrase == "СВОЯ!" || phrase == "ВЫСТРЕЛ!!");
            bool wallCommand = urgent && (phrase.StartsWith("Код СТЕНА!", System.StringComparison.Ordinal) || phrase == "КОНТАКТ!!" || phrase == "Готовность. Движение");
            if (!grenadeWarning && (s.RetreatQuoteUntil > Time || s.CasualtyRecovery.QuoteUntil > Time))
                return;
            if (s.Alive && (grenadeWarning || wallCommand || (!(Time - s.SpeechAt < 1.5f) || s.LastSpoken.Length <= 0) && (urgent || !(Time < s.SpeechCooldown))))
            {
                phrase = SilentOrders.Core.Dialogue.Adapt(this, s, phrase);
                if (grenadeWarning || wallCommand || !(phrase == s.LastSpoken) || !(Time - s.LastSpokenAt < 10f))
                {
                    s.Speech = phrase;
                    s.SpeechShort = SilentOrders.Core.Dialogue.Short(phrase);
                    s.SpeechUrgent = urgent;
                    s.SpeechAt = Time;
                    s.LastSpoken = phrase;
                    s.LastSpokenAt = Time;
                    s.SpeechUntil = Time + 4.2f;
                    s.SpeechCooldown = Time + VoiceRandom.Range(5f, 8f);
                    Log(s.Side, s.Name + ": " + phrase);
                }
            }
        }

        public bool ContactManeuversEnabled = true, SuppressionVisionEnabled = true;
        public bool PeripheralFocusEnabled = true, SurpriseBurstEnabled = true, StealthChoiceEnabled = true;
        public int PairExperimentProfile;
        public bool PairExperimentMarch;
        public bool PairExperimentCover;
        public PairPlanSettings PairPlan;
        public bool TacticalMarchEnabled = true, ReactionTimingEnabled = true;
        bool deploymentPrepared;
        public void PrepareDeployment()
        {
            if (deploymentPrepared) return;
            SpawnLayout.Apply(this);
            deploymentPrepared = true;
        }

        public void Step()
        {
            PrepareDeployment();
            if (Outcome != 0)
            {
                StepCore();
                return;
            }

            Map.GeometryQueries.BeginTick(Map);
            try
            {
                StepCore();
            }
            finally
            {
                Map.GeometryQueries.EndTick();
            }
        }

        private void StepCore()
        {
            foreach (var s in Soldiers)
                if (s.CampReserveRounds > 0 && s.GrenadeCharges == 0)
                {
                    s.GrenadeType = s.CampReserveType;
                    s.GrenadeCharges = s.CampReserveRounds;
                    s.PocketMask |= 1 << s.GrenadeType;
                    s.CampReserveRounds = 0;
                }

            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.Simulation.Step"))
            {
                if (Outcome != 0)
                {
                    return;
                }

                float tick = Config.Tick;
                Time += tick;
                StoryMortars?.Update(this);
                CareExperiments.Tick(this);
                SilentOrders.Core.Ballistics.BeginStep(this);
                VehicleArrivals.Update(this);
                TickNumber++;
                for (int num = Radio.Count - 1; num >= 0; num--)
                {
                    SilentOrders.Core.RadioReport radioReport = Radio[num];
                    if (!(radioReport.DeliverAt > Time))
                    {
                        if (Soldiers[radioReport.Sender].Alive)
                        {
                            Merge(Team(radioReport.Side).Contacts, radioReport.Contact);
                            foreach (SilentOrders.Core.Soldier soldier2 in Soldiers)
                            {
                                if (soldier2.Alive && soldier2.Side == radioReport.Side)
                                {
                                    Merge(soldier2.Contacts, radioReport.Contact);
                                }
                            }

                            Stats.Reports++;
                        }

                        Radio.RemoveAt(num);
                    }
                }

                foreach (SilentOrders.Core.Soldier soldier3 in Soldiers)
                {
                    if (!soldier3.Alive || !soldier3.Deployed)
                    {
                        continue;
                    }

                    if (soldier3.Health <= 20f && !soldier3.Incapacitated)
                    {
                        soldier3.Incapacitated = true;
                        Outcomes.BleedingWound(this, soldier3, (soldier3.BleedSourceId >= 0 && soldier3.BleedSourceId < Soldiers.Count) ? Soldiers[soldier3.BleedSourceId] : null);
                        SilentOrders.Core.TacticalAI.Stop(soldier3);
                        soldier3.SupportReady = false;
                        Stats.CriticalWounds++;
                        SilentOrders.Core.Dialogue.Say(this, soldier3, 32, urgent: true);
                    }

                    if (soldier3.Health <= 20f && !soldier3.Stabilized)
                    {
                        soldier3.Bleeding = true;
                    }

                    if (soldier3.Bleeding)
                    {
                        if (soldier3.BleedSourceId >= 0 && Soldiers[soldier3.BleedSourceId].Side != soldier3.Side)
                        {
                            Soldiers[soldier3.BleedSourceId].DamageDealt += System.Math.Min(soldier3.Health, tick);
                        }

                        soldier3.Health = System.Math.Max(0f, soldier3.Health - tick);
                        if (!soldier3.Alive)
                        {
                            soldier3.Bleeding = false;
                            Down(soldier3, (soldier3.BleedSourceId >= 0) ? Soldiers[soldier3.BleedSourceId] : null, "bleeding");
                            Log(soldier3.Side, soldier3.Name + ": погиб от кровопотери.");
                            continue;
                        }
                    }

                    if (soldier3.Incapacitated || soldier3.Broken || soldier3.Suppression > 0.8f)
                    {
                        if (soldier3.ImmobileSince < 0f)
                        {
                            soldier3.ImmobileSince = Time;
                        }
                    }
                    else
                    {
                        soldier3.ImmobileSince = -1f;
                    }

                    bool flag = PositionLogic.Protected(this, soldier3, soldier3.Position);
                    bool flag2 = Time - soldier3.LastIncoming < 2f;
                    soldier3.Suppression = System.Math.Max(0f, soldier3.Suppression - tick * (flag ? 0.16f : 0.08f));
                    float num2 = 0f;
                    foreach (SilentOrders.Core.Soldier soldier4 in Soldiers)
                    {
                        if (soldier4.Id != soldier3.Id && soldier4.Side == soldier3.Side && soldier4.Available && !soldier4.Broken && SilentOrders.Core.V2.Distance(soldier3.Position, soldier4.Position) < 6f)
                        {
                            num2 += ((soldier4.Id == Team(soldier3).LeaderId) ? 0.025f : 0.008f);
                        }
                    }

                    float num3 = ((num2 == 0f && flag2) ? 0.006f : 0f);
                    float num4 = (flag2 ? (-0.024f * soldier3.Suppression - num3) : (0.012f + num2));
                    if (Time - soldier3.LastBlast < 4f && flag2)
                    {
                        num4 -= 0.006f;
                    }

                    if (num4 > 0f)
                    {
                        num4 = MoraleDynamics.RecoveryRate(soldier3, num4);
                    }

                    // Immediately after psychological collapse no positive regeneration is possible.
                    // Incoming fire can still reduce morale; the pause is not a permanent lock.
                    if (soldier3.Broken && num4 > 0f && (Time < soldier3.MoraleRecoveryBlockedUntil || Time - soldier3.LastIncoming <= 3f))
                        num4 = 0f;
                    if (flag2 && num4 < 0f && soldier3.Morale < .35f)
                        num4 *= 1.18f; // low morale amplifies sustained-fire stress slightly
                    soldier3.Morale = SilentOrders.Core.Util.Clamp(soldier3.Morale + tick * num4, 0f, soldier3.MoraleMaximum);
                    if (flag)
                    {
                        soldier3.CoverTime += tick;
                        Stats.CoverSeconds += tick;
                    }
                    else
                    {
                        soldier3.ExposedTime += tick;
                        Stats.ExposedSeconds += tick;
                    }

                    if (soldier3.Morale <= .001f && flag2 && !soldier3.Broken)
                        MoraleDynamics.EnterBroken(this, soldier3);
                    CombatCohesion.AwardRelief(this, soldier3);
                    if (!soldier3.Incapacitated && soldier3.RescuerId < 0 && Time >= soldier3.NextSense)
                    {
                        using (new CpuSample("Rubezh.AI.sense"))
                            SilentOrders.Core.Perception.Observe(this, soldier3);
                        soldier3.NextSense = Time + Config.PerceptionPeriod;
                    }
                }

                foreach (var wounded in Soldiers)
                    CareExperiments.WoundedSense(this, wounded);
                SupplyRecovery.Tick(this);
                BuddyActions.Tick(this, tick);
                NewMechanics.Tick(this);
                AiTasks.Maintain(this);
                CasualtyRecovery.Maintain(this);
                LocalTriage.Update(this);
                TourniquetLogistics.Plan(this);
                CombatCohesion.Tick(this);
                TacticalOrders.ApplyPending(this, Green);
                TacticalOrders.ApplyPending(this, Red);
                QuietRecon.Update(this);
                ReconOpening.Update(this);
                ReconCombatPhase.Update(this);
                TacticalMarch.Update(this, Green);
                DefenderGroupsRuntime.Update(this);
                SilentOrders.Core.CampaignScenario.Update(this);
                RedCounterattack.Tick(this);
                SilentOrders.Core.SmokePlanner.Update(this, Green);
                if (!CustomDefenders)
                    SilentOrders.Core.SmokePlanner.Update(this, Red);
                if (Time >= Green.NextPlan && !VehicleArrivals.Disembarking(this, Side.Green))
                {
                    SilentOrders.Core.SupportAI.Plan(this, Green);
                    SilentOrders.Core.SupplyAI.Plan(this, Green);
                    SilentOrders.Core.TaskReview.Group(this, Green);
                    using (new CpuSample("Rubezh.AI.squad"))
                        SilentOrders.Core.SquadBrain.Plan(this, Green);
                    Green.NextPlan = Time + Config.SquadPeriod;
                }

                if (!CustomDefenders && Time >= Red.NextPlan)
                {
                    SilentOrders.Core.SupportAI.Plan(this, Red);
                    SilentOrders.Core.SupplyAI.Plan(this, Red);
                    SilentOrders.Core.TaskReview.Group(this, Red);
                    using (new CpuSample("Rubezh.AI.squad"))
                        SilentOrders.Core.SquadBrain.Plan(this, Red);
                    Red.NextPlan = Time + Config.SquadPeriod;
                }

                FriendlyGrenadeSafety.Update(this);
                TacticalMarch.EnforceCover(this, Green);
                ContactMovement.EnforceSupport(this);
                PairTacticalPlan.Enforce(this);
                QuietRecon.Enforce(this);
                ReconOpening.Enforce(this);
                for (int i = 0; i < Soldiers.Count; i++)
                {
                    SilentOrders.Core.Soldier soldier = Soldiers[(i + TickNumber) % Soldiers.Count];
                    if (soldier.Alive && soldier.Deployed)
                    {
                        if (soldier.Available && !BuddyActions.Active(soldier) && !SupplyRecovery.Active(soldier) && !HandLauncher.Active(soldier) && !PairExchange.Active(this, soldier) && !CombatCrouch.Standing(this, soldier) && !FriendlyGrenadeSafety.Active(this, soldier) && !soldier.GrenadeEscape.Active && !soldier.VehicleDisperse && !soldier.VehicleRally && Time >= soldier.NextDecision)
                        {
                            // Keep the decision due while executing: retreat logic reads this deadline.
                            soldier.NextDecision = float.NegativeInfinity;
                            float requestedDecision = float.NegativeInfinity;
                            try
                            {
                                if (!SilentOrders.Core.SceneEquipment.TryUpdate(this, soldier))
                                {
                                    SoldierDecisionCalls++;
                                    using (new CpuSample("Rubezh.AI.decide"))
                                        SilentOrders.Core.TacticalAI.Decide(this, soldier);
                                }
                            }
                            finally
                            {
                                requestedDecision = soldier.NextDecision;
                                // An exception must not strand the scheduler in a sentinel state.
                                if (float.IsNegativeInfinity(soldier.NextDecision))
                                    soldier.NextDecision = Time;
                            }

                            float scheduledDecision = Time + (Config.DecisionPeriod + Random.Range(0f, 0.12f)) * ConcussionEffects.ReactionTimeMultiplier(ConcussionEffects.Current(this, soldier));
                            soldier.NextDecision = float.IsNegativeInfinity(requestedDecision) ? scheduledDecision : Math.Min(requestedDecision, scheduledDecision);
                        }

                        MotionCombat.Begin(this, soldier);
                        CombatCrouch.Begin(this, soldier);
                        PairExchange.Review(this, soldier);
                        ShoulderPair.Update(this, soldier);
                        using (new CpuSample("Rubezh.AI.actor"))
                            UpdateSoldier(soldier, tick);
                        MotionCombat.End(this, soldier, tick);
                        CombatCrouch.End(this, soldier, tick);
                        if (soldier.Awareness.Initialized && soldier.Available && !soldier.Busy && soldier.HandsBusyUntil <= Time && Time - soldier.LastFire > .25f)
                            soldier.Facing = soldier.Awareness.Look;
                        AiTasks.Observe(this, soldier);
                    }
                }

                GroupMovement.Sample(this);
                SilentOrders.Core.Ballistics.Update(this);
                BloodTrail.Update(this);
                SmokeTactics.ReviewGroup(this, Green);
                if (!CustomDefenders)
                    SmokeTactics.ReviewGroup(this, Red);
                foreach (var g in DefenderGroups)
                    if (g.Deployed)
                        SmokeTactics.ReviewGroup(this, g.Command);
                SilentOrders.Core.ArcedOrdnance.Update(this);
                BlastCrater.Record(this);
                PairExchange.Sweep(this);
                SmokeTactics.Tick(this);
                CoverDebrisSystem.Consume(this);
                Traces.RemoveAll((SilentOrders.Core.ShotTrace t) => t.Until < Time);
                Smoke.RemoveAll((SilentOrders.Core.SmokeCloud t) => t.Until <= Time);
                Blasts.RemoveAll((SilentOrders.Core.BlastEffect t) => t.Until <= Time);
                foreach (var wounded in Soldiers)
                    CasualtyRecovery.UpdateVoice(this, wounded);
                Dialogue.Refresh(this);
                UpdateObjective(tick);
                History.Sample(this);
                Outcomes.Sample(this);
            }
        }

        private static void Merge(System.Collections.Generic.Dictionary<int, SilentOrders.Core.Contact> contacts, SilentOrders.Core.Contact incoming)
        {
            if (!contacts.TryGetValue(incoming.EnemyId, out var value) || value.SeenAt < incoming.SeenAt)
            {
                SilentOrders.Core.Contact contact = incoming.Copy();
                contact.Reported = true;
                contacts[incoming.EnemyId] = contact;
            }
        }

        private void UpdateSoldier(SilentOrders.Core.Soldier s, float dt)
        {
            s.Moving = false;
            if (!s.Deployed)
                return;
            if (CombatCrouch.Standing(this, s) && !FriendlyGrenadeSafety.Active(this, s))
            {
                s.SupportReady = false;
                s.State = ActionState.Observe;
                return;
            }

            NewMechanics.ThrowBack(this, s);
            if (GrenadeSafety.Update(this, s, dt))
                return;
            if (FriendlyGrenadeSafety.Hold(this, s))
                return;
            if (PairExchange.Move(this, s, dt))
                return;
            if (SupplyRecovery.Hold(this, s, dt))
                return;
            if (HandLauncher.Update(this, s, dt))
                return;
            // Retired experimental movement never owns a turn; clear legacy state only.
            s.Research.MoveKind = 0;
            if (BuddyActions.Hold(this, s, dt))
                return;
            if (TrafficCoordination.Update(this, s, dt))
                return;
            if (VehicleArrivals.Disperse(this, s, dt))
                return;
            if (VehicleArrivals.Rally(this, s, dt))
                return;
            if (OpenGroundSpacing.Move(this, s, dt))
                return;
            if (!s.Available || s.Broken || s.Suppression > 0.7f || s.ReloadUntil > Time)
            {
                s.RocketAimTarget = -1;
            }

            LauncherLogistics.TryCollectNearby(this, s);
            AmmoLogistics.CollectNearby(this, s);
            if (TourniquetLogistics.UpdatePatient(this, s, dt))
                return;
            if (SilentOrders.Core.SupplyAI.Update(this, s, dt) || SilentOrders.Core.SupportAI.Update(this, s, dt))
            {
                return;
            }

            if (s.Broken)
            {
                s.State = SilentOrders.Core.ActionState.Cower;
                s.Crouched = true;
                s.SupportReady = false;
                if (PositionLogic.Protected(this, s, s.Position))
                {
                    SilentOrders.Core.TacticalAI.Stop(s);
                }
                else
                {
                    CrawlToCover(s, dt);
                }

                return;
            }

            if (s.ReloadUntil > Time)
            {
                s.State = SilentOrders.Core.ActionState.Reload;
                s.Crouched = true;
                return;
            }

            if (s.ReloadUntil > 0f)
            {
                MagazineSystem.Reload(this, s);
                s.ReloadUntil = 0f;
                SilentOrders.Core.Dialogue.Say(this, s, 7);
            }

            if (s.HandsBusyUntil > Time && s.Motion.MobileSmokeUntil <= Time)
            {
                s.SupportReady = false;
                s.State = ActionState.Observe;
                return;
            }

            if (SmokeTactics.AssaultMove(this, s, dt))
                return;
            if (EngagementPolicy.UpdateApproach(this, s, dt))
                return;
            if (CounterSniper.Move(this, s, dt))
                return;
            MotionCombat.Smoke(this, s);
            bool waitingSmoke = s.Motion.MobileSmokeUntil <= Time && SmokeTactics.Waiting(this, s);
            bool forcedMovement = !waitingSmoke && (ProgressRecovery.Move(this, s, dt) || ShoulderPair.Move(this, s, dt) || (LastOpponentHunt.Owns(this, s) ? LastOpponentHunt.Move(this, s, dt) : (RedCounterattack.Move(this, s, dt) || TacticalOrders.UpdatePriorityMovement(this, s, dt))));
            if (forcedMovement && TacticalOrders.Emergency(Team(s)))
                return;
            AmmoLogistics.TryBuddy(this, s);
            if (s.HandsBusyUntil > Time && s.Motion.MobileSmokeUntil <= Time)
                return;
            // A nearby fallen weapon is a finite resource, not free ammunition.
            // The scavenger still travels using the ordinary movement/cover system.
            if (!WithdrawalTactics.Active(Team(s)) && Team(s).Style != CombatStyle.Passive)
                CorpseEquipment.TryUpdate(this, s);
            bool flag = SilentOrders.Core.CombatCoordination.WantsReload(this, s) && MagazineSystem.BetterMagazine(s);
            if ((s.Ammo <= 0 || flag) && s.Reserve > 0)
            {
                s.BuddyAction.ReloadAssisted = false;
                s.ReloadUntil = Time + WeaponBalance.ReloadSeconds(s);
                s.State = SilentOrders.Core.ActionState.Reload;
                Stats.Reloads++;
                if (flag)
                {
                    Stats.TacticalReloads++;
                    s.NextTacticalReload = Time + 18f;
                }

                SilentOrders.Core.Dialogue.Say(this, s, (s.GunMachineGunner) ? 31 : 5, urgent: true);
                SilentOrders.Core.Soldier soldier = Soldiers[s.BuddyId];
                if (soldier.Alive && soldier.Suppression < 0.7f)
                {
                    soldier.NextDecision = 0f;
                    SilentOrders.Core.Dialogue.Say(this, soldier, 6, urgent: true);
                }
            }
            else if (s.Suppression > 0.87f && !CombatCohesion.BypassCover(this, s))
            {
                s.State = SilentOrders.Core.ActionState.Pinned;
                s.Crouched = true;
            }
            else
            {
                if (CloseCombat.TryStrike(this, s))
                    return;
                if (SilentOrders.Core.ArcedOrdnance.TryUse(this, s) || SilentOrders.Core.SpecialWeapons.TryLaunch(this, s))
                {
                    return;
                }

                if (!forcedMovement && !MotionCombat.InMotion(this, s))
                    FiringPositionPolicy.Hold(this, s);
                SomaliFire.PreparePosture(this, s);
                SilentOrders.Core.Contact contact = SilentOrders.Core.TacticalAI.FireTarget(this, s);
                if (contact == null)
                {
                    s.FireStatus = ((s.Ammo <= 0) ? "Нет БК" : ((s.ReloadUntil > Time) ? "Перезарядка" : "Сектор потерян"));
                }

                bool flag2 = s.Job == SilentOrders.Core.Job.Cover || s.Job == SilentOrders.Core.Job.Hold || s.Job == SilentOrders.Core.Job.Command;
                bool flag3 = (MotionCombat.CanFire(this, s) || SomaliFire.CanAttempt(this, s, contact)) && s.HandsBusyUntil <= Time && contact != null && s.Ammo > 0 && Time >= s.NextShot && Time >= s.ConcussionReactUntil;
                bool flag4 = !forcedMovement && s.PathCursor < s.Path.Count;
                if (flag3 && (!flag4 || flag2 || MotionCombat.InMotion(this, s) || SilentOrders.Core.V2.Distance(s.Position, contact.Position) < 5f) && s.RecoverUntil <= Time)
                {
                    if (!FriendlyInLane(s, contact.Position))
                    {
                        Fire(s, contact);
                    }
                    else
                    {
                        Stats.FriendlyFireHolds++;
                        s.Reason = "Союзник на линии огня — удерживаю огонь";
                    }
                }

                flag4 = !forcedMovement && s.PathCursor < s.Path.Count; // Fire may hand movement to a short approach.
                bool flag5 = Time - s.LastFire < 0.24f && !MotionCombat.InMotion(this, s);
                bool flag6 = flag4 && WithdrawalTactics.Active(Team(s)) && !WithdrawalTactics.HasCover(this, s) || flag4 && !PositionLogic.ShelterMoving(this, s) && s.FireLaneCrossUntil <= Time && !(s.Sniper && s.SniperMoving) && (PositionLogic.Protected(this, s, s.Position) || (Time - s.LastIncoming > 5f && SilentOrders.Core.TacticalAI.Risk(this, s, s.Position) < 0.01f)) && SilentOrders.Core.V2.Distance(s.Position, s.Destination) > 0.7f && !SilentOrders.Core.SquadBrain.MovementCovered(this, s) && !CombatCohesion.BypassCover(this, s);
                if (FormationEnvelope.Active(this, s) || ReconOpening.Owns(this, s) || QuietRecon.Owns(this, s) || WallTravel.Active(this, s) || PairTacticalPlan.StepCovered(this, s))
                    flag6 = false;
                if (flag6)
                {
                    s.State = SilentOrders.Core.ActionState.WaitCover;
                    s.WaitTime += dt;
                    s.Reason = "Перед опасным участком: жду подтверждения прикрытия";
                    SilentOrders.Core.Dialogue.Say(this, s, 4);
                }

                // Ordinary flanking and inter-wing transfers require tactical
                // cover clearance; the emergency escape and pinned-rescue bypasses
                // retain their dedicated higher priorities.
                if (flag4 && !flag5 && !flag6 && !waitingSmoke && (!TacticalOrders.Flanks(Team(s)) || ObjectiveOpportunity.MovementAllowed(this, s) || WallTravel.Active(this, s) || PositionLogic.ShelterMoving(this, s) || s.FireLaneCrossUntil > Time || CombatCohesion.BypassCover(this, s) || FlankTactics.CanAdvance(this, s)))
                {
                    SilentOrders.Core.Locomotion.AdvanceWaypoint(this, s);
                    SilentOrders.Core.V2 v = s.Path[s.PathCursor];
                    SilentOrders.Core.V2 v2 = v - s.Position;
                    float num2 = SilentOrders.Core.CombatMovement.Speed(this, s);
                    if (SilentOrders.Core.Locomotion.TryStep(this, s, v2.Normalized * System.Math.Min(v2.Length, num2 * dt), out var next))
                    {
                        s.DistanceTravelled += SilentOrders.Core.V2.Distance(s.Position, next);
                        s.Position = next;
                        s.CoverWaitStartedAt = -1f;
                        s.Facing = v2.Normalized;
                        s.Moving = true;
                        s.LastMoved = Time;
                        if (SilentOrders.Core.V2.Distance(next, v) < 0.04f)
                        {
                            s.PathCursor++;
                        }
                    }
                    else if (!CombatCrouch.Standing(this, s) && Time - s.Ai.Started > 1.5f && Time - s.LastMoved > 1.5f && s.Motion.GroupWaitUntil <= Time)
                    {
                        s.NextDecision = 0f;
                        s.Path.Clear();
                        s.PathCursor = 0;
                    }
                }

                s.Crouched = (!s.Moving && PositionLogic.Protected(this, s, s.Position)) || s.Suppression > 0.55f;
                if (!flag5 && !flag6)
                {
                    s.State = ((!s.Moving) ? ((s.RecoverUntil > Time) ? SilentOrders.Core.ActionState.Recover : ((s.MoveAuthorized || (s.Job != 0 && s.Job != SilentOrders.Core.Job.Flank)) ? SilentOrders.Core.ActionState.Observe : SilentOrders.Core.ActionState.WaitCover)) : SilentOrders.Core.ActionState.Move);
                }
            }
        }

        private void CrawlToCover(SilentOrders.Core.Soldier s, float dt)
        {
            if (!PositionLogic.Protected(this, s, s.Destination) || V2.Distance(s.Position, s.Destination) > 3.2f)
            {
                TacticalAI.Stop(s);
                return;
            }

            if (s.PathCursor >= s.Path.Count)
            {
                return;
            }

            SilentOrders.Core.V2 v = s.Path[s.PathCursor] - s.Position;
            SilentOrders.Core.V2 v2 = s.Position + v.Normalized * System.Math.Min(v.Length, Config.MoveSpeed * BodyArmor.Multiplier(s.ArmorLevel) * ConcussionEffects.MovementMultiplier(ConcussionEffects.Current(this, s)) * 0.32f * dt);
            foreach (SilentOrders.Core.Soldier soldier in Soldiers)
            {
                if (soldier.Alive && soldier.Id != s.Id && SilentOrders.Core.V2.Distance(v2, soldier.Position) < 0.4f)
                {
                    return;
                }
            }

            if (MedicTactics.AllowStep(this, s, v2) && !PairExchange.Reserved(this, s, v2) && Map.Walkable(v2) && Locomotion.WalkableSegment(this, s.Position, v2) && FireLaneSafety.AllowStep(this, s, v2 - s.Position))
            {
                s.DistanceTravelled += V2.Distance(s.Position, v2);
                s.Facing = v.Normalized;
                s.Position = v2;
                s.Moving = true;
                s.LastMoved = Time;
                if (v.Length < 0.06f)
                {
                    s.PathCursor++;
                }
            }
        }

        public bool FriendlyInLane(SilentOrders.Core.Soldier shooter, SilentOrders.Core.V2 target)
        {
            if (shooter.FireHoldUntil > Time)
                return true;
            foreach (SilentOrders.Core.Soldier soldier in Soldiers)
            {
                if (soldier.Alive && soldier.Deployed && soldier.Side == shooter.Side && soldier.Id != shooter.Id && !ShoulderPair.CanShootOver(this, shooter, soldier, target) && SilentOrders.Core.V2.SegmentDistance(soldier.Position, shooter.Position, target) < 0.45f && SilentOrders.Core.V2.Distance(soldier.Position, shooter.Position) > 0.2f)
                {
                    return true;
                }
            }

            return false;
        }

        private void Fire(SilentOrders.Core.Soldier shooter, SilentOrders.Core.Contact contact)
        {
            if (shooter.SomaliNext > Time)
                return;
            if (ReconOpening.HoldFire(this, shooter) || QuietRecon.HoldFire(this, shooter) || (!ReconOpening.ActiveFire(this, shooter) && PairTacticalPlan.HoldFire(this, shooter, contact)))
                return;
            bool somaliShot = SomaliFire.CanAttempt(this, shooter, contact);
            if (!somaliShot && !CombatCrouch.FireAllowed(this, shooter, contact.Position))
                return;
            if (!shooter.Deployed || !MotionCombat.CanFire(this, shooter) || shooter.HandsBusyUntil > Time)
                return;
            if (V2.Distance(shooter.Position, contact.Position) > WeaponRange(shooter) && !EngagementPolicy.EmergencyAllowed(this, shooter, contact))
                return;
            bool sightReady = DirectionalVision.ReadyToFire(this, shooter, contact);
            bool reactionReady = ReactionTiming.WeaponReady(this, shooter, contact);
            if (!reactionReady || !sightReady)
                return;
            if (!somaliShot && !MotionCombat.InMotion(this, shooter) && !ReconOpening.ActiveFire(this, shooter) && EngagementPolicy.TryApproach(this, shooter, contact))
                return;
            if (!MotionCombat.SafeFire(this, shooter, contact.Position) || FriendlyInLane(shooter, contact.Position) || FireLaneSafety.HoldFire(this, shooter, contact.Position))
            {
                Stats.FriendlyFireHolds++;
                return;
            }

            bool flag = shooter.Visible.Contains(contact.EnemyId);
            SilentOrders.Core.V2 position = contact.Position;
            if (!somaliShot && !Map.LineOfSight(shooter.Position, position))
            {
                return;
            }

            V2 muzzle;
            if (somaliShot)
                SomaliFire.Pose(this, shooter, position, out muzzle, out var low);
            else if (!SilentOrders.Core.Ballistics.TryMuzzle(this, shooter, position, out muzzle))
            {
                shooter.SupportReady = false;
                shooter.NextDecision = Time;
                shooter.Reason = "Ствол перекрыт: нужна другая огневая позиция";
                return;
            }

            if (somaliShot && !SomaliFire.Activate(this, shooter, contact))
                return;
            EngagementPolicy.Fired(this, shooter, contact);
            if (shooter.Side == Side.Green && AreaSuppression.Active(this, shooter))
                shooter.Mechanics.AreaShots++;
            shooter.Ammo--;
            shooter.Shots++;
            ReconOpening.Fired(this, shooter, contact);
            if (MotionCombat.InMotion(this, shooter))
                shooter.Motion.MovingShots++;
            if (shooter.Motion.FrontId >= 0 && shooter.Motion.FrontId < Soldiers.Count && ShoulderPair.CanShootOver(this, shooter, Soldiers[shooter.Motion.FrontId], contact.Position))
                shooter.Motion.ShoulderShots++;
            Stats.Shots++;
            AudioEvents.Emit(Time, shooter.GunSniper ? 2 : shooter.GunMachineGunner ? 1 : 0, shooter.Id);
            shooter.LastFire = Time;
            FireLaneSafety.Fired(this, shooter, contact.Position);
            shooter.NextShot = Time + WeaponBalance.For(shooter).FireSeconds + shooter.Suppression * 0.6f;
            if (!shooter.GunSniper && (shooter.WeaponModel == 1 || AreaSuppression.Active(this, shooter) || LastOpponentHunt.IsCover(this, shooter)))
            {
                if (shooter.BurstRounds <= 0)
                {
                    shooter.BurstRounds = AreaSuppression.BurstSize(this, shooter);
                }

                shooter.BurstRounds--;
                shooter.NextShot = Time + ((shooter.BurstRounds > 0) ? WeaponBalance.For(shooter).FireSeconds : WeaponBalance.For(shooter).FireSeconds * 9f) + shooter.Suppression * 0.2f;
            }

            if (shooter.WeaponModel >= 4)
            {
                shooter.NextShot = Time + WeaponBalance.For(shooter).FireSeconds + shooter.Suppression * 0.6f;
            }

            SilentOrders.Core.Squad squad = Team(shooter);
            if (squad.EnvelopmentActive && squad.SupportIds.Contains(shooter.Id) && !shooter.GunSniper && shooter.Role != SilentOrders.Core.Role.Medic)
            {
                shooter.NextShot = Time + (shooter.NextShot - Time) * 0.78f;
            }

            if (squad.Style == SilentOrders.Core.CombatStyle.Passive)
            {
                shooter.NextShot += 0.4f;
            }

            if (shooter.SuppressiveFire)
            {
                Stats.SuppressionShots++;
                shooter.NextShot = Time + (shooter.NextShot - Time) * ((squad.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 0.7f : 0.85f);
            }

            // After style/suppression modifiers, all actual sniper weapons fire
            // half as often; the change applies equally to Red and Green.
            // Blind-fire eligibility is established before posture/LOS restrictions.
            if (somaliShot)
            {
                shooter.NextShot = Time + .18f;
                shooter.FireStatus = "Сомалийская стрельба";
            }
            else
            {
                shooter.FireStatus = V2.Distance(shooter.Position, contact.Position) > WeaponRange(shooter) ? "Экстренный огонь: точность ×0.5" : (shooter.SuppressiveFire ? "Подавляет позицию" : "Прицельный огонь");
            }

            shooter.Facing = (position - shooter.Position).Normalized;
            shooter.State = ((flag && !shooter.SuppressiveFire && shooter.Role != SilentOrders.Core.Role.MachineGunner) ? SilentOrders.Core.ActionState.Fire : SilentOrders.Core.ActionState.Suppress);
            var launched = SilentOrders.Core.Ballistics.Launch(this, shooter, contact, muzzle);
            CoverDebrisSystem.EjectCasing(this, shooter, muzzle, contact.Position);
            SomaliFire.Finish(this, shooter);
            foreach (SilentOrders.Core.Soldier soldier2 in Soldiers)
            {
                if (!shooter.GunSniper && soldier2.Alive && soldier2.Side != shooter.Side && SilentOrders.Core.V2.Distance(soldier2.Position, shooter.Position) < 17f && !soldier2.Visible.Contains(shooter.Id) && (!soldier2.Contacts.TryGetValue(shooter.Id, out var value) || Time - value.SeenAt > 2f))
                {
                    soldier2.Contacts[shooter.Id] = new SilentOrders.Core.Contact
                    {
                        EnemyId = shooter.Id,
                        Position = Map.NearestOpen(shooter.Position + new SilentOrders.Core.V2(Random.Range(-2f, 2f), Random.Range(-2f, 2f))),
                        SeenAt = Time,
                        Confidence = 0.35f,
                        Visual = false
                    };
                }
            }
        }

        public float WeaponRange(SilentOrders.Core.Soldier s)
        {
            return WeaponBalance.For(s).Range;
        }

        public float SmokeTransmission(SilentOrders.Core.V2 a, SilentOrders.Core.V2 z)
        {
            return SmokeTransmissionAt(a, z, Time);
        }

        public float SmokeTransmissionAt(SilentOrders.Core.V2 a, SilentOrders.Core.V2 z, float at)
        {
            float depth = 0;
            foreach (var c in Smoke)
                depth += SmokeTactics.Depth(a, z, c.Position, c.Born, c.Until, c.Radius, at);
            return System.Math.Max(.035f, (float)System.Math.Exp(-depth));
        }

        // Legacy estimate for external diagnostics. Actual bullets use AimQuality
        // and geometric collisions; this value is never rolled to award damage.
        public float Accuracy(SilentOrders.Core.Soldier shooter, SilentOrders.Core.Soldier target)
        {
            return SilentOrders.Core.Ballistics.Accuracy(this, shooter, target.Position, target.Moving, target.Crouched, ShoulderPair.Protection(this, target, shooter.Position), CombatCrouch.Protected(target), CombatTraining.Corners(this, target));
        }

        public void RecordStress(SilentOrders.Core.Soldier shooter, SilentOrders.Core.Soldier target, float suppression, float morale)
        {
            Outcomes.Pressure(this, shooter, target, suppression, morale);
            if (shooter != null && shooter.Side != target.Side)
            {
                shooter.MoraleDamage += System.Math.Min(target.Morale, System.Math.Max(0f, morale)) * 100f;
                shooter.SuppressionDealt += System.Math.Min(1f - target.Suppression, System.Math.Max(0f, suppression)) * 100f;
            }
        }

        public void Damage(SilentOrders.Core.Soldier target, SilentOrders.Core.Soldier shooter, float amount, float suppression, float morale)
        {
            if (!target.Alive)
            {
                return;
            }

            float healthBeforeDamage = target.Health;
            amount *= BodyArmor.DamageMultiplier(target.ArmorLevel);
            if (shooter != null && shooter.Side != target.Side)
            {
                shooter.DamageDealt += System.Math.Min(target.Health, System.Math.Max(0f, amount));
            }

            if (shooter != null && shooter.Side == target.Side)
                amount *= .5f;
            History.Event(this, "damage", shooter == null ? -1 : shooter.Id, target.Id, shooter == null ? target.Position : shooter.Position, target.Position, amount);
            RecordStress(shooter, target, suppression, morale);
            target.Health = System.Math.Max(0f, target.Health - amount);
            LossMemory.Record(this, target, amount);
            target.LastHurt = (target.LastIncoming = Time);
            target.Suppression = SilentOrders.Core.Util.Clamp(target.Suppression + suppression*target.RaidSuppressionFactor, 0f, 1f);
            target.Morale = System.Math.Max(0f, target.Morale - morale);
            if (!target.Alive)
            {
                Down(target, shooter);
                return;
            }

            if (target.Health <= 20f)
            {
                target.Bleeding = true;
                target.Stabilized = false;
                target.BleedSourceId = shooter == null ? -1 : shooter.Id;
            }

            if (target.Health <= 20f && !target.Incapacitated)
            {
                target.Incapacitated = true;
                target.SupportReady = false;
                SilentOrders.Core.TacticalAI.Stop(target);
                Stats.CriticalWounds++;
                SilentOrders.Core.Dialogue.Say(this, target, 32, urgent: true);
            }
            else
            {
                SilentOrders.Core.Dialogue.Say(this, target, 12, urgent: true);
            }

            Outcomes.Wound(this, target, shooter, healthBeforeDamage, healthBeforeDamage - target.Health);
        }

        private void Down(SilentOrders.Core.Soldier target, SilentOrders.Core.Soldier shooter, string cause = "direct_hit")
        {
            Outcomes.Death(this, target, shooter, cause);
            CorpseEquipment.Drop(this, target);
            if (target.SupplyDonorId >= 0)
            {
                SilentOrders.Core.SupplyAI.Cancel(this, target);
            }

            foreach (SilentOrders.Core.Soldier soldier in Soldiers)
            {
                if (soldier.SupplyDonorId == target.Id || soldier.SupplyReceiverId == target.Id)
                {
                    SilentOrders.Core.SupplyAI.Cancel(this, soldier);
                }
            }

            if (target.RescueTargetId >= 0)
            {
                SilentOrders.Core.SupportAI.Release(this, target, complete: false);
            }

            if (target.RescuerId >= 0)
            {
                SilentOrders.Core.SupportAI.Release(this, Soldiers[target.RescuerId], complete: false);
            }

            if (shooter != null)
            {
                shooter.Kills++;
            }

            target.State = SilentOrders.Core.ActionState.Down;
            target.Moving = false;
            target.Speech = "";
            TacticalAI.Stop(target); // Reset cursor as well as the route after a lethal hit.
            Log(target.Side, target.Name + " выбыл из боя.");
            foreach (SilentOrders.Core.Soldier soldier2 in Soldiers)
            {
                if (soldier2.Alive && soldier2.Side == target.Side && SilentOrders.Core.V2.Distance(soldier2.Position, target.Position) < 9f)
                {
                    RecordStress(shooter, soldier2, 0f, (target.Id == Team(target).LeaderId) ? 0.28f : 0.2f);
                    soldier2.Morale = System.Math.Max(0f, soldier2.Morale - ((target.Id == Team(target).LeaderId) ? 0.28f : 0.2f));
                    SilentOrders.Core.Dialogue.Say(this, soldier2, 13, urgent: true);
                }
            }
        }

        private void UpdateObjective(float dt)
        {
            if (MissionCompletion.Update(this))
                return;
            int num = 0;
            int num2 = 0;
            foreach (SilentOrders.Core.Soldier soldier in Soldiers)
            {
                if (soldier.Alive && !soldier.Incapacitated && soldier.RescuerId < 0 && !soldier.Busy && !soldier.Broken && SilentOrders.Core.V2.Distance(soldier.Position, Map.Objective) <= 2.1f)
                {
                    if (soldier.Side == SilentOrders.Core.Side.Green)
                    {
                        num++;
                    }
                    else
                    {
                        num2++;
                    }
                }
            }

            Contested = num > 0 && num2 > 0;
            if (num > 0 && num2 == 0)
            {
                Capture = System.Math.Min(1f, Capture + dt / Config.CaptureSeconds);
            }
            else if (num == 0)
            {
                Capture = System.Math.Max(0f, Capture - dt / (Config.CaptureSeconds * 2f));
            }

            if (Alive(SilentOrders.Core.Side.Green) == 0)
            {
                Outcome = SilentOrders.Core.Outcome.GreenEliminated;
            }
            else if (!RaidMode && Capture >= 1f)
            {
                Outcome = SilentOrders.Core.Outcome.GreenCaptured;
            }
            else if (Time >= Config.TimeLimit && EnemyClearedAt < 0f)
            {
                Outcome = SilentOrders.Core.Outcome.RedHeld;
            }

            if (Outcome != 0)
            {
                Log((Outcome != SilentOrders.Core.Outcome.GreenCaptured) ? SilentOrders.Core.Side.Red : SilentOrders.Core.Side.Green, (Outcome == SilentOrders.Core.Outcome.GreenCaptured) ? "Точка «Рубеж» захвачена." : ((Outcome == SilentOrders.Core.Outcome.GreenEliminated) ? "Атакующее отделение выбыло." : "Время истекло. Оборона удержала рубеж."));
            }
        }
    }
}
