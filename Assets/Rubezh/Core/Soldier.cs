using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class Soldier
    {
        public WeaponUpgrade WeaponUpgrade, BackWeaponUpgrade, DroppedWeaponUpgrade;
        public readonly FormationReturnState FormationReturn = new FormationReturnState();
        public GameplayRules Rules { get; internal set; }

        public Soldier(GameplayRules rules = null)
        {
            Rules = rules ?? GameplayRules.CaptureDefaults();
        }

        internal float DuoReview;
        internal SmokeCloud WatchedSmoke;
        internal float SmokeCheckAt;
        internal int SmokeFlank;
        public System.Collections.Generic.HashSet<string> CampSkills;
        public int CampReserveType, CampReserveRounds;
        public Rubezh.GreenLook Look;
        public readonly CareState Care = new CareState();
        public int ArmorLevel;
        public readonly BuddyActionState BuddyAction = new BuddyActionState();
        public readonly MotionState Motion = new MotionState();
        public readonly CrouchState Crouch = new CrouchState();
        public readonly PairExchangeState Exchange = new PairExchangeState();
        public readonly UnitAssignment Assignment = new UnitAssignment();
        public readonly AwarenessState Awareness = new AwarenessState();
        public readonly EngagementState Engagement = new EngagementState();
        public readonly CounterSniperAction CounterSniper = new CounterSniperAction();
        public readonly GrenadeEscapeState GrenadeEscape = new GrenadeEscapeState();
        public readonly CasualtyRecoveryState CasualtyRecovery = new CasualtyRecoveryState();
        public readonly AiTaskState Ai = new AiTaskState();
        public readonly ProgressMemory Progress = new ProgressMemory();
        public readonly ExecutionMemory Execution = new ExecutionMemory();
        public float SmokeReadyAt, HandsBusyUntil;
        public float SmokeThrowAt = -1f;
        public V2 SmokeThrowTarget;
        public DefenderGroupState DefenderGroup;
        public bool VehicleRally;
        public float VehicleRouteAt;
        // One-shot run to a distributed defensive line after leaving a transport.
        public bool VehicleDisperse;
        public SilentOrders.Core.V2 VehicleDisperseTarget;
        public float VehicleDisperseUntil;
        public readonly MovementPriority PriorityMovement = new MovementPriority();
        public readonly OutmaneuverMemory Outmaneuver = new OutmaneuverMemory();
        public readonly PositionMemory Tactical = new PositionMemory();
        public int Id;
        public int BuddyId;
        public int Element;
        public float DamageDealt;
        public float MoraleDamage;
        public float SuppressionDealt;
        public float NextPressureMove;
        public float PressureMoveUntil;
        public int HelpActions;
        public int Evacuated;
        public int SelfTreatments;
        public int CriticalHits;
        public readonly System.Collections.Generic.HashSet<int> SavedComrades = new System.Collections.Generic.HashSet<int>();
        public readonly AreaSuppressionState AreaSuppression = new AreaSuppressionState();
        public readonly IncomingFireState IncomingFire = new IncomingFireState();
        public int BurstRounds;
        public int KnownMapRevision;
        public bool SuppressiveFire;
        public bool ManeuverPaused;
        public SilentOrders.Core.V2 SavedManeuverAnchor;
        public float NextSuppressDecision;
        public float SuppressUntil;
        public float NextTacticalReload;
        public float LastBlast = -100f;
        public float SupportFailedUntil;
        public SilentOrders.Core.V2 SupportFailedPost;
        public string FireStatus = "Наблюдает";
        public float RejectedUntil;
        public float NextStatus;
        public SilentOrders.Core.V2 RejectedPost;
        public string Readiness = "На позиции";
        public bool KedrBinoculars; // Personal equipment, never inherited with command.
        public string Name;
        public SilentOrders.Core.Side Side;
        public SilentOrders.Core.Role Role;
        public bool SupplyPack;
        public int WeaponVariant;
        public bool WesternGun;
        public int WeaponModel => WeaponVariant % 6;
        // Weapon is distinct from squad duty; a medic or commander keeps the role
        // when picking up an abandoned rifle or machine gun.
        public NewMechanicsState Research = new NewMechanicsState();
        public bool ClassKitAssigned;
        public int ClassRank;
        public V2 ScopeAnchor;
        public bool ScopeTracking;
        public float ScopeSince;
        public int GunRoleOverride = -1;
        public Role GunRole => GunRoleOverride < 0 ? Role : (Role)GunRoleOverride;
        public bool GunSniper => GunRole == Role.Sniper;
        public bool GunMachineGunner => GunRole == Role.MachineGunner;

        public bool WeaponDropped, WeaponClaimed;
        public Role DroppedWeaponRole;
        public int DroppedWeaponVariant, DroppedAmmo, DroppedReserve;
        public float CorpseAngle, DroppedWeaponAngle;
        public V2 BloodAnchor;
        public bool BloodAnchorValid;
        public float CoverWaitStartedAt = -1f;
        public float FlagResponseUntil;
        public float TrafficRetryAt, TrafficYieldUntil;
        public V2 TrafficYieldTarget;
        public V2 DroppedWeaponPosition;
        public int LootTargetId = -1;
        public float LootRetryAt;
        public System.Collections.Generic.List<V2> EquipmentRecoveryRoute;
        public V2 EquipmentRecoveryGoal;
        // One extra firearm tracked in inventory; deliberately not rendered on the back.
        public bool HasBackWeapon, ActiveRecoveredWeapon;
        public float NextTourniquetThrow;
        public Role BackGunRole;
        public int BackVariant, BackAmmo, BackReserve;
        public int LootGroundId = -1, WeaponAidTargetId = -1;
        public float WeaponAidRetryAt;
        public bool WeaponPickupPriority;
        public int GrenadeType;
        public int GrenadeCharges;
        public int OtherGrenadeType, OtherGrenadeCharges, ThirdGrenadeType, ThirdGrenadeCharges, PocketMask;
        public int LauncherKind;
        public int ReserveLauncherKind = -1;
        public float NextSniperPost;
        public float SniperRetreatUntil, SniperNextSmoke;
        public bool SniperMoving, SniperRetreating;
        public float PairRegroupUntil;
        public int OrdnanceAimTarget = -1;
        public float OrdnanceAimSince;
        public float OrdnanceAimLast = -100f;
        public float NextOrdnance;
        public int RocketAimTarget = -1;
        public float RocketAimSince;
        public int Tourniquets = 1;
        public int Rockets; // Single-shot ready launcher: strictly 0 or 1.
        public int LauncherPickupId = -1;
        public float LauncherPickupRetryAt, LauncherThrowRetryAt;
        public int SmokeCharges;
        public int PackRockets;
        public int PackRshg;
        public int CombatTrainingLevel = 3; // Quick battles retain all skills; campaign applies purchased level.
        public int PackBoxes;
        public int PackMagazines;
        // Separate carried ammunition families; never alias the generic magazine/box counts.
        public int PackM4Magazines;
        public int PackM249Boxes;
        public int PackM24Magazines;
        public float NextAmmoThrow;
        public int RocketAimCover = -1;
        public readonly HeavyWeaponDecision HeavyRocket = new HeavyWeaponDecision(), HeavyUnderbarrel = new HeavyWeaponDecision();
        public int LoadedUnderbarrel; // One chambered GP round, separate from grenade pockets.
        public int PackSmokes, PackGrenades, PackUnderbarrel, PackF1, PackRGN, PackRGO, Medkits;
        public int TreatmentKind;
        public int TreatmentDonorId = -1;
        public int SupplyDonorId = -1;
        public int SupplyReceiverId = -1;
        public int SupplyKind;
        public float SupplySince;
        public float SupplyCooldown;
        public SilentOrders.Core.V2 SupplyAnchor;
        public SilentOrders.Core.Job SupplyJob;
        public bool Deployed = true;
        public int RigLevel = 2;
        public readonly GreenMechanicsState Mechanics = new GreenMechanicsState();
        public float ReloadFactor = 1f;
        public float AidSeconds = 3f;
        public float MoraleFactor = 1f;
        public float MoraleMaximum = 1f, MoraleRegenBonus, MoraleBlockReduction;
        public float AccuracyFactor = 1f;
        public bool Incapacitated;
        public bool Bleeding;
        public bool Stabilized;
        public int BleedSourceId = -1;
        public float NextCrawlPlan;
        public int RescuerId = -1;
        public int RescueTargetId = -1;
        public SilentOrders.Core.RescuePhase RescuePhase;
        public SilentOrders.Core.V2 RescueDestination;
        public SilentOrders.Core.V2 RescueOrigin;
        public float RescueSince;
        public float TreatmentUntil;
        public float AidCooldown;
        public float RescueCooldown;
        public float ImmobileSince = -1f;
        public float NextSpecial;
        public SilentOrders.Core.V2 Position;
        public SilentOrders.Core.V2 Destination;
        public SilentOrders.Core.V2 OrderAnchor;
        public SilentOrders.Core.V2 Facing;
        public float Health = 100f;
        public float Suppression;
        public float RaidSuppressionFactor=1f;
        public float Morale = 1f;
        public float MedicCombatReviewAt, MedicCombatCommitUntil;
        internal System.Collections.Generic.List<V2> MedicCombatRoute;
        internal V2 MedicCombatGoal;
        public int Ammo = 30;
        public MagazineStore Magazines = new MagazineStore(240), BackMagazines = new MagazineStore(0);
        public int Reserve
        {
            get
            {
                return Magazines.Total(this);
            }

            set
            {
                Magazines.SetTotal(this, value);
            }
        }

        public int Kills;
        public int Shots;
        public float NextSense;
        public float NextDecision;
        public float ConcussionReactUntil, NextMelee;
        public float NextShot;
        public float ReloadUntil;
        public float LastHurt = -100f;
        public float LastFire = -100f;
        public bool SomaliFiring;
        public float SomaliUntil, SomaliNext;
        public int SomaliRemaining;
        public V2 SomaliMuzzle, SomaliTarget;
        public bool SomaliLow;
        public int SomaliStarts, SomaliShots, SomaliHits, SomaliKills;
        public float SomaliPressure, SomaliDamageDealt, SomaliDamageReceived;
        public V2 FireLaneTarget;
        public float FireLaneUntil, FireHoldUntil, FireLaneReplanAt, FireLaneCrossUntil, FireLaneWaitSince = -1;
        public int FireLaneWaitShooter = -1;
        public float LastMoved;
        public float LastRadio = -100f;
        public float SpeechUntil;
        public float SpeechCooldown;
        public float RecoverUntil;
        public bool Moving;
        public bool Crouched;
        public bool Broken;
        // Morale/solidarity state (game simulation time).
        public float MoraleRecoveryBlockedUntil;
        public float IdleCombatSince = -1f, NextInitiativeAt, InitiativeUntil;
        public float NextHelpRequest, HelpRetreatUntil, HelpRetreatNextRoute;
        public bool HelpRetreatPlanned, HelpSmokePreferred;
        public int HelpTargetId = -1;
        public float HelpUntil, HelpNextSmoke, HelpNextRoute, HelpRouteUntil;
        public bool HelpReliefEligible;
        public bool MoveAuthorized;
        public bool SupportReady;
        public bool WasAtAnchor;
        public readonly UncoveredBoundState UncoveredBound = new UncoveredBoundState();
        public float LastIncoming = -100f;
        public float SettledSince;
        public float OrderUntil;
        public float NextChatter;
        public float NextRally;
        public float DistanceTravelled;
        public float CoverTime;
        public float ExposedTime;
        public float WaitTime;
        public SilentOrders.Core.V2 PreviousDestination;
        public int OrderRevision;
        public int LastPhrase = -1;
        public string Order = "Ожидание приказа";
        public float RetreatQuoteUntil;
        public float SpeechAt;
        public float LastSpokenAt = -100f;
        public bool SpeechUrgent;
        public string SpeechShort = "";
        public string LastSpoken = "";
        public string Speech = "";
        public string Reason = "Ожидание задачи";
        public SilentOrders.Core.Job Job;
        public SilentOrders.Core.ActionState State;
        public System.Collections.Generic.List<SilentOrders.Core.V2> Path = new System.Collections.Generic.List<SilentOrders.Core.V2>();
        public int PathCursor;
        public readonly System.Collections.Generic.Dictionary<int, SilentOrders.Core.Contact> Contacts = new System.Collections.Generic.Dictionary<int, SilentOrders.Core.Contact>();
        public readonly System.Collections.Generic.HashSet<int> Visible = new System.Collections.Generic.HashSet<int>();
        public bool Sniper => Role == SilentOrders.Core.Role.Sniper;
        public bool Underbarrel => (WeaponVariant % 6 == 2 || WeaponVariant % 6 == 3) && (Side == SilentOrders.Core.Side.Red || Role == SilentOrders.Core.Role.Commander || Role == SilentOrders.Core.Role.Rifleman);
        public int ReserveCapacity => RigLevel < 0 ? (GunMachineGunner ? 0 : MagazineSize) : GunSniper ? (MagazineSize * (3 + RigLevel * 2)) : (GunMachineGunner ? ((RigLevel + 1) * 100) : (90 + RigLevel * 60));
        public bool Busy => Care.Leader >= 0 || SupplyDonorId >= 0 || RescuePhase != 0 || TreatmentUntil > 0f;
        public bool Available => Alive && !Incapacitated && RescuerId < 0 && !Busy;
        public int MagazineSize => WeaponBalance.For(this).Magazine;
        public bool Alive => Health > 0f;
    }
}
