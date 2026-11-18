using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class AiContactReport
    {
        public int enemy;
        public string kind;
        public float x, y, age, confidence, uncertainty;
    }

    [Serializable]
    public sealed class AiUnitReport
    {
        public BuddyActionState cooperativeAction;
        public GreenMechanicsState greenMechanics;
        public int somaliStarts, somaliShots, somaliHits, somaliKills;
        public float somaliPressure, somaliDamageDealt, somaliDamageReceived;
        public int[] newMechanicsUses;
        public CareState care;
        public int armorLevel;
        public int pair, pairLeader, planGeneration, assignmentRevision, assignmentCompleted, assignmentFailures;
        public string assignmentOwner, assignmentState, assignmentReason;
        public bool limitedBound;
        public int ammoFamily, heavyReviews, heavyReserved, heavyUrgent;
        public int ammoInWeapon, reserveAmmo, recoveryAttempts, recoveryStarted, recoveryFinished, recoveryAborted, recoveryNoRoute;
        public float observedProgressAt, recoveryRemaining;
        public string fireStatus;
        public int id, revision, group;
        public string side, name, task, channels, reason, rejected, knowledge;
        public float x, y, goalX, goalY, health, morale, suppression, taskSince, lastProgress;
        public int ammunition, smokes, visible, contacts;
        public bool supportReady;
        public int rescueTarget, supplyDonor, supplyReceiver, launcherPickup, weaponPickup;
        public bool aggressiveSuppression;
        public int observedFireLanes;
        public int weaponAidTarget = -1, lootCorpse = -1;
        public bool hasBackWeapon;
        public int backAmmo, backReserve;
        public int helpTarget = -1;
        public float helpRemaining, sniperIncomingRemaining;
        public bool busy;
        public string counterSniper;
        public int sniperThreat = -1, coveredAlly = -1;
        public List<AiContactReport> evidence = new List<AiContactReport>();
    }

    [Serializable]
    public sealed class AiTraceReport
    {
        public float time, x, y, goalX, goalY;
        public int soldier, revision;
        public string transition, task, reason;
    }

    [Serializable]
    public sealed class AiBulletReport
    {
        public int shooter, intended;
        public float x, y, height, directionX, directionY, slope, speed, remaining, damage;
        public bool ricochet;
        public float endX, endY, endHeight, accuracy;
        public int endCell;
    }

    [Serializable]
    public sealed class AiGroupReport
    {
        public bool hunt;
        public int huntCover = -1, huntTarget = -1;
        public int group, leader, partner, mover, revision;
        public string name, phase, duty, reason;
        public float anchorX, anchorY, recoveryRemaining;
        public bool deployed;
    }

    [Serializable]
    public sealed class AiBattleReport
    {
        public PerformanceReport performance;
        public int newMechanicsMask;
        public bool fogEnabled, hardcoreFog;
        public int careMechanicsMask;
        public int schemaVersion = 18, seed, combatSeed, mission;
        public string version, exportedUtc, greenOrder, redOrder, outcome, fingerprint;
        public List<AiGroupReport> groups = new List<AiGroupReport>();
        public float battleTime;
        public int transitions, interruptions, rejectedOrders, smokeRequests, smokeRejected, smokeLaunched, smokeEffective, smokeIneffective;
        public int historyFrameLimit;
        public float historyInterval = BattleHistory.Interval;
        public int droppedHistoryFrames, droppedHistoryEvents, droppedDecisionEvents;
        public HistoryMap initialMap, currentMap;
        public HistoryFrame initialState, currentState;
        public List<HistoryFrame> history = new List<HistoryFrame>();
        public List<HistoryEvent> combatEvents = new List<HistoryEvent>();
        public List<CriticalCombatOutcome> criticalOutcomes = new List<CriticalCombatOutcome>();
        public int droppedCriticalOutcomes;
        public Tuning tuning;
        public int areaSuppressionBullets;
        public int ammoMagazinesThrown, ammoBoxesThrown, ammoDropsCollected, personalMagazineShares;
        public int bulletsLaunched, bulletHits, strayHits, friendlyBulletHits, ricochets, bulletWallStops, bulletVehicleStops, bulletLowCoverStops, bulletGroundStops;
        public int crossingPauses, crossingWaits, crossingDetours;
        public List<AiBulletReport> bullets = new List<AiBulletReport>();
        public string scope = "Developer history: initial map/state, snapshots every 2 seconds (up to last 900, capped at 30000 unit samples), 16384 combat/journal events and 8192 decisions. Dropped counters disclose truncation. Paths capped at 128 remaining nodes, contacts at 64 per unit. Diagnostic reconstruction, not a deterministic replay. History records both sides; each unit contact list remains limited to its own knowledge.";
        public List<AiUnitReport> units = new List<AiUnitReport>();
        public List<AiTraceReport> events = new List<AiTraceReport>();
        public static AiBattleReport Capture(BattleSimulation b, string fingerprint = "")
        {
            var d = b.Diagnostics;
            var report = new AiBattleReport
            {
                fogEnabled = b.Fog.Enabled,
                hardcoreFog = b.Fog.Hardcore,
                careMechanicsMask = b.Rules.CareMask,
                newMechanicsMask = b.Rules.MechanicsMask,
                version = RubezhVersion.Current,
                exportedUtc = DateTime.UtcNow.ToString("O"),
                seed = b.Seed,
                combatSeed = b.CombatSeed,
                mission = b.CampaignLevel,
                battleTime = b.Time,
                greenOrder = b.Green.CommanderOrder,
                redOrder = b.Red.CommanderOrder,
                outcome = b.Outcome.ToString(),
                fingerprint = fingerprint,
                transitions = d.Transitions,
                interruptions = d.Interruptions,
                rejectedOrders = d.RejectedOrders,
                smokeRequests = d.SmokeRequests,
                smokeRejected = d.SmokeRejected,
                smokeLaunched = d.SmokeLaunched,
                smokeEffective = d.SmokeEffective,
                smokeIneffective = d.SmokeIneffective
            };
            report.ammoMagazinesThrown = b.Stats.AmmoMagazinesThrown;
            report.ammoBoxesThrown = b.Stats.AmmoBoxesThrown;
            report.ammoDropsCollected = b.Stats.AmmoDropsCollected;
            report.personalMagazineShares = b.Stats.AmmunitionShares;
            report.historyFrameLimit = Math.Min(BattleHistory.FrameLimit, Math.Max(1, BattleHistory.UnitSampleLimit / Math.Max(1, b.Soldiers.Count)));
            report.initialMap = b.History.InitialMap;
            report.currentMap = HistoryMap.Capture(b.Map);
            report.initialState = b.History.Initial;
            report.currentState = BattleHistory.Capture(b);
            report.tuning = b.Config;
            report.criticalOutcomes = b.Outcomes.Export(b);
            report.droppedCriticalOutcomes = b.Outcomes.DroppedEvents;
            report.history.AddRange(b.History.Frames);
            report.combatEvents.AddRange(b.History.Events);
            report.droppedHistoryFrames = b.History.DroppedFrames;
            report.droppedHistoryEvents = b.History.DroppedEvents;
            report.droppedDecisionEvents = d.DroppedEvents;
            var shots = b.Ballistics;
            report.areaSuppressionBullets = shots.AreaLaunched;
            report.crossingPauses = shots.CrossingPauses;
            report.crossingWaits = shots.CrossingWaits;
            report.crossingDetours = shots.CrossingDetours;
            report.bulletsLaunched = shots.Launched;
            report.bulletHits = shots.Hits;
            report.strayHits = shots.StrayHits;
            report.friendlyBulletHits = shots.FriendlyHits;
            report.ricochets = shots.Ricochets;
            report.bulletWallStops = shots.WallStops;
            report.bulletVehicleStops = shots.VehicleStops;
            report.bulletLowCoverStops = shots.LowCoverStops;
            report.bulletGroundStops = shots.GroundStops;
            foreach (var p in shots.Active)
                report.bullets.Add(new AiBulletReport { shooter = p.ShooterId, intended = p.IntendedId, x = p.Position.X, y = p.Position.Y, height = p.Height, directionX = p.Direction.X, directionY = p.Direction.Y, slope = p.Slope, speed = p.Speed, remaining = p.Remaining, damage = p.Damage, ricochet = p.Ricocheted, endX = p.EndPoint.X, endY = p.EndPoint.Y, endHeight = p.EndHeight, endCell = p.EndCell, accuracy = p.Quality });
            for (int gi = 0; gi < b.DefenderGroups.Count; gi++)
            {
                var g = b.DefenderGroups[gi];
                var c = g.Cooperation;
                report.groups.Add(new AiGroupReport { hunt = LastOpponentHunt.GroupActive(b, g), huntCover = g.Hunt.CoverId, huntTarget = g.Hunt.Target == null ? -1 : g.Hunt.Target.EnemyId, group = gi, name = g.Name, leader = g.Command.LeaderId, partner = c.Partner, mover = c.Mover, revision = c.Revision, phase = g.Counterattack.Phase.ToString(), duty = c.Duty.ToString(), reason = c.Reason, anchorX = c.Anchor.X, anchorY = c.Anchor.Y, recoveryRemaining = Math.Max(0, c.RecoveryUntil - b.Time), deployed = g.Deployed });
            }

            foreach (var s in b.Soldiers)
                report.units.Add(new AiUnitReport { id = s.Id, revision = s.OrderRevision, group = s.DefenderGroup == null ? -1 : b.DefenderGroups.IndexOf(s.DefenderGroup), side = s.Side.ToString(), name = s.Name, task = s.Ai.Kind.ToString(), channels = s.Ai.Channels.ToString(), reason = s.Reason, rejected = s.Ai.Rejected, knowledge = s.Tactical.Knowledge, x = s.Position.X, y = s.Position.Y, goalX = s.OrderAnchor.X, goalY = s.OrderAnchor.Y, health = s.Health, morale = s.Morale, suppression = s.Suppression, taskSince = s.Ai.Started, lastProgress = s.Ai.LastProgress, ammunition = s.Ammo + s.Reserve, smokes = s.SmokeCharges, visible = s.Visible.Count, contacts = s.Contacts.Count, supportReady = s.SupportReady });
            for (int i = 0; i < b.Soldiers.Count; i++)
            {
                var s = b.Soldiers[i];
                var u = report.units[i];
                u.cooperativeAction = s.BuddyAction;
                u.greenMechanics = s.Mechanics;
                u.newMechanicsUses = (int[])s.Research.Uses.Clone();
                u.care = s.Care;
                u.armorLevel = s.ArmorLevel;
                u.somaliStarts = s.SomaliStarts;
                u.somaliShots = s.SomaliShots;
                u.somaliHits = s.SomaliHits;
                u.somaliKills = s.SomaliKills;
                u.somaliPressure = s.SomaliPressure;
                u.somaliDamageDealt = s.SomaliDamageDealt;
                u.somaliDamageReceived = s.SomaliDamageReceived;
                var a = s.Assignment;
                u.pair = a.PairId;
                u.pairLeader = a.Leader;
                u.planGeneration = a.PlanGeneration;
                u.assignmentRevision = a.Revision;
                u.assignmentCompleted = a.Completed;
                u.assignmentFailures = a.Failures;
                u.assignmentOwner = a.Owner;
                u.assignmentState = a.Status.ToString();
                u.assignmentReason = a.Reason;
                u.limitedBound = a.LimitedBound;
                u.ammoFamily = AmmoLogistics.Family(s);
                u.heavyReviews = s.HeavyRocket.Reviews + s.HeavyUnderbarrel.Reviews;
                u.heavyReserved = s.HeavyRocket.Reserved + s.HeavyUnderbarrel.Reserved;
                u.heavyUrgent = s.HeavyRocket.Urgent + s.HeavyUnderbarrel.Urgent;
                u.ammoInWeapon = s.Ammo;
                u.reserveAmmo = s.Reserve;
                u.fireStatus = s.FireStatus;
                u.observedProgressAt = s.Progress.ProgressAt;
                u.recoveryRemaining = Math.Max(0, s.Progress.Until - b.Time);
                u.recoveryAttempts = s.Progress.Attempts;
                u.recoveryStarted = s.Progress.Started;
                u.recoveryFinished = s.Progress.Finished;
                u.recoveryAborted = s.Progress.Aborted;
                u.recoveryNoRoute = s.Progress.NoRoute;
                u.weaponAidTarget = s.WeaponAidTargetId;
                u.lootCorpse = s.LootTargetId;
                u.hasBackWeapon = s.HasBackWeapon;
                u.backAmmo = s.BackAmmo;
                u.backReserve = s.BackReserve;
                u.rescueTarget = s.RescueTargetId;
                u.supplyDonor = s.SupplyDonorId;
                u.supplyReceiver = s.SupplyReceiverId;
                u.launcherPickup = s.LauncherPickupId;
                u.weaponPickup = s.LootGroundId;
                u.aggressiveSuppression = AreaSuppression.Active(b, s);
                u.observedFireLanes = s.IncomingFire.Lanes.FindAll(x => x.Until > b.Time).Count;
                u.helpTarget = s.HelpTargetId;
                u.helpRemaining = Math.Max(0, s.HelpUntil - b.Time);
                u.sniperIncomingRemaining = Math.Max(0, s.CounterSniper.IncomingUntil - b.Time);
                u.busy = s.Busy;
                u.counterSniper = s.CounterSniper.Phase.ToString();
                u.sniperThreat = s.CounterSniper.Threat == null ? -1 : s.CounterSniper.Threat.EnemyId;
                u.coveredAlly = s.CounterSniper.Victim;
                foreach (var c in Knowledge.Contacts(b, s))
                    u.evidence.Add(new AiContactReport { enemy = c.EnemyId, kind = Knowledge.Kind(b, s, c).ToString(), x = c.Position.X, y = c.Position.Y, age = b.Time - c.SeenAt, confidence = c.Confidence, uncertainty = c.Uncertainty });
                foreach (var c in b.Team(s).Observation.NoiseAreas)
                    u.evidence.Add(new AiContactReport { enemy = -1, kind = "Inferred", x = c.Position.X, y = c.Position.Y, age = b.Time - c.SeenAt, confidence = c.Confidence });
            }

            foreach (var e in d.Events)
                report.events.Add(new AiTraceReport { time = e.Time, soldier = e.Soldier, revision = e.Revision, transition = e.Event, task = e.Task, reason = e.Reason, x = e.Position.X, y = e.Position.Y, goalX = e.Destination.X, goalY = e.Destination.Y });
            return report;
        }
    }
}
