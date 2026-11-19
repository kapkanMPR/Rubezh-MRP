using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    [Serializable]
    public struct HistoryPoint
    {
        public float x,
            y;

        public static implicit operator HistoryPoint(V2 p)
        {
            return new HistoryPoint { x = p.X, y = p.Y };
        }
    }

    [Serializable]
    public sealed class HistoryPairTask
    {
        public bool lateralComplete;
        public int maneuverSide;
        public int group,
            pair,
            leader,
            a,
            z,
            mover,
            supportPair,
            revision,
            completed,
            failures;
        public string duty,
            reason;
        public HistoryPoint goal;
    }

    [Serializable]
    public sealed class HistoryCommandPlan
    {
        public int group,
            leader,
            deputy,
            generation,
            successions,
            pairSuccessions,
            assignments,
            completed,
            localFailures,
            pairFailures,
            merges,
            limitedBounds;
        public int volleyDesired,
            volleyActual,
            volleyMaximum;
        public string breakthroughPhase;
        public int snailBounds;
        public int[] withdrawMovers;
        public bool straight;
        public float straightRear,
            straightFront;
        public HistoryPoint straightAxis;
        public bool active;
        public string scheme,
            reason;
        public int style,
            intent,
            withdrawMover,
            withdrawBounds,
            withdrawCompleted,
            withdrawCoverWaits,
            withdrawCohesionWaits,
            localHoldMoves,
            holdDriftRejected;
        public float modeSince;
        public HistoryPoint retreatDirection;
        public List<HistoryPairTask> pairs = new List<HistoryPairTask>();

        public static HistoryCommandPlan Capture(Squad q, int group)
        {
            var s = q.CommandPlan;
            var r = new HistoryCommandPlan
            {
                volleyDesired = q.Breakthrough.DesiredVolley,
                volleyActual = q.Breakthrough.Targets.Count,
                volleyMaximum = q.Breakthrough.MaxVolley,
                breakthroughPhase = q.Breakthrough.Phase.ToString(),
                group = group,
                leader = s.Leader,
                deputy = s.Deputy,
                generation = s.Generation,
                successions = s.Successions,
                pairSuccessions = s.PairSuccessions,
                assignments = s.Assignments,
                completed = s.Completed,
                localFailures = s.LocalFailures,
                pairFailures = s.PairFailures,
                merges = s.OrphanMerges,
                limitedBounds = s.LimitedBounds,
                snailBounds = s.SnailBounds,
                withdrawMovers = q.Withdrawal.Movers.ToArray(),
                straight = q.Straight.Active,
                straightRear = q.Straight.Rear,
                straightFront = q.Straight.Front,
                straightAxis = q.Straight.Axis,
                active = s.Active,
                scheme = s.Scheme,
                reason = s.Reason,
                style = (int)q.Style,
                intent = (int)q.Intent,
                withdrawMover = q.Withdrawal.MoverId,
                withdrawBounds = q.Withdrawal.TotalBounds,
                withdrawCompleted = q.Withdrawal.Completed,
                withdrawCoverWaits = q.Withdrawal.CoverWaits,
                withdrawCohesionWaits = q.Withdrawal.CohesionWaits,
                localHoldMoves = q.Posture.LocalMoves,
                holdDriftRejected = q.Posture.RejectedDrift,
                modeSince = q.Posture.Since,
                retreatDirection = q.Withdrawal.Rear,
            };
            foreach (var p in s.Pairs)
                r.pairs.Add(
                    new HistoryPairTask
                    {
                        lateralComplete = p.LateralComplete,
                        maneuverSide = p.ManeuverSide,
                        group = group,
                        pair = p.Id,
                        leader = p.Leader,
                        a = p.A,
                        z = p.B,
                        mover = p.Mover,
                        supportPair = p.SupportPair,
                        revision = p.Revision,
                        completed = p.Completed,
                        failures = p.Failures,
                        duty = p.Duty.ToString(),
                        reason = p.Reason,
                        goal = p.Goal,
                    }
                );
            return r;
        }
    }

    [Serializable]
    public sealed class HistoryContact
    {
        public int enemy;
        public HistoryPoint position;
        public float seen,
            confidence,
            uncertainty;
        public bool visual,
            inferred;
    }

    [Serializable]
    public sealed class HistoryUnit
    {
        public bool seated,
            automaticCrouch;
        public float standRemaining;
        public int exchangePartner,
            exchanges;
        public string exchangeReason;
        public bool fastRun,
            wallTravel,
            shoulderPair;
        public int frontId,
            rearId,
            groupMovingPeak,
            movingShots,
            movingSmokes,
            wallBounds,
            shoulderShots;
        public float sprintRecovery;
        public int packM4Magazines,
            packM24Magazines,
            packM249Boxes;
        public int ammoFamily,
            heavyReviews,
            heavyReserved,
            packMagazines,
            packBoxes;
        public bool snailBound;
        public int coverId;
        public bool hasHoldAnchor;
        public HistoryPoint holdAnchor;
        public int commandStyle;
        public int pairId,
            pairLeader,
            taskGeneration,
            taskRevision;
        public string taskOwner,
            taskStatus;
        public string executionCause;
        public float stalledSince;
        public int localRepairs,
            returnedTasks,
            rejectedRepeats;
        public float visionRange,
            lookX,
            lookY,
            approachRemaining;
        public bool focused;
        public int focusTarget,
            approachTarget,
            emergencyShots;
        public float observedProgressAt,
            recoveryRemaining;
        public int recoveryStarted,
            recoveryFinished,
            recoveryAborted;
        public string fireStatus;
        public int id,
            group,
            weapon,
            ammo,
            reserve,
            shots,
            rescuer,
            patient,
            helpTarget,
            buddy,
            pathCursor,
            pathLength,
            smokes,
            grenades,
            rockets,
            tourniquets;
        public string name,
            side,
            role,
            state,
            job,
            order,
            reason,
            rejected,
            counterSniper;
        public HistoryPoint position,
            goal;
        public float health,
            morale,
            suppression,
            lastFire,
            lastIncoming,
            lastMoved,
            nextShot,
            reloadUntil,
            helpUntil,
            weaponRange;
        public bool deployed,
            incapacitated,
            bleeding,
            broken,
            busy,
            moving,
            moveAuthorized,
            supportReady,
            sniperRetreatCover,
            areaSuppression;
        public List<HistoryPoint> route = new List<HistoryPoint>();
        public List<HistoryContact> contacts = new List<HistoryContact>();
    }

    [Serializable]
    public sealed class HistoryGroup
    {
        public bool hunt;
        public int huntCover = -1,
            huntTarget = -1;
        public int id,
            mover;
        public string name,
            phase,
            duty,
            reason;
        public HistoryPoint anchor;
        public bool clear;
    }

    [Serializable]
    public sealed class HistoryCloud
    {
        public int sourceSide = -1;
        public HistoryPoint position;
        public float born,
            until;
    }

    [Serializable]
    public sealed class HistoryProjectile
    {
        public int shooter,
            kind;
        public HistoryPoint position,
            target;
        public float height,
            detonateAt;
        public bool landed;
    }

    [Serializable]
    public sealed class HistoryAmmo
    {
        public int id,
            from,
            to,
            family,
            rounds;
        public bool box,
            flying;
        public HistoryPoint position,
            landing;
    }

    [Serializable]
    public sealed class HistoryFrame
    {
        public List<HistoryAmmo> ammunition = new List<HistoryAmmo>();
        public float time,
            capture;
        public int mapRevision;
        public string outcome,
            greenOrder,
            redOrder;
        public string commandPhase,
            commandReason;
        public int acceptedStyle,
            acceptedIntent,
            executingStyle,
            executingIntent;
        public float commandAcceptedAt,
            commandExecuteAt;
        public bool commandTransition;
        public List<HistoryCommandPlan> commandPlans = new List<HistoryCommandPlan>();
        public List<HistoryUnit> units = new List<HistoryUnit>();
        public List<HistoryGroup> groups = new List<HistoryGroup>();
        public List<HistoryCloud> smoke = new List<HistoryCloud>();
        public List<HistoryProjectile> projectiles = new List<HistoryProjectile>();
    }

    [Serializable]
    public sealed class HistoryEvent
    {
        public float time,
            amount,
            accuracy;
        public int shot;
        public string kind,
            text;
        public int actor,
            target;
        public HistoryPoint from,
            to;
    }

    [Serializable]
    public sealed class HistoryMap
    {
        public int columns,
            rows,
            theme;
        public HistoryPoint entry,
            enemyEntry,
            objective;
        public int[] tiles,
            props;
        public float[] health,
            rotation,
            scaleX,
            scaleY;

        public static HistoryMap Capture(BattleMap m)
        {
            var result = new HistoryMap
            {
                columns = m.Columns,
                rows = m.Rows,
                theme = m.Theme,
                entry = m.Entry,
                enemyEntry = m.EnemyEntry,
                objective = m.Objective,
                health = (float[])m.CoverHealth.Clone(),
                rotation = (float[])m.LevelPropRotation.Clone(),
                scaleX = (float[])m.LevelPropScaleX.Clone(),
                scaleY = (float[])m.LevelPropScaleY.Clone(),
                tiles = new int[m.Tiles.Length],
                props = new int[m.LevelProps.Length],
            };
            for (int i = 0; i < result.tiles.Length; i++)
                result.tiles[i] = (int)m.Tiles[i];
            for (int i = 0; i < result.props.Length; i++)
                result.props[i] = m.LevelProps[i];
            return result;
        }
    }

    public sealed class BattleHistory
    {
        public const int FrameLimit = 900,
            EventLimit = 16384,
            UnitSampleLimit = 30000;
        public const float Interval = 2;
        public HistoryMap InitialMap;
        public HistoryFrame Initial;
        public readonly List<HistoryFrame> Frames = new List<HistoryFrame>();
        public readonly List<HistoryEvent> Events = new List<HistoryEvent>();
        public int DroppedFrames,
            DroppedEvents;
        float next;

        public void Event(
            BattleSimulation b,
            string kind,
            int actor,
            int target,
            V2 from,
            V2 to,
            float amount = 0,
            string text = "",
            int shot = 0,
            float accuracy = 0
        )
        {
            if (!b.Diagnostics.Enabled)
                return;
            if (Events.Count >= EventLimit)
            {
                Events.RemoveRange(0, 256);
                DroppedEvents += 256;
            }

            Events.Add(
                new HistoryEvent
                {
                    time = b.Time,
                    kind = kind,
                    actor = actor,
                    target = target,
                    from = from,
                    to = to,
                    amount = amount,
                    text = text,
                    shot = shot,
                    accuracy = accuracy,
                }
            );
        }

        public void Sample(BattleSimulation b)
        {
            if (!b.Diagnostics.Enabled || b.Time < next)
                return;
            next = b.Time + Interval;
            var f = Capture(b);
            if (Initial == null)
            {
                Initial = f;
                InitialMap = HistoryMap.Capture(b.Map);
            }

            int limit = Math.Min(FrameLimit, Math.Max(1, UnitSampleLimit / Math.Max(1, b.Soldiers.Count)));
            while (Frames.Count >= limit)
            {
                Frames.RemoveAt(0);
                DroppedFrames++;
            }

            Frames.Add(f);
        }

        public static HistoryFrame Capture(BattleSimulation b)
        {
            var f = new HistoryFrame
            {
                time = b.Time,
                capture = b.Capture,
                mapRevision = b.Map.Revision,
                outcome = b.Outcome.ToString(),
                greenOrder = b.Green.CommanderOrder,
                redOrder = b.Red.CommanderOrder,
            };
            f.commandPlans.Add(HistoryCommandPlan.Capture(b.Green, -1));
            if (!b.CustomDefenders)
                f.commandPlans.Add(HistoryCommandPlan.Capture(b.Red, -2));
            for (int gi = 0; gi < b.DefenderGroups.Count; gi++)
                if (b.DefenderGroups[gi].Deployed)
                    f.commandPlans.Add(HistoryCommandPlan.Capture(b.DefenderGroups[gi].Command, gi));
            var order = b.Green.CommandTransition;
            f.commandPhase = order.Phase;
            f.commandReason = order.Reason;
            f.commandTransition = order.Waiting;
            f.acceptedStyle = (int)order.Style;
            f.acceptedIntent = (int)order.Intent;
            f.executingStyle = (int)b.Green.Style;
            f.executingIntent = (int)b.Green.Intent;
            f.commandAcceptedAt = order.AcceptedAt;
            f.commandExecuteAt = order.ExecuteAt;
            foreach (var s in b.Soldiers)
            {
                var u = new HistoryUnit
                {
                    seated = s.Crouch.Seated,
                    automaticCrouch = CombatCrouch.Automatic(s),
                    standRemaining = Math.Max(0, s.Crouch.StandUntil - b.Time),
                    exchangePartner = s.Exchange.Partner,
                    exchanges = s.Exchange.Completed,
                    exchangeReason = s.Exchange.Reason,
                    fastRun = s.Motion.Fast,
                    wallTravel = WallTravel.Active(b, s),
                    shoulderPair = ShoulderPair.Linked(b, s),
                    frontId = s.Motion.FrontId,
                    rearId = s.Motion.RearId,
                    groupMovingPeak = s.Motion.GroupPeak,
                    movingShots = s.Motion.MovingShots,
                    movingSmokes = s.Motion.MovingSmokes,
                    wallBounds = s.Motion.WallBounds,
                    shoulderShots = s.Motion.ShoulderShots,
                    sprintRecovery = Math.Max(0, s.Motion.RecoveryUntil - b.Time),
                    ammoFamily = AmmoLogistics.Family(s),
                    heavyReviews = s.HeavyRocket.Reviews + s.HeavyUnderbarrel.Reviews,
                    heavyReserved = s.HeavyRocket.Reserved + s.HeavyUnderbarrel.Reserved,
                    packMagazines = s.PackMagazines,
                    packBoxes = s.PackBoxes,
                    packM4Magazines = s.PackM4Magazines,
                    packM24Magazines = s.PackM24Magazines,
                    packM249Boxes = s.PackM249Boxes,
                    snailBound = s.Assignment.SnailBound,
                    coverId = s.Assignment.CoverId,
                    pairId = s.Assignment.PairId,
                    pairLeader = s.Assignment.Leader,
                    taskGeneration = s.Assignment.PlanGeneration,
                    taskRevision = s.Assignment.Revision,
                    taskOwner = s.Assignment.Owner,
                    taskStatus = s.Assignment.Status.ToString(),
                    executionCause = s.Execution.Cause,
                    stalledSince = s.Execution.StalledSince,
                    localRepairs = s.Execution.Repairs,
                    returnedTasks = s.Execution.ReturnedTasks,
                    rejectedRepeats = s.Execution.RejectedRepeats,
                    visionRange = DirectionalVision.FrontRange(b, s),
                    lookX = s.Awareness.Look.X,
                    lookY = s.Awareness.Look.Y,
                    focused = s.Awareness.Focused,
                    focusTarget = s.Awareness.FocusTarget,
                    approachTarget = s.Engagement.Target,
                    approachRemaining = Math.Max(0, s.Engagement.ApproachUntil - b.Time),
                    emergencyShots = s.Engagement.EmergencyShots,
                    observedProgressAt = s.Progress.ProgressAt,
                    recoveryRemaining = Math.Max(0, s.Progress.Until - b.Time),
                    recoveryStarted = s.Progress.Started,
                    recoveryFinished = s.Progress.Finished,
                    recoveryAborted = s.Progress.Aborted,
                    fireStatus = s.FireStatus,
                    id = s.Id,
                    group = s.DefenderGroup == null ? -1 : b.DefenderGroups.IndexOf(s.DefenderGroup),
                    name = s.Name,
                    side = s.Side.ToString(),
                    role = s.Role.ToString(),
                    weapon = s.WeaponVariant,
                    ammo = s.Ammo,
                    reserve = s.Reserve,
                    shots = s.Shots,
                    rescuer = s.RescuerId,
                    patient = s.RescueTargetId,
                    helpTarget = s.HelpTargetId,
                    buddy = s.BuddyId,
                    pathCursor = s.PathCursor,
                    pathLength = s.Path.Count,
                    smokes = s.SmokeCharges,
                    grenades = s.GrenadeCharges,
                    rockets = s.Rockets,
                    tourniquets = s.Tourniquets,
                    state = s.State.ToString(),
                    job = s.Job.ToString(),
                    order = s.Order,
                    reason = s.Reason,
                    rejected = s.Ai.Rejected,
                    counterSniper = s.CounterSniper.Phase.ToString(),
                    position = s.Position,
                    goal = s.Destination,
                    health = s.Health,
                    morale = s.Morale,
                    suppression = s.Suppression,
                    lastFire = s.LastFire,
                    lastIncoming = s.LastIncoming,
                    lastMoved = s.LastMoved,
                    nextShot = s.NextShot,
                    reloadUntil = s.ReloadUntil,
                    helpUntil = s.HelpUntil,
                    weaponRange = b.WeaponRange(s),
                    deployed = s.Deployed,
                    incapacitated = s.Incapacitated,
                    bleeding = s.Bleeding,
                    broken = s.Broken,
                    busy = s.Busy,
                    moving = s.Moving,
                    moveAuthorized = s.MoveAuthorized,
                    supportReady = s.SupportReady,
                    sniperRetreatCover = SniperRetreatCover.Holding(b, s),
                    areaSuppression = AreaSuppression.Active(b, s),
                };
                for (int i = s.PathCursor; i < s.Path.Count && u.route.Count < 128; i++)
                    u.route.Add(s.Path[i]);
                foreach (var c in Knowledge.Contacts(b, s))
                    if (u.contacts.Count < 64)
                        u.contacts.Add(
                            new HistoryContact
                            {
                                enemy = c.EnemyId,
                                position = c.Position,
                                seen = c.SeenAt,
                                confidence = c.Confidence,
                                uncertainty = c.Uncertainty,
                                visual = c.Visual && s.Visible.Contains(c.EnemyId),
                                inferred = c.Inferred,
                            }
                        );
                V2 hold;
                u.commandStyle = (int)b.Team(s).Style;
                u.hasHoldAnchor = b.Team(s).Posture.Anchors.TryGetValue(s.Id, out hold);
                u.holdAnchor = hold;
                f.units.Add(u);
            }

            for (int i = 0; i < b.DefenderGroups.Count; i++)
            {
                var g = b.DefenderGroups[i];
                f.groups.Add(
                    new HistoryGroup
                    {
                        hunt = LastOpponentHunt.GroupActive(b, g),
                        huntCover = g.Hunt.CoverId,
                        huntTarget = g.Hunt.Target == null ? -1 : g.Hunt.Target.EnemyId,
                        id = i,
                        name = g.Name,
                        phase = g.Counterattack.Phase.ToString(),
                        duty = g.Cooperation.Duty.ToString(),
                        reason = g.Cooperation.Reason,
                        anchor = g.Cooperation.Anchor,
                        mover = g.Counterattack.LastMoverId,
                        clear = g.Counterattack.RetreatClear,
                    }
                );
            }

            foreach (var c in b.Smoke)
                f.smoke.Add(
                    new HistoryCloud
                    {
                        sourceSide = c.SourceSide,
                        position = c.Position,
                        born = c.Born,
                        until = c.Until,
                    }
                );
            foreach (var p in b.Projectiles)
                f.projectiles.Add(
                    new HistoryProjectile
                    {
                        shooter = p.ShooterId,
                        kind = p.Kind,
                        position = p.Position,
                        target = p.To,
                        height = p.Height,
                        landed = p.Landed,
                        detonateAt = p.DetonateAt,
                    }
                );
            foreach (var a in b.GroundAmmo)
                if (!a.Collected)
                    f.ammunition.Add(
                        new HistoryAmmo
                        {
                            id = a.Id,
                            from = a.FromSoldierId,
                            to = a.IntendedFor,
                            family = a.Family,
                            rounds = a.Rounds,
                            box = a.Box,
                            flying = a.Flying(b.Time),
                            position = a.Position(b.Time),
                            landing = a.To,
                        }
                    );
            return f;
        }
    }
}
