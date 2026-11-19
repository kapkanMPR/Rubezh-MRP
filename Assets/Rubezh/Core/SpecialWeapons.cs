using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SpecialWeapons
    {
        // Identical for both factions, both single-use launcher models.
        // Dedicated launcher preparation, independent of primary firearm acquisition.
        public const float LauncherAimSeconds = 2.5f;
        public const float BlastRadius = 3.3f;
        public const float DamageMin = 34.15f;
        public const float DamageMax = 54.4f;

        public static float Radius(Soldier s)
        {
            return (s.LauncherKind == 1) ? 4.3f : 3.3f;
        }

        public static float Range(BattleSimulation b)
        {
            return (b.Config.WeaponRange + 3f) * 1.2f;
        }

        public static bool UsefulImpact(BattleSimulation b, Soldier s, V2 target, V2 impact)
        {
            if (
                s.LauncherKind == 1
                && !DynamicStructures.CanBreach(b.Map, s.Position, target, impact)
                && (b.Map.At(impact) == Tile.Wall || !b.Map.LineOfSight(impact, target))
            )
            {
                return false;
            }

            if (V2.Distance(s.Position, impact) < 4.5f || V2.Distance(target, impact) > Radius(s) * 0.9f)
            {
                return false;
            }

            foreach (Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == s.Side && soldier.Alive && V2.Distance(soldier.Position, impact) < Radius(s) + 0.4f)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryLaunch(BattleSimulation b, Soldier s)
        {
            if (
                !BreakthroughTactics.OrdnanceAllowed(b, s, true)
                || b.Time < s.ConcussionReactUntil
                || (s.Role != Role.Grenadier && s.Role != Role.Commander && !HandLauncher.Active(s))
                || (s.Side == Side.Green && s.LauncherKind == 1 && !SquadLoadout.ClassKit(s.Role, 0, 4))
                || s.Rockets <= 0
                || !s.Available
                || s.Broken
                || MotionCombat.InMotion(b, s)
                || s.Motion.Fast
                || s.Suppression > 0.7f
                || s.ReloadUntil > b.Time
                || b.Time < s.NextSpecial
                || (b.Time < s.NextShot && s.RocketAimTarget < 0)
            )
            {
                s.RocketAimTarget = -1;
                return false;
            }

            Contact contact = null;
            float bestScore = -1000f;
            V2 aimPoint = new V2();
            int coverCell = -1;
            foreach (Contact value in s.Contacts.Values)
            {
                if (
                    !BreakthroughTactics.TargetAllowed(b, s, value)
                    || (
                        !s.Visible.Contains(value.EnemyId)
                        && (!value.Visual || value.Confidence < 0.7f)
                        && !CounterSniper.AreaFire(b, s, value)
                    )
                    || b.Time - value.SeenAt > (CounterSniper.AreaFire(b, s, value) ? 8f : 2.5f)
                    || V2.Distance(s.Position, value.Position) > Range(b)
                    || V2.Distance(s.Position, value.Position) < 4.5f
                    || (
                        !b.Map.LineOfSight(s.Position, value.Position)
                        && !DynamicStructures.CanBreach(
                            b.Map,
                            s.Position,
                            value.Position,
                            b.Map.LauncherImpact(s.Position, value.Position)
                        )
                    )
                    || !UsefulImpact(b, s, value.Position, b.Map.LauncherImpact(s.Position, value.Position))
                )
                {
                    continue;
                }

                bool blocked = false;
                foreach (Soldier soldier in b.Soldiers)
                {
                    if (
                        soldier.Alive
                        && soldier.Side == s.Side
                        && V2.Distance(soldier.Position, value.Position)
                            < Radius(s) + 1.2f + value.Uncertainty + (value.Inferred ? 1.7f : 0)
                    )
                    {
                        blocked = true;
                    }
                }

                if (blocked || b.FriendlyInLane(s, value.Position))
                {
                    continue;
                }

                float candidateScore;
                if (s.LauncherKind == 1)
                {
                    V2 v = b.Map.LauncherImpact(s.Position, value.Position);
                    int nearbyEnemies = 0;
                    foreach (Contact value2 in s.Contacts.Values)
                    {
                        if (
                            value2.EnemyId != value.EnemyId
                            && value2.Visual
                            && value2.Confidence >= 0.6f
                            && b.Time - value2.SeenAt < 3f
                            && V2.Distance(value2.Position, v) < Radius(s)
                            && b.Map.LineOfSight(v, value2.Position)
                        )
                        {
                            nearbyEnemies++;
                        }
                    }

                    candidateScore = ((nearbyEnemies <= 0) ? 1 : (100 + nearbyEnemies * 10));
                    if (b.Time - value.FireSeenAt < 3f)
                    {
                        candidateScore += 0.5f;
                    }
                }
                else
                {
                    candidateScore = b.Map.Protection(value.Position, s.Position) * 3f;
                    foreach (Contact value3 in s.Contacts.Values)
                    {
                        if (
                            value3.EnemyId != value.EnemyId
                            && b.Time - value3.SeenAt < 3f
                            && V2.Distance(value3.Position, value.Position) < Radius(s)
                        )
                        {
                            candidateScore += 2f;
                        }
                    }

                    if (b.Team(s).CombatStall > 15f)
                    {
                        candidateScore += 1f;
                    }
                }

                V2 coverPoint;
                int candidateCover;
                float coverValue = LauncherCoverDenial.Find(b, s, value, out coverPoint, out candidateCover);
                candidateScore += coverValue;
                if (
                    DynamicStructures.CanBreach(
                        b.Map,
                        s.Position,
                        value.Position,
                        b.Map.LauncherImpact(s.Position, value.Position)
                    )
                )
                    candidateScore += 4;
                if (BreakthroughTactics.Assigned(b, s))
                    candidateScore += 10;
                if (CounterSniper.AreaFire(b, s, value))
                    candidateScore += 12;
                if (candidateScore > 0.8f && candidateScore > bestScore)
                {
                    bestScore = candidateScore;
                    contact = value;
                    coverCell = candidateCover;
                    aimPoint = coverCell >= 0 ? coverPoint : value.Position;
                }
            }

            if (contact == null)
            {
                s.RocketAimTarget = -1;
                return false;
            }

            if (s.RocketAimTarget < 0 && !HeavyWeaponPolicy.Allow(b, s, contact, true))
            {
                s.RocketAimTarget = -1;
                return false;
            }

            DirectionalVision.WorkOnTarget(b, s, contact);
            if (s.RocketAimTarget != contact.EnemyId || s.RocketAimCover != coverCell)
            {
                s.RocketAimTarget = contact.EnemyId;
                s.RocketAimSince = b.Time;
                s.RocketAimCover = coverCell;
                if (coverCell >= 0)
                    b.History.Event(
                        b,
                        "coverDenialAim",
                        s.Id,
                        contact.EnemyId,
                        s.Position,
                        aimPoint,
                        bestScore,
                        "cover=" + coverCell
                    );
            }

            if (!CombatCrouch.FireAllowed(b, s, aimPoint))
                return false;
            s.Facing = (aimPoint - s.Position).Normalized;
            s.State = ActionState.Launch;
            s.SupportReady = false;
            TacticalAI.Stop(s);
            bool reactionReady = ReactionTiming.Ready(b, s, contact);
            if (b.Time - s.RocketAimSince < LauncherAimSeconds || !reactionReady)
            {
                s.Reason = coverCell >= 0 ? "Сношу полезное укрытие противника" : "Прицеливаюсь из гранатомёта";
                return true;
            }

            s.RocketAimTarget = -1;
            float scatterRadius =
                ((b.SmokeTransmission(s.Position, aimPoint) < 0.3f) ? 1.2f : 0.35f) + HandLauncher.ExtraScatter(s);
            scatterRadius = Math.Max(0, scatterRadius - (s.Side == Side.Green && s.Role == Role.Grenadier ? 1f : 0f));
            V2 impactPoint =
                CounterSniper.Scatter(b, aimPoint, contact.Uncertainty)
                + new V2(
                    b.Random.Range(0f - scatterRadius, scatterRadius),
                    b.Random.Range(0f - scatterRadius, scatterRadius)
                );
            V2 v3 = impactPoint;
            impactPoint = b.Map.LauncherImpact(s.Position, impactPoint);
            if (!UsefulImpact(b, s, contact.Inferred ? v3 : aimPoint, impactPoint))
            {
                s.NextSpecial = b.Time + 1f;
                s.Reason = "Пуск закрыт укрытием. Берегу заряд";
                return false;
            }

            foreach (Soldier witness in b.Soldiers)
            {
                if (
                    witness.Side == s.Side
                    && witness.Alive
                    && V2.Distance(witness.Position, impactPoint) < Radius(s) + 0.4f
                )
                {
                    s.NextSpecial = b.Time + 1f;
                    return false;
                }
            }

            if (
                coverCell >= 0
                && (
                    LauncherCoverDenial.BreakChance(b.Map, coverCell, s.LauncherKind) <= 0
                    || V2.Distance(impactPoint, aimPoint) > Radius(s) - .5f
                    || !LauncherCoverDenial.Safe(b, s, contact, v3, impactPoint)
                )
            )
            {
                s.NextSpecial = b.Time + 1;
                return false;
            }

            LaunchProjectile(b, s, v3);
            if (coverCell >= 0)
            {
                var shot = b.Projectiles[b.Projectiles.Count - 1];
                shot.DenialCover = coverCell;
                shot.DenialEnemy = contact.EnemyId;
                b.History.Event(
                    b,
                    "coverDenialLaunch",
                    s.Id,
                    contact.EnemyId,
                    s.Position,
                    aimPoint,
                    coverCell,
                    shot.Kind == 4 ? "RSHG" : "RPG"
                );
            }

            return true;
        }

        // Shared physical projectile, ammo consumption, visual/audio state and cooldowns.
        public static void LaunchProjectile(BattleSimulation b, Soldier s, V2 target)
        {
            TacticalAI.Stop(s);
            s.RocketAimTarget = -1;
            if (s.Mechanics.HandLauncher)
                s.Mechanics.TemporaryShots++;
            s.Rockets = 0;
            LauncherLogistics.EjectSpent(b, s); // Visible inert tube, thrown no farther than 2 cells.
            BreakthroughTactics.Fired(b, s);
            s.NextSpecial = b.Time + 4f;
            s.NextShot = b.Time + 2f;
            s.LastFire = b.Time;
            s.State = ActionState.Launch;
            s.SupportReady = false;
            s.Facing = (target - s.Position).Normalized;
            b.Stats.RocketsFired++;
            b.AudioEvents.Emit(b.Time, 3, s.Id);
            V2 v4 = target;
            float wallAimHeight;
            bool wallAim = DynamicStructures.RocketHeight(b.Map, s.Position, target, b.Random, out wallAimHeight);
            b.Projectiles.Add(
                new ArcProjectile
                {
                    HasWallAimHeight = wallAim,
                    WallAimEndHeight = wallAimHeight,
                    ShooterId = s.Id,
                    Kind = ((s.LauncherKind == 1) ? 4 : 3),
                    From = s.Position,
                    To = v4,
                    Position = s.Position,
                    Height = 1.55f,
                    Born = b.Time,
                    Duration = Math.Max(0.18f, V2.Distance(s.Position, v4) / 22f),
                }
            );
            bool closeFriend = false;
            if (!b.Historical01225)
                foreach (var friend in b.Soldiers)
                    if (
                        friend != s
                        && friend.Side == s.Side
                        && friend.Alive
                        && friend.Deployed
                        && V2.Distance(friend.Position, s.Position) <= 1.6f
                    )
                    {
                        closeFriend = true;
                        break;
                    }

            if (closeFriend)
                b.Say(s, "ВЫСТРЕЛ!!", true);
            else
                Dialogue.Say(b, s, 27, urgent: true);
            b.Log(s.Side, s.Name + ": одноразовый гранатомет использован.");
            LauncherLogistics.ReadyReserve(s);
        }

        public static int DirectHit(BattleSimulation b, Soldier shooter, ref V2 impact)
        {
            V2 a = impact - shooter.Position;
            float length = a.Length;
            if (length < 0.01f)
            {
                return -1;
            }

            V2 v = a / length;
            float hitDistance = length;
            int hitSoldier = -1;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (!soldier.Alive || soldier.Id == shooter.Id)
                {
                    continue;
                }

                V2 a2 = soldier.Position - shooter.Position;
                float alongRay = V2.Dot(a2, v);
                float perpendicularSquared = Math.Max(0f, a2.Length * a2.Length - alongRay * alongRay);
                if (!(alongRay < 0f) && !(perpendicularSquared > 0.1024f))
                {
                    float entryDistance = alongRay - (float)Math.Sqrt(0.1024f - perpendicularSquared);
                    if (
                        !(entryDistance < 0f)
                        && !(entryDistance > hitDistance)
                        && b.Map.LineOfSight(shooter.Position, soldier.Position)
                        && b.Map.Walkable(soldier.Position)
                    )
                    {
                        hitDistance = entryDistance;
                        hitSoldier = soldier.Id;
                    }
                }
            }

            if (hitSoldier >= 0)
            {
                impact = shooter.Position + v * hitDistance;
            }

            return hitSoldier;
        }

        public static void ResolveBlast(
            BattleSimulation b,
            Soldier shooter,
            V2 impact,
            int directVictim = -1,
            int launcherKind = -1,
            V2? origin = null,
            float impactHeight = 1.35f
        )
        {
            if ((launcherKind < 0 ? shooter.LauncherKind : launcherKind) == 1)
            {
                ArcedOrdnance.Resolve(
                    b,
                    shooter,
                    impact,
                    4.3f,
                    50f,
                    65f,
                    -1,
                    0f,
                    thermobaric: true,
                    origin: origin,
                    impactHeight: impactHeight
                );
                return;
            }

            var shielded = DynamicStructures.Shielded(b, impact, impact - (origin ?? shooter.Position), impactHeight);
            var already = new System.Collections.Generic.HashSet<int>();
            int revision = b.Map.Revision;
            bool lowCover = b.Map.At(impact) == Tile.LowCover;
            var affected = new System.Collections.Generic.List<Soldier>();
            foreach (Soldier soldier in b.Soldiers)
            {
                float distanceToImpact = V2.Distance(soldier.Position, impact);
                if (
                    soldier.Alive
                    && !(distanceToImpact > 3.3f)
                    && b.Map.LineOfSight(
                        impact - (impact - (origin ?? shooter.Position)).Normalized * .03f,
                        soldier.Position
                    )
                )
                {
                    float falloff = 1f - distanceToImpact / 4.3f;
                    float coverRating = CombatTraining.Cover(b, soldier, soldier.Position, impact);
                    float damageMultiplier = 1f - coverRating * (lowCover ? 0.35f : 0.55f);
                    already.Add(soldier.Id);
                    soldier.LastBlast = b.Time;
                    float amount = (
                        (soldier.Id == directVictim)
                            ? b.Random.Range(80f, 95f)
                            : (b.Random.Range(34.15f, 54.4f) * falloff * damageMultiplier)
                    );
                    b.Damage(soldier, shooter, amount, 0.85f * falloff, 0.4f * falloff);
                    ConcussionEffects.Apply(b, soldier, distanceToImpact, 3.3f);
                    if (soldier.Alive)
                        affected.Add(soldier);
                }
            }

            DynamicStructures.Blast(b, impact, 3.3f, 1, false, impact - (origin ?? shooter.Position), impactHeight);
            DynamicStructures.BehindDamage(b, shooter, impact, shielded, 1, already, impactHeight);
            CoverDestruction.Blast(b, impact, 3.3f, CoverBlast.Rpg, impact - (origin ?? shooter.Position));
            if (b.Map.Revision != revision)
            {
                foreach (var survivor in affected)
                {
                    b.RecordStress(shooter, survivor, 0f, .035f);
                    survivor.Morale = Math.Max(0f, survivor.Morale - .035f);
                }

                b.Log(shooter.Side, "Попадание в укрытие: защита повреждена, позиции пересчитываются.");
            }
        }
    }
}
