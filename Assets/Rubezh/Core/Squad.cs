using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class Squad
    {
        public readonly FormationEnvelopeState FormationEnvelope = new FormationEnvelopeState();
        public readonly GroupCommandPlan CommandPlan = new GroupCommandPlan();
        public readonly CommanderTransition CommandTransition = new CommanderTransition();
        public readonly AiGroupState Ai = new AiGroupState();
        public readonly SmokeState Smoke = new SmokeState();
        public DefenderGroupState DefenderGroup;
        public bool Contains(Soldier soldier)
        {
            return soldier.Side == Side && (DefenderGroup == null || soldier.DefenderGroup == DefenderGroup);
        }

        public V2 DefenseCenter(BattleSimulation b)
        {
            if (DefenderGroup != null && DefenderGroup.Cooperation.Active && DefenderGroup.Counterattack.Phase != RedCounterattackPhase.Retreat)
                return DefenderGroup.Cooperation.Anchor;
            return DefenderGroup == null || DefenderGroup.Pullback && DefenderGroup.Counterattack.Phase != RedCounterattackPhase.Assault ? b.Map.Objective : DefenderGroup.Spawn;
        }

        public readonly WithdrawalPlan Withdrawal = new WithdrawalPlan();
        public readonly PosturePlan Posture = new PosturePlan();
        public readonly ObjectiveCapturePlan CapturePlan = new ObjectiveCapturePlan();
        public readonly FlankPlan FlankPlan = new FlankPlan();
        public FormationSettings Formation = new FormationSettings();
        public float NextPairReview;
        public readonly BreakthroughState Breakthrough = new BreakthroughState();
        public readonly StraightPlan Straight = new StraightPlan();
        public bool TaskReplanRequested;
        public int TacticalComposition = -1;
        public bool TacticalHadFresh;
        public readonly ObservationMemory Observation = new ObservationMemory();
        public readonly SilentOrders.Core.DefenseState Defense = new SilentOrders.Core.DefenseState();
        public SilentOrders.Core.Side Side;
        public SilentOrders.Core.CombatStyle Style = SilentOrders.Core.CombatStyle.Balanced;
        public SilentOrders.Core.CombatStyle PendingStyle = SilentOrders.Core.CombatStyle.Balanced;
        public SilentOrders.Core.ManeuverIntent Intent;
        public SilentOrders.Core.ManeuverIntent PendingIntent;
        public SilentOrders.Core.CommanderCharacter Character;
        public bool PriorityPending;
        public bool PriorityChanged;
        public bool PriorityInitialized;
        public bool RegroupAssigned;
        public bool EmergencyRetreat;
        public float PriorityAt;
        public float SmokeWaitUntil;
        public float NextPriorityPlan;
        public float NextFeedback;
        public float LastAssessment;
        public int LastShots;
        public int LastHealth = 1000;
        public string PriorityStatus = "Замысел принят: баланс, центр";
        public string Execution = "Подготовка";
        public int MovingElement;
        public int LeaderId;
        public float NextPlan;
        public float LastBound;
        public float LastContact = -100f;
        public float FlankY = 3f;
        public int FocusEnemyId = -1;
        public float NextFocus;
        public int BoundCount;
        public int Revision;
        public int ReadyCount;
        public int RequiredSupport;
        public int MovingCount;
        public float PhaseSince;
        public float ReadySince = -1f;
        public float FrontX;
        public float RouteY = 18.5f;
        public float LastEncouragement;
        public bool Initialized;
        public bool HadContact;
        public SilentOrders.Core.ManeuverPhase Phase;
        public int SupportMoverId = -1;
        public int SupportFollowerId = -1;
        public int SupportMovePhase;
        public float LostSectorSince = -1f;
        public float SupportMoveSince;
        public float NextSupportMove;
        public float OpeningUntil;
        public float AidPauseSince = -1f;
        public float NextPlannedSmoke;
        public SilentOrders.Core.V2 SupportRally;
        public string SupportStatus = "Поддержка на исходных";
        public string DelayReason = "";
        public readonly System.Collections.Generic.List<SilentOrders.Core.V2> PlannedSmoke = new System.Collections.Generic.List<SilentOrders.Core.V2>();
        public bool EnvelopmentActive;
        public int EnvelopmentBounds;
        public float EnvelopmentSince;
        public SilentOrders.Core.V2 EnvelopmentThreat;
        public readonly System.Collections.Generic.List<int> SupportIds = new System.Collections.Generic.List<int>();
        public SilentOrders.Core.V2 Stage;
        public SilentOrders.Core.V2 ProgressCenter;
        public float LastProgress;
        public float LastAdaptation = -100f;
        public float AdaptStarted;
        public float NextSmoke;
        public float CombatStall;
        public float ProgressHealth = 1000f;
        public int Replans;
        public SilentOrders.Core.Adaptation Adaptation;
        public bool AdaptReleased;
        public readonly System.Collections.Generic.List<int> ManeuverIds = new System.Collections.Generic.List<int>();
        public string CommanderOrder = "Занять исходные позиции";
        public string Plan = "";
        public readonly System.Collections.Generic.Dictionary<int, SilentOrders.Core.Contact> Contacts = new System.Collections.Generic.Dictionary<int, SilentOrders.Core.Contact>();
    }
}
