using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class BattleSimulation
    {
        public MissionMortarSupport StoryMortars;
        public int EnemyWeaponMission;
        public bool EnemyWeaponEndless;
        public readonly System.Collections.Generic.List<BlastCrater> Craters =
            new System.Collections.Generic.List<BlastCrater>();
        public bool OpeningAllowOpenGround = true;
        public bool ReconCombatTransitionEnabled = true;
        public bool OpeningFixedAccuracyEnabled = true;
        public bool OpeningIgnoreCoverEnabled = false,
            OpeningDoubleAccuracyEnabled = false;
        public bool OpeningHorizonEnabled = true,
            OpeningCriticalEnabled = true;
        public bool ReconOpeningEnabled = true;
        public bool DefenderRestEnabled = true,
            QuietReconEnabled = true,
            UsefulReconEnabled = true;
        public float ReconBudget = 14,
            ReconStandOff = 10;

        // Archived showcase battles use the pre-cover-denial rules of AI-0.12.25.
        public bool Historical01225;
        public readonly BattleVisibility Fog = new BattleVisibility();
        public readonly BattleAudioEvents AudioEvents = new BattleAudioEvents();
        public readonly LossMemoryState LossLearning = new LossMemoryState();
        internal readonly RouteSnapshot RouteView = new RouteSnapshot();
        internal readonly System.Collections.Generic.Dictionary<(Side, DefenderGroupState), int> MovementCounts =
            new System.Collections.Generic.Dictionary<(Side, DefenderGroupState), int>();
        public readonly AiDiagnostics Diagnostics = new AiDiagnostics();
        public readonly BattleHistory History = new BattleHistory();
        public readonly CriticalOutcomeReview Outcomes = new CriticalOutcomeReview();
        public readonly BallisticState Ballistics = new BallisticState();
        public readonly System.Collections.Generic.List<SmokeEffect> SmokeEffects =
            new System.Collections.Generic.List<SmokeEffect>();
        public readonly TacticalMap Tactics = new TacticalMap();
        public readonly Tuning Config;
        public readonly BattleMap Map;
        public readonly Rng Random;
        public readonly Rng VoiceRandom;
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
                throw new InvalidOperationException("Combat already started");
            CombatSeed = seed;
            Random.Reseed(seed);
            VoiceRandom.Reseed(seed ^ 0x1648D);
            foreach (var soldier in Soldiers)
                soldier.NextShot = Random.Range(.2f, 1f);
        }

        public readonly System.Collections.Generic.List<Soldier> Soldiers =
            new System.Collections.Generic.List<Soldier>();
        public readonly System.Collections.Generic.List<BloodTrace> BloodTraces =
            new System.Collections.Generic.List<BloodTrace>();

        // Pure visual particles; never consulted by movement, cover, collision, vision or targeting.
        public int CoverDebrisRevision;
        public readonly System.Collections.Generic.Dictionary<int, CoverDebrisPiece> CoverRemnants =
            new System.Collections.Generic.Dictionary<int, CoverDebrisPiece>();
        public readonly System.Collections.Generic.List<CoverDebrisPiece> CoverDebris =
            new System.Collections.Generic.List<CoverDebrisPiece>();
        public readonly System.Collections.Generic.List<CoverDebrisPiece> ShellCasings =
            new System.Collections.Generic.List<CoverDebrisPiece>();
        public readonly Squad Green = new Squad { Side = Side.Green, LeaderId = 0 };
        private readonly Squad defaultRed = new Squad { Side = Side.Red, LeaderId = 5 };
        public readonly System.Collections.Generic.List<DefenderGroupState> DefenderGroups =
            new System.Collections.Generic.List<DefenderGroupState>();
        public readonly CounterSniperIntel SniperIntel = new CounterSniperIntel();
        public bool CustomDefenders;
        public VehicleArrivalState GreenVehicle,
            RedVehicle;
        internal DefenderGroupState ActiveDefenderGroup;
        public Squad Red
        {
            get { return ActiveDefenderGroup == null ? defaultRed : ActiveDefenderGroup.Command; }
        }

        public Squad Team(Soldier soldier)
        {
            return soldier.DefenderGroup == null ? Team(soldier.Side) : soldier.DefenderGroup.Command;
        }

        public readonly System.Collections.Generic.List<BattleEvent> Events =
            new System.Collections.Generic.List<BattleEvent>();
        public readonly System.Collections.Generic.List<ShotTrace> Traces =
            new System.Collections.Generic.List<ShotTrace>();
        public readonly System.Collections.Generic.List<SmokeCloud> Smoke =
            new System.Collections.Generic.List<SmokeCloud>();
        public readonly System.Collections.Generic.List<ArcProjectile> Projectiles =
            new System.Collections.Generic.List<ArcProjectile>();
        public SceneEquipmentState SceneEquipment;
        public readonly System.Collections.Generic.List<GroundWeapon> GroundWeapons =
            new System.Collections.Generic.List<GroundWeapon>();
        public readonly System.Collections.Generic.List<GroundTourniquet> GroundTourniquets =
            new System.Collections.Generic.List<GroundTourniquet>();
        public float NextTourniquetPlan;
        public readonly System.Collections.Generic.List<GroundAmmo> GroundAmmo =
            new System.Collections.Generic.List<GroundAmmo>();
        public readonly System.Collections.Generic.List<GroundLauncher> GroundLaunchers =
            new System.Collections.Generic.List<GroundLauncher>();
        public readonly System.Collections.Generic.List<BlastEffect> Blasts =
            new System.Collections.Generic.List<BlastEffect>();
        public readonly Statistics Stats = new Statistics();
        public readonly System.Collections.Generic.List<RadioReport> Radio =
            new System.Collections.Generic.List<RadioReport>();
        public int CampaignLevel;
        public int CampaignReserve;
        private readonly RedCounterattackState defaultCounterattack = new RedCounterattackState();
        public RedCounterattackState Counterattack
        {
            get { return ActiveDefenderGroup == null ? defaultCounterattack : ActiveDefenderGroup.Counterattack; }
        }

        public float EnemyClearedAt = -1f;
        public bool VictoryByElimination;
        public bool RaidMode;
        public int CampaignTotal;
        public float Time;
        public float Capture;
        public bool Contested;
        public int TickNumber;
        public Outcome Outcome;
        public readonly GameplayRules Rules;

        public BattleSimulation(int seed = 410, Tuning tuning = null, GameplayRules rules = null)
        {
            Rules = rules == null ? GameplayRules.CaptureDefaults() : rules.Copy();
            Seed = seed;
            CombatSeed = seed;
            Config = tuning ?? new Tuning();
            Map = new BattleMap();
            Random = new Rng(seed);
            VoiceRandom = new Rng(seed ^ 0x1648D);
            string[] callsigns = new string[10]
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
                "ЯСТРЕБ",
            };
            V2[] spawnPositions = new V2[10]
            {
                new V2(6.5f, 17.5f),
                new V2(6.5f, 15.5f),
                new V2(6.5f, 20.5f),
                new V2(5.5f, 15.5f),
                new V2(5.5f, 20.5f),
                new V2(50.5f, 18.5f),
                new V2(44.5f, 15.5f),
                new V2(44.5f, 20.5f),
                new V2(50.5f, 15.5f),
                new V2(50.5f, 21.5f),
            };
            for (int i = 0; i < 10; i++)
            {
                int roleSlot = i % 5;
                int teamOffset = ((i >= 5) ? 5 : 0);
                Soldier soldier = new Soldier(Rules)
                {
                    Id = i,
                    Name = callsigns[i],
                    KedrBinoculars = i == 0,
                    Side = ((i >= 5) ? Side.Red : Side.Green),
                    Role = (Role)roleSlot,
                    Element = ((roleSlot == 0) ? (-1) : ((roleSlot <= 2) ? 1 : 0)),
                    BuddyId =
                        teamOffset + ((roleSlot == 0) ? 1 : ((roleSlot % 2 == 1) ? (roleSlot + 1) : (roleSlot - 1))),
                    Position = Map.NearestOpen(spawnPositions[i]),
                    NextSense = (float)i * 0.023f,
                    NextDecision = (float)i * 0.055f,
                    Facing = new V2((i < 5) ? 1 : (-1), 0f),
                    NextShot = Random.Range(0.2f, 1f),
                };
                soldier.SupplyPack = roleSlot == 4;
                soldier.PackRockets = ((roleSlot == 4) ? 2 : 0);
                soldier.PackBoxes = ((roleSlot == 4) ? 1 : 0);
                soldier.PackMagazines = ((roleSlot == 4) ? 10 : 0);
                soldier.Tourniquets = ((roleSlot != 2) ? 1 : 5);
                soldier.Rockets = ((roleSlot == 3) ? 1 : 0);
                soldier.SmokeCharges = ((roleSlot == 0) ? 3 : 0);
                if (roleSlot == 1)
                {
                    soldier.WeaponVariant = 1;
                    soldier.Ammo = 100;
                    soldier.Magazines.Reset(soldier, 300);
                }

                soldier.Destination = (soldier.OrderAnchor = soldier.Position);
                Soldiers.Add(soldier);
            }

            foreach (var enemy in Soldiers)
                if (enemy.Side == Side.Red)
                    EnemyInventory.Configure(enemy, Soldiers.FindAll(x => x.Side == Side.Red));
            Green.Character = (CommanderCharacter)(Math.Abs((long)seed) % 3);
            Red.Character = (CommanderCharacter)((Math.Abs((long)seed) + 1) % 3);
            Log(Side.Green, "Отделение КЕДР: занять точку «Рубеж». Беречь людей.");
            Log(Side.Red, "Отделение ГРАНИТ: удерживать подходы к точке.");
        }

        public Squad Team(Side side)
        {
            return (side == Side.Green) ? Green : Red;
        }

        public int Alive(Side side)
        {
            int livingCount = 0;
            foreach (Soldier soldier in Soldiers)
            {
                if (soldier.Side == side && soldier.Alive)
                {
                    livingCount++;
                }
            }

            return livingCount;
        }

        public void Log(Side side, string message)
        {
            string b = message.Trim().TrimEnd('.');
            int eventIndex = Events.Count - 1;
            while (eventIndex >= 0 && Time - Events[eventIndex].Time < 4f)
            {
                if (Events[eventIndex].Side == side && Events[eventIndex].Text.Trim().TrimEnd('.') == b)
                {
                    return;
                }

                eventIndex--;
            }

            History.Event(this, "journal", -1, -1, new V2(), new V2(), 0, side + ": " + message);
            Events.Add(
                new BattleEvent
                {
                    Time = Time,
                    Side = side,
                    Text = message,
                }
            );
            if (Events.Count > 100)
            {
                Events.RemoveAt(0);
            }
        }

        public void Say(Soldier s, string phrase, bool urgent = false)
        {
            // Preserve the unique panic quote during its brief on-screen lifetime.
            bool grenadeWarning = urgent && (phrase == "СВОЯ!" || phrase == "ВЫСТРЕЛ!!");
            bool wallCommand =
                urgent
                && (
                    phrase.StartsWith("Код СТЕНА!", StringComparison.Ordinal)
                    || phrase == "КОНТАКТ!!"
                    || phrase == "Готовность. Движение"
                );
            if (!grenadeWarning && (s.RetreatQuoteUntil > Time || s.CasualtyRecovery.QuoteUntil > Time))
                return;
            if (
                s.Alive
                && (
                    grenadeWarning
                    || wallCommand
                    || (!(Time - s.SpeechAt < 1.5f) || s.LastSpoken.Length <= 0)
                        && (urgent || !(Time < s.SpeechCooldown))
                )
            )
            {
                phrase = Dialogue.Adapt(this, s, phrase);
                if (grenadeWarning || wallCommand || !(phrase == s.LastSpoken) || !(Time - s.LastSpokenAt < 10f))
                {
                    s.Speech = phrase;
                    s.SpeechShort = Dialogue.Short(phrase);
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

        public bool ContactManeuversEnabled = true,
            SuppressionVisionEnabled = true;
        public bool PeripheralFocusEnabled = true,
            SurpriseBurstEnabled = true,
            StealthChoiceEnabled = true;
        public int PairExperimentProfile;
        public bool PairExperimentMarch;
        public bool PairExperimentCover;
        public PairPlanSettings PairPlan;
        public bool TacticalMarchEnabled = true,
            ReactionTimingEnabled = true;
        bool deploymentPrepared;

        public void PrepareDeployment()
        {
            if (deploymentPrepared)
                return;
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
                for (int reportIndex = Radio.Count - 1; reportIndex >= 0; reportIndex--)
                {
                    RadioReport radioReport = Radio[reportIndex];
                    if (!(radioReport.DeliverAt > Time))
                    {
                        if (Soldiers[radioReport.Sender].Alive)
                        {
                            Merge(Team(radioReport.Side).Contacts, radioReport.Contact);
                            foreach (Soldier recipient in Soldiers)
                            {
                                if (recipient.Alive && recipient.Side == radioReport.Side)
                                {
                                    Merge(recipient.Contacts, radioReport.Contact);
                                }
                            }

                            Stats.Reports++;
                        }

                        Radio.RemoveAt(reportIndex);
                    }
                }

                foreach (Soldier fighter in Soldiers)
                {
                    if (!fighter.Alive || !fighter.Deployed)
                    {
                        continue;
                    }

                    if (fighter.Health <= 20f && !fighter.Incapacitated)
                    {
                        fighter.Incapacitated = true;
                        Outcomes.BleedingWound(
                            this,
                            fighter,
                            (fighter.BleedSourceId >= 0 && fighter.BleedSourceId < Soldiers.Count)
                                ? Soldiers[fighter.BleedSourceId]
                                : null
                        );
                        TacticalAI.Stop(fighter);
                        fighter.SupportReady = false;
                        Stats.CriticalWounds++;
                        Dialogue.Say(this, fighter, 32, urgent: true);
                    }

                    if (fighter.Health <= 20f && !fighter.Stabilized)
                    {
                        fighter.Bleeding = true;
                    }

                    if (fighter.Bleeding)
                    {
                        if (fighter.BleedSourceId >= 0 && Soldiers[fighter.BleedSourceId].Side != fighter.Side)
                        {
                            Soldiers[fighter.BleedSourceId].DamageDealt += Math.Min(fighter.Health, tick);
                        }

                        fighter.Health = Math.Max(0f, fighter.Health - tick);
                        if (!fighter.Alive)
                        {
                            fighter.Bleeding = false;
                            Down(
                                fighter,
                                (fighter.BleedSourceId >= 0) ? Soldiers[fighter.BleedSourceId] : null,
                                "bleeding"
                            );
                            Log(fighter.Side, fighter.Name + ": погиб от кровопотери.");
                            continue;
                        }
                    }

                    if (fighter.Incapacitated || fighter.Broken || fighter.Suppression > 0.8f)
                    {
                        if (fighter.ImmobileSince < 0f)
                        {
                            fighter.ImmobileSince = Time;
                        }
                    }
                    else
                    {
                        fighter.ImmobileSince = -1f;
                    }

                    bool isProtected = PositionLogic.Protected(this, fighter, fighter.Position);
                    bool recentlyUnderFire = Time - fighter.LastIncoming < 2f;
                    fighter.Suppression = Math.Max(0f, fighter.Suppression - tick * (isProtected ? 0.16f : 0.08f));
                    float allyMoraleSupport = 0f;
                    foreach (Soldier nearbyAlly in Soldiers)
                    {
                        if (
                            nearbyAlly.Id != fighter.Id
                            && nearbyAlly.Side == fighter.Side
                            && nearbyAlly.Available
                            && !nearbyAlly.Broken
                            && V2.Distance(fighter.Position, nearbyAlly.Position) < 6f
                        )
                        {
                            allyMoraleSupport += ((nearbyAlly.Id == Team(fighter).LeaderId) ? 0.025f : 0.008f);
                        }
                    }

                    float isolationPenalty = ((allyMoraleSupport == 0f && recentlyUnderFire) ? 0.006f : 0f);
                    float moraleRate = (
                        recentlyUnderFire
                            ? (-0.024f * fighter.Suppression - isolationPenalty)
                            : (0.012f + allyMoraleSupport)
                    );
                    if (Time - fighter.LastBlast < 4f && recentlyUnderFire)
                    {
                        moraleRate -= 0.006f;
                    }

                    if (moraleRate > 0f)
                    {
                        moraleRate = MoraleDynamics.RecoveryRate(fighter, moraleRate);
                    }

                    // Immediately after psychological collapse no positive regeneration is possible.
                    // Incoming fire can still reduce morale; the pause is not a permanent lock.
                    if (
                        fighter.Broken
                        && moraleRate > 0f
                        && (Time < fighter.MoraleRecoveryBlockedUntil || Time - fighter.LastIncoming <= 3f)
                    )
                        moraleRate = 0f;
                    if (recentlyUnderFire && moraleRate < 0f && fighter.Morale < .35f)
                        moraleRate *= 1.18f; // low morale amplifies sustained-fire stress slightly
                    fighter.Morale = Util.Clamp(fighter.Morale + tick * moraleRate, 0f, fighter.MoraleMaximum);
                    if (isProtected)
                    {
                        fighter.CoverTime += tick;
                        Stats.CoverSeconds += tick;
                    }
                    else
                    {
                        fighter.ExposedTime += tick;
                        Stats.ExposedSeconds += tick;
                    }

                    if (fighter.Morale <= .001f && recentlyUnderFire && !fighter.Broken)
                        MoraleDynamics.EnterBroken(this, fighter);
                    CombatCohesion.AwardRelief(this, fighter);
                    if (!fighter.Incapacitated && fighter.RescuerId < 0 && Time >= fighter.NextSense)
                    {
                        using (new CpuSample("Rubezh.AI.sense"))
                            Perception.Observe(this, fighter);
                        fighter.NextSense = Time + Config.PerceptionPeriod;
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
                CampaignScenario.Update(this);
                RedCounterattack.Tick(this);
                SmokePlanner.Update(this, Green);
                if (!CustomDefenders)
                    SmokePlanner.Update(this, Red);
                if (Time >= Green.NextPlan && !VehicleArrivals.Disembarking(this, Side.Green))
                {
                    SupportAI.Plan(this, Green);
                    SupplyAI.Plan(this, Green);
                    TaskReview.Group(this, Green);
                    using (new CpuSample("Rubezh.AI.squad"))
                        SquadBrain.Plan(this, Green);
                    Green.NextPlan = Time + Config.SquadPeriod;
                }

                if (!CustomDefenders && Time >= Red.NextPlan)
                {
                    SupportAI.Plan(this, Red);
                    SupplyAI.Plan(this, Red);
                    TaskReview.Group(this, Red);
                    using (new CpuSample("Rubezh.AI.squad"))
                        SquadBrain.Plan(this, Red);
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
                    Soldier soldier = Soldiers[(i + TickNumber) % Soldiers.Count];
                    if (soldier.Alive && soldier.Deployed)
                    {
                        if (
                            soldier.Available
                            && !BuddyActions.Active(soldier)
                            && !SupplyRecovery.Active(soldier)
                            && !HandLauncher.Active(soldier)
                            && !PairExchange.Active(this, soldier)
                            && !CombatCrouch.Standing(this, soldier)
                            && !FriendlyGrenadeSafety.Active(this, soldier)
                            && !soldier.GrenadeEscape.Active
                            && !soldier.VehicleDisperse
                            && !soldier.VehicleRally
                            && Time >= soldier.NextDecision
                        )
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
                                        TacticalAI.Decide(this, soldier);
                                }
                            }
                            finally
                            {
                                requestedDecision = soldier.NextDecision;
                                // An exception must not strand the scheduler in a sentinel state.
                                if (float.IsNegativeInfinity(soldier.NextDecision))
                                    soldier.NextDecision = Time;
                            }

                            float scheduledDecision =
                                Time
                                + (Config.DecisionPeriod + Random.Range(0f, 0.12f))
                                    * ConcussionEffects.ReactionTimeMultiplier(
                                        ConcussionEffects.Current(this, soldier)
                                    );
                            soldier.NextDecision = float.IsNegativeInfinity(requestedDecision)
                                ? scheduledDecision
                                : Math.Min(requestedDecision, scheduledDecision);
                        }

                        MotionCombat.Begin(this, soldier);
                        CombatCrouch.Begin(this, soldier);
                        PairExchange.Review(this, soldier);
                        ShoulderPair.Update(this, soldier);
                        using (new CpuSample("Rubezh.AI.actor"))
                            UpdateSoldier(soldier, tick);
                        MotionCombat.End(this, soldier, tick);
                        CombatCrouch.End(this, soldier, tick);
                        if (
                            soldier.Awareness.Initialized
                            && soldier.Available
                            && !soldier.Busy
                            && soldier.HandsBusyUntil <= Time
                            && Time - soldier.LastFire > .25f
                        )
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
                ArcedOrdnance.Update(this);
                BlastCrater.Record(this);
                PairExchange.Sweep(this);
                SmokeTactics.Tick(this);
                CoverDebrisSystem.Consume(this);
                Traces.RemoveAll((ShotTrace t) => t.Until < Time);
                Smoke.RemoveAll((SmokeCloud t) => t.Until <= Time);
                Blasts.RemoveAll((BlastEffect t) => t.Until <= Time);
                foreach (var wounded in Soldiers)
                    CasualtyRecovery.UpdateVoice(this, wounded);
                Dialogue.Refresh(this);
                UpdateObjective(tick);
                History.Sample(this);
                Outcomes.Sample(this);
            }
        }

        private static void Merge(System.Collections.Generic.Dictionary<int, Contact> contacts, Contact incoming)
        {
            if (!contacts.TryGetValue(incoming.EnemyId, out var value) || value.SeenAt < incoming.SeenAt)
            {
                Contact contact = incoming.Copy();
                contact.Reported = true;
                contacts[incoming.EnemyId] = contact;
            }
        }

        private void UpdateSoldier(Soldier s, float dt)
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
            if (SupplyAI.Update(this, s, dt) || SupportAI.Update(this, s, dt))
            {
                return;
            }

            if (s.Broken)
            {
                s.State = ActionState.Cower;
                s.Crouched = true;
                s.SupportReady = false;
                if (PositionLogic.Protected(this, s, s.Position))
                {
                    TacticalAI.Stop(s);
                }
                else
                {
                    CrawlToCover(s, dt);
                }

                return;
            }

            if (s.ReloadUntil > Time)
            {
                s.State = ActionState.Reload;
                s.Crouched = true;
                return;
            }

            if (s.ReloadUntil > 0f)
            {
                MagazineSystem.Reload(this, s);
                s.ReloadUntil = 0f;
                Dialogue.Say(this, s, 7);
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
            bool forcedMovement =
                !waitingSmoke
                && (
                    ProgressRecovery.Move(this, s, dt)
                    || ShoulderPair.Move(this, s, dt)
                    || (
                        LastOpponentHunt.Owns(this, s)
                            ? LastOpponentHunt.Move(this, s, dt)
                            : (RedCounterattack.Move(this, s, dt) || TacticalOrders.UpdatePriorityMovement(this, s, dt))
                    )
                );
            if (forcedMovement && TacticalOrders.Emergency(Team(s)))
                return;
            AmmoLogistics.TryBuddy(this, s);
            if (s.HandsBusyUntil > Time && s.Motion.MobileSmokeUntil <= Time)
                return;
            // A nearby fallen weapon is a finite resource, not free ammunition.
            // The scavenger still travels using the ordinary movement/cover system.
            if (!WithdrawalTactics.Active(Team(s)) && Team(s).Style != CombatStyle.Passive)
                CorpseEquipment.TryUpdate(this, s);
            bool proactiveReload = CombatCoordination.WantsReload(this, s) && MagazineSystem.BetterMagazine(s);
            if ((s.Ammo <= 0 || proactiveReload) && s.Reserve > 0)
            {
                s.BuddyAction.ReloadAssisted = false;
                s.ReloadUntil = Time + WeaponBalance.ReloadSeconds(s);
                s.State = ActionState.Reload;
                Stats.Reloads++;
                if (proactiveReload)
                {
                    Stats.TacticalReloads++;
                    s.NextTacticalReload = Time + 18f;
                }

                Dialogue.Say(this, s, (s.GunMachineGunner) ? 31 : 5, urgent: true);
                Soldier soldier = Soldiers[s.BuddyId];
                if (soldier.Alive && soldier.Suppression < 0.7f)
                {
                    soldier.NextDecision = 0f;
                    Dialogue.Say(this, soldier, 6, urgent: true);
                }
            }
            else if (s.Suppression > 0.87f && !CombatCohesion.BypassCover(this, s))
            {
                s.State = ActionState.Pinned;
                s.Crouched = true;
            }
            else
            {
                if (CloseCombat.TryStrike(this, s))
                    return;
                if (ArcedOrdnance.TryUse(this, s) || SpecialWeapons.TryLaunch(this, s))
                {
                    return;
                }

                if (!forcedMovement && !MotionCombat.InMotion(this, s))
                    FiringPositionPolicy.Hold(this, s);
                SomaliFire.PreparePosture(this, s);
                Contact contact = TacticalAI.FireTarget(this, s);
                if (contact == null)
                {
                    s.FireStatus = (
                        (s.Ammo <= 0) ? "Нет БК" : ((s.ReloadUntil > Time) ? "Перезарядка" : "Сектор потерян")
                    );
                }

                bool holdingPosition = s.Job == Job.Cover || s.Job == Job.Hold || s.Job == Job.Command;
                bool canEngage =
                    (MotionCombat.CanFire(this, s) || SomaliFire.CanAttempt(this, s, contact))
                    && s.HandsBusyUntil <= Time
                    && contact != null
                    && s.Ammo > 0
                    && Time >= s.NextShot
                    && Time >= s.ConcussionReactUntil;
                bool hasRoute = !forcedMovement && s.PathCursor < s.Path.Count;
                if (
                    canEngage
                    && (
                        !hasRoute
                        || holdingPosition
                        || MotionCombat.InMotion(this, s)
                        || V2.Distance(s.Position, contact.Position) < 5f
                    )
                    && s.RecoverUntil <= Time
                )
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

                hasRoute = !forcedMovement && s.PathCursor < s.Path.Count; // Fire may hand movement to a short approach.
                bool pausedToFire = Time - s.LastFire < 0.24f && !MotionCombat.InMotion(this, s);
                bool waitingForCover =
                    hasRoute && WithdrawalTactics.Active(Team(s)) && !WithdrawalTactics.HasCover(this, s)
                    || hasRoute
                        && !PositionLogic.ShelterMoving(this, s)
                        && s.FireLaneCrossUntil <= Time
                        && !(s.Sniper && s.SniperMoving)
                        && (
                            PositionLogic.Protected(this, s, s.Position)
                            || (Time - s.LastIncoming > 5f && TacticalAI.Risk(this, s, s.Position) < 0.01f)
                        )
                        && V2.Distance(s.Position, s.Destination) > 0.7f
                        && !SquadBrain.MovementCovered(this, s)
                        && !CombatCohesion.BypassCover(this, s);
                if (
                    ReconOpening.Owns(this, s)
                    || QuietRecon.Owns(this, s)
                    || WallTravel.Active(this, s)
                    || PairTacticalPlan.StepCovered(this, s)
                )
                    waitingForCover = false;
                if (waitingForCover)
                {
                    s.State = ActionState.WaitCover;
                    s.WaitTime += dt;
                    s.Reason = "Перед опасным участком: жду подтверждения прикрытия";
                    Dialogue.Say(this, s, 4);
                }

                // Ordinary flanking and inter-wing transfers require tactical
                // cover clearance; the emergency escape and pinned-rescue bypasses
                // retain their dedicated higher priorities.
                if (
                    hasRoute
                    && !pausedToFire
                    && !waitingForCover
                    && !waitingSmoke
                    && (
                        !TacticalOrders.Flanks(Team(s))
                        || ObjectiveOpportunity.MovementAllowed(this, s)
                        || WallTravel.Active(this, s)
                        || PositionLogic.ShelterMoving(this, s)
                        || s.FireLaneCrossUntil > Time
                        || CombatCohesion.BypassCover(this, s)
                        || FlankTactics.CanAdvance(this, s)
                    )
                )
                {
                    Locomotion.AdvanceWaypoint(this, s);
                    V2 v = s.Path[s.PathCursor];
                    V2 stepDirection = v - s.Position;
                    float movementSpeed = CombatMovement.Speed(this, s);
                    if (
                        Locomotion.TryStep(
                            this,
                            s,
                            stepDirection.Normalized * Math.Min(stepDirection.Length, movementSpeed * dt),
                            out var next
                        )
                    )
                    {
                        s.DistanceTravelled += V2.Distance(s.Position, next);
                        s.Position = next;
                        s.CoverWaitStartedAt = -1f;
                        s.Facing = stepDirection.Normalized;
                        s.Moving = true;
                        s.LastMoved = Time;
                        if (V2.Distance(next, v) < 0.04f)
                        {
                            s.PathCursor++;
                        }
                    }
                    else if (
                        !CombatCrouch.Standing(this, s)
                        && Time - s.Ai.Started > 1.5f
                        && Time - s.LastMoved > 1.5f
                        && s.Motion.GroupWaitUntil <= Time
                    )
                    {
                        s.NextDecision = 0f;
                        s.Path.Clear();
                        s.PathCursor = 0;
                    }
                }

                s.Crouched = (!s.Moving && PositionLogic.Protected(this, s, s.Position)) || s.Suppression > 0.55f;
                if (!pausedToFire && !waitingForCover)
                {
                    s.State = (
                        (!s.Moving)
                            ? (
                                (s.RecoverUntil > Time)
                                    ? ActionState.Recover
                                    : (
                                        (s.MoveAuthorized || (s.Job != 0 && s.Job != Job.Flank))
                                            ? ActionState.Observe
                                            : ActionState.WaitCover
                                    )
                            )
                            : ActionState.Move
                    );
                }
            }
        }

        private void CrawlToCover(Soldier s, float dt)
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

            V2 v = s.Path[s.PathCursor] - s.Position;
            V2 crawlDestination =
                s.Position
                + v.Normalized
                    * Math.Min(
                        v.Length,
                        Config.MoveSpeed
                            * BodyArmor.Multiplier(s.ArmorLevel)
                            * ConcussionEffects.MovementMultiplier(ConcussionEffects.Current(this, s))
                            * 0.32f
                            * dt
                    );
            foreach (Soldier soldier in Soldiers)
            {
                if (soldier.Alive && soldier.Id != s.Id && V2.Distance(crawlDestination, soldier.Position) < 0.4f)
                {
                    return;
                }
            }

            if (
                MedicTactics.AllowStep(this, s, crawlDestination)
                && !PairExchange.Reserved(this, s, crawlDestination)
                && Map.Walkable(crawlDestination)
                && Locomotion.WalkableSegment(this, s.Position, crawlDestination)
                && FireLaneSafety.AllowStep(this, s, crawlDestination - s.Position)
            )
            {
                s.DistanceTravelled += V2.Distance(s.Position, crawlDestination);
                s.Facing = v.Normalized;
                s.Position = crawlDestination;
                s.Moving = true;
                s.LastMoved = Time;
                if (v.Length < 0.06f)
                {
                    s.PathCursor++;
                }
            }
        }

        public bool FriendlyInLane(Soldier shooter, V2 target)
        {
            if (shooter.FireHoldUntil > Time)
                return true;
            foreach (Soldier soldier in Soldiers)
            {
                if (
                    soldier.Alive
                    && soldier.Deployed
                    && soldier.Side == shooter.Side
                    && soldier.Id != shooter.Id
                    && !ShoulderPair.CanShootOver(this, shooter, soldier, target)
                    && V2.SegmentDistance(soldier.Position, shooter.Position, target) < 0.45f
                    && V2.Distance(soldier.Position, shooter.Position) > 0.2f
                )
                {
                    return true;
                }
            }

            return false;
        }

        private void Fire(Soldier shooter, Contact contact)
        {
            if (shooter.SomaliNext > Time)
                return;
            if (
                ReconOpening.HoldFire(this, shooter)
                || QuietRecon.HoldFire(this, shooter)
                || (!ReconOpening.ActiveFire(this, shooter) && PairTacticalPlan.HoldFire(this, shooter, contact))
            )
                return;
            bool somaliShot = SomaliFire.CanAttempt(this, shooter, contact);
            if (!somaliShot && !CombatCrouch.FireAllowed(this, shooter, contact.Position))
                return;
            if (!shooter.Deployed || !MotionCombat.CanFire(this, shooter) || shooter.HandsBusyUntil > Time)
                return;
            if (
                V2.Distance(shooter.Position, contact.Position) > WeaponRange(shooter)
                && !EngagementPolicy.EmergencyAllowed(this, shooter, contact)
            )
                return;
            bool sightReady = DirectionalVision.ReadyToFire(this, shooter, contact);
            bool reactionReady = ReactionTiming.WeaponReady(this, shooter, contact);
            if (!reactionReady || !sightReady)
                return;
            if (
                !somaliShot
                && !MotionCombat.InMotion(this, shooter)
                && !ReconOpening.ActiveFire(this, shooter)
                && EngagementPolicy.TryApproach(this, shooter, contact)
            )
                return;
            if (
                !MotionCombat.SafeFire(this, shooter, contact.Position)
                || FriendlyInLane(shooter, contact.Position)
                || FireLaneSafety.HoldFire(this, shooter, contact.Position)
            )
            {
                Stats.FriendlyFireHolds++;
                return;
            }

            bool targetVisible = shooter.Visible.Contains(contact.EnemyId);
            V2 position = contact.Position;
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
            if (
                shooter.Motion.FrontId >= 0
                && shooter.Motion.FrontId < Soldiers.Count
                && ShoulderPair.CanShootOver(this, shooter, Soldiers[shooter.Motion.FrontId], contact.Position)
            )
                shooter.Motion.ShoulderShots++;
            Stats.Shots++;
            AudioEvents.Emit(
                Time,
                shooter.GunSniper ? 2
                    : shooter.GunMachineGunner ? 1
                    : 0,
                shooter.Id
            );
            shooter.LastFire = Time;
            FireLaneSafety.Fired(this, shooter, contact.Position);
            shooter.NextShot = Time + WeaponBalance.For(shooter).FireSeconds + shooter.Suppression * 0.6f;
            if (
                !shooter.GunSniper
                && (
                    shooter.WeaponModel == 1
                    || AreaSuppression.Active(this, shooter)
                    || LastOpponentHunt.IsCover(this, shooter)
                )
            )
            {
                if (shooter.BurstRounds <= 0)
                {
                    shooter.BurstRounds = AreaSuppression.BurstSize(this, shooter);
                }

                shooter.BurstRounds--;
                shooter.NextShot =
                    Time
                    + (
                        (shooter.BurstRounds > 0)
                            ? WeaponBalance.For(shooter).FireSeconds
                            : WeaponBalance.For(shooter).FireSeconds * 9f
                    )
                    + shooter.Suppression * 0.2f;
            }

            if (shooter.WeaponModel >= 4)
            {
                shooter.NextShot = Time + WeaponBalance.For(shooter).FireSeconds + shooter.Suppression * 0.6f;
            }

            Squad squad = Team(shooter);
            if (
                squad.EnvelopmentActive
                && squad.SupportIds.Contains(shooter.Id)
                && !shooter.GunSniper
                && shooter.Role != Role.Medic
            )
            {
                shooter.NextShot = Time + (shooter.NextShot - Time) * 0.78f;
            }

            if (squad.Style == CombatStyle.Passive)
            {
                shooter.NextShot += 0.4f;
            }

            if (shooter.SuppressiveFire)
            {
                Stats.SuppressionShots++;
                shooter.NextShot =
                    Time + (shooter.NextShot - Time) * ((squad.Style == CombatStyle.Aggressive) ? 0.7f : 0.85f);
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
                shooter.FireStatus =
                    V2.Distance(shooter.Position, contact.Position) > WeaponRange(shooter)
                        ? "Экстренный огонь: точность ×0.5"
                        : (shooter.SuppressiveFire ? "Подавляет позицию" : "Прицельный огонь");
            }

            shooter.Facing = (position - shooter.Position).Normalized;
            shooter.State = (
                (targetVisible && !shooter.SuppressiveFire && shooter.Role != Role.MachineGunner)
                    ? ActionState.Fire
                    : ActionState.Suppress
            );
            var launched = SilentOrders.Core.Ballistics.Launch(this, shooter, contact, muzzle);
            CoverDebrisSystem.EjectCasing(this, shooter, muzzle, contact.Position);
            SomaliFire.Finish(this, shooter);
            foreach (Soldier listener in Soldiers)
            {
                if (
                    !shooter.GunSniper
                    && listener.Alive
                    && listener.Side != shooter.Side
                    && V2.Distance(listener.Position, shooter.Position) < 17f
                    && !listener.Visible.Contains(shooter.Id)
                    && (!listener.Contacts.TryGetValue(shooter.Id, out var value) || Time - value.SeenAt > 2f)
                )
                {
                    listener.Contacts[shooter.Id] = new Contact
                    {
                        EnemyId = shooter.Id,
                        Position = Map.NearestOpen(
                            shooter.Position + new V2(Random.Range(-2f, 2f), Random.Range(-2f, 2f))
                        ),
                        SeenAt = Time,
                        Confidence = 0.35f,
                        Visual = false,
                    };
                }
            }
        }

        public float WeaponRange(Soldier s)
        {
            return WeaponBalance.For(s).Range;
        }

        public float SmokeTransmission(V2 a, V2 z)
        {
            return SmokeTransmissionAt(a, z, Time);
        }

        public float SmokeTransmissionAt(V2 a, V2 z, float at)
        {
            float depth = 0;
            foreach (var c in Smoke)
                depth += SmokeTactics.Depth(a, z, c.Position, c.Born, c.Until, c.Radius, at);
            return Math.Max(.035f, (float)Math.Exp(-depth));
        }

        // Legacy estimate for external diagnostics. Actual bullets use AimQuality
        // and geometric collisions; this value is never rolled to award damage.
        public float Accuracy(Soldier shooter, Soldier target)
        {
            return SilentOrders.Core.Ballistics.Accuracy(
                this,
                shooter,
                target.Position,
                target.Moving,
                target.Crouched,
                ShoulderPair.Protection(this, target, shooter.Position),
                CombatCrouch.Protected(target),
                CombatTraining.Corners(this, target)
            );
        }

        public void RecordStress(Soldier shooter, Soldier target, float suppression, float morale)
        {
            Outcomes.Pressure(this, shooter, target, suppression, morale);
            if (shooter != null && shooter.Side != target.Side)
            {
                shooter.MoraleDamage += Math.Min(target.Morale, Math.Max(0f, morale)) * 100f;
                shooter.SuppressionDealt += Math.Min(1f - target.Suppression, Math.Max(0f, suppression)) * 100f;
            }
        }

        public void Damage(Soldier target, Soldier shooter, float amount, float suppression, float morale)
        {
            if (!target.Alive)
            {
                return;
            }

            float healthBeforeDamage = target.Health;
            amount *= BodyArmor.DamageMultiplier(target.ArmorLevel);
            if (shooter != null && shooter.Side != target.Side)
            {
                shooter.DamageDealt += Math.Min(target.Health, Math.Max(0f, amount));
            }

            if (shooter != null && shooter.Side == target.Side)
                amount *= .5f;
            History.Event(
                this,
                "damage",
                shooter == null ? -1 : shooter.Id,
                target.Id,
                shooter == null ? target.Position : shooter.Position,
                target.Position,
                amount
            );
            RecordStress(shooter, target, suppression, morale);
            target.Health = Math.Max(0f, target.Health - amount);
            LossMemory.Record(this, target, amount);
            target.LastHurt = (target.LastIncoming = Time);
            target.Suppression = Util.Clamp(target.Suppression + suppression * target.RaidSuppressionFactor, 0f, 1f);
            target.Morale = Math.Max(0f, target.Morale - morale);
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
                TacticalAI.Stop(target);
                Stats.CriticalWounds++;
                Dialogue.Say(this, target, 32, urgent: true);
            }
            else
            {
                Dialogue.Say(this, target, 12, urgent: true);
            }

            Outcomes.Wound(this, target, shooter, healthBeforeDamage, healthBeforeDamage - target.Health);
        }

        private void Down(Soldier target, Soldier shooter, string cause = "direct_hit")
        {
            Outcomes.Death(this, target, shooter, cause);
            CorpseEquipment.Drop(this, target);
            if (target.SupplyDonorId >= 0)
            {
                SupplyAI.Cancel(this, target);
            }

            foreach (Soldier soldier in Soldiers)
            {
                if (soldier.SupplyDonorId == target.Id || soldier.SupplyReceiverId == target.Id)
                {
                    SupplyAI.Cancel(this, soldier);
                }
            }

            if (target.RescueTargetId >= 0)
            {
                SupportAI.Release(this, target, complete: false);
            }

            if (target.RescuerId >= 0)
            {
                SupportAI.Release(this, Soldiers[target.RescuerId], complete: false);
            }

            if (shooter != null)
            {
                shooter.Kills++;
            }

            target.State = ActionState.Down;
            target.Moving = false;
            target.Speech = "";
            TacticalAI.Stop(target); // Reset cursor as well as the route after a lethal hit.
            Log(target.Side, target.Name + " выбыл из боя.");
            foreach (Soldier witness in Soldiers)
            {
                if (witness.Alive && witness.Side == target.Side && V2.Distance(witness.Position, target.Position) < 9f)
                {
                    RecordStress(shooter, witness, 0f, (target.Id == Team(target).LeaderId) ? 0.28f : 0.2f);
                    witness.Morale = Math.Max(
                        0f,
                        witness.Morale - ((target.Id == Team(target).LeaderId) ? 0.28f : 0.2f)
                    );
                    Dialogue.Say(this, witness, 13, urgent: true);
                }
            }
        }

        private void UpdateObjective(float dt)
        {
            if (MissionCompletion.Update(this))
                return;
            int greenAtObjective = 0;
            int redAtObjective = 0;
            foreach (Soldier soldier in Soldiers)
            {
                if (
                    soldier.Alive
                    && !soldier.Incapacitated
                    && soldier.RescuerId < 0
                    && !soldier.Busy
                    && !soldier.Broken
                    && V2.Distance(soldier.Position, Map.Objective) <= 2.1f
                )
                {
                    if (soldier.Side == Side.Green)
                    {
                        greenAtObjective++;
                    }
                    else
                    {
                        redAtObjective++;
                    }
                }
            }

            Contested = greenAtObjective > 0 && redAtObjective > 0;
            if (greenAtObjective > 0 && redAtObjective == 0)
            {
                Capture = Math.Min(1f, Capture + dt / Config.CaptureSeconds);
            }
            else if (greenAtObjective == 0)
            {
                Capture = Math.Max(0f, Capture - dt / (Config.CaptureSeconds * 2f));
            }

            if (Alive(Side.Green) == 0)
            {
                Outcome = Outcome.GreenEliminated;
            }
            else if (!RaidMode && Capture >= 1f)
            {
                Outcome = Outcome.GreenCaptured;
            }
            else if (Time >= Config.TimeLimit && EnemyClearedAt < 0f)
            {
                Outcome = Outcome.RedHeld;
            }

            if (Outcome != 0)
            {
                Log(
                    (Outcome != Outcome.GreenCaptured) ? Side.Red : Side.Green,
                    (Outcome == Outcome.GreenCaptured)
                        ? "Точка «Рубеж» захвачена."
                        : (
                            (Outcome == Outcome.GreenEliminated)
                                ? "Атакующее отделение выбыло."
                                : "Время истекло. Оборона удержала рубеж."
                        )
                );
            }
        }
    }
}
