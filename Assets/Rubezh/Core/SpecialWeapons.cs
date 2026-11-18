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
        public static float Radius(SilentOrders.Core.Soldier s)
        {
            return (s.LauncherKind == 1) ? 4.3f : 3.3f;
        }

        public static float Range(SilentOrders.Core.BattleSimulation b)
        {
            return (b.Config.WeaponRange + 3f) * 1.2f;
        }

        public static bool UsefulImpact(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 target, SilentOrders.Core.V2 impact)
        {
            if (s.LauncherKind == 1 && !DynamicStructures.CanBreach(b.Map,s.Position,target,impact) && (b.Map.At(impact) == SilentOrders.Core.Tile.Wall || !b.Map.LineOfSight(impact, target)))
            {
                return false;
            }

            if (SilentOrders.Core.V2.Distance(s.Position, impact) < 4.5f || SilentOrders.Core.V2.Distance(target, impact) > Radius(s) * 0.9f)
            {
                return false;
            }

            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == s.Side && soldier.Alive && SilentOrders.Core.V2.Distance(soldier.Position, impact) < Radius(s) + 0.4f)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryLaunch(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (!BreakthroughTactics.OrdnanceAllowed(b, s, true) || b.Time < s.ConcussionReactUntil || (s.Role != SilentOrders.Core.Role.Grenadier && s.Role != SilentOrders.Core.Role.Commander && !HandLauncher.Active(s)) || (s.Side == Side.Green && s.LauncherKind == 1 && !SquadLoadout.ClassKit(s.Role, 0, 4)) || s.Rockets <= 0 || !s.Available || s.Broken || MotionCombat.InMotion(b, s) || s.Motion.Fast || s.Suppression > 0.7f || s.ReloadUntil > b.Time || b.Time < s.NextSpecial || (b.Time < s.NextShot && s.RocketAimTarget < 0))
            {
                s.RocketAimTarget = -1;
                return false;
            }

            SilentOrders.Core.Contact contact = null;
            float num = -1000f;
            V2 aimPoint = new V2();
            int coverCell = -1;
            foreach (SilentOrders.Core.Contact value in s.Contacts.Values)
            {
                if (!BreakthroughTactics.TargetAllowed(b, s, value) || (!s.Visible.Contains(value.EnemyId) && (!value.Visual || value.Confidence < 0.7f) && !CounterSniper.AreaFire(b, s, value)) || b.Time - value.SeenAt > (CounterSniper.AreaFire(b, s, value) ? 8f : 2.5f) || SilentOrders.Core.V2.Distance(s.Position, value.Position) > Range(b) || SilentOrders.Core.V2.Distance(s.Position, value.Position) < 4.5f || (!b.Map.LineOfSight(s.Position, value.Position) && !DynamicStructures.CanBreach(b.Map,s.Position,value.Position,b.Map.LauncherImpact(s.Position,value.Position))) || !UsefulImpact(b, s, value.Position, b.Map.LauncherImpact(s.Position, value.Position)))
                {
                    continue;
                }

                bool flag = false;
                foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                {
                    if (soldier.Alive && soldier.Side == s.Side && SilentOrders.Core.V2.Distance(soldier.Position, value.Position) < Radius(s) + 1.2f + value.Uncertainty + (value.Inferred ? 1.7f : 0))
                    {
                        flag = true;
                    }
                }

                if (flag || b.FriendlyInLane(s, value.Position))
                {
                    continue;
                }

                float num3;
                if (s.LauncherKind == 1)
                {
                    SilentOrders.Core.V2 v = b.Map.LauncherImpact(s.Position, value.Position);
                    int num2 = 0;
                    foreach (SilentOrders.Core.Contact value2 in s.Contacts.Values)
                    {
                        if (value2.EnemyId != value.EnemyId && value2.Visual && value2.Confidence >= 0.6f && b.Time - value2.SeenAt < 3f && SilentOrders.Core.V2.Distance(value2.Position, v) < Radius(s) && b.Map.LineOfSight(v, value2.Position))
                        {
                            num2++;
                        }
                    }

                    num3 = ((num2 <= 0) ? 1 : (100 + num2 * 10));
                    if (b.Time - value.FireSeenAt < 3f)
                    {
                        num3 += 0.5f;
                    }
                }
                else
                {
                    num3 = b.Map.Protection(value.Position, s.Position) * 3f;
                    foreach (SilentOrders.Core.Contact value3 in s.Contacts.Values)
                    {
                        if (value3.EnemyId != value.EnemyId && b.Time - value3.SeenAt < 3f && SilentOrders.Core.V2.Distance(value3.Position, value.Position) < Radius(s))
                        {
                            num3 += 2f;
                        }
                    }

                    if (b.Team(s).CombatStall > 15f)
                    {
                        num3 += 1f;
                    }
                }

                V2 coverPoint;
                int candidateCover;
                float coverValue = LauncherCoverDenial.Find(b, s, value, out coverPoint, out candidateCover);
                num3 += coverValue;
                if(DynamicStructures.CanBreach(b.Map,s.Position,value.Position,b.Map.LauncherImpact(s.Position,value.Position)))num3+=4;
                if (BreakthroughTactics.Assigned(b, s))
                    num3 += 10;
                if (CounterSniper.AreaFire(b, s, value))
                    num3 += 12;
                if (num3 > 0.8f && num3 > num)
                {
                    num = num3;
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
                    b.History.Event(b, "coverDenialAim", s.Id, contact.EnemyId, s.Position, aimPoint, num, "cover=" + coverCell);
            }

            if (!CombatCrouch.FireAllowed(b, s, aimPoint))
                return false;
            s.Facing = (aimPoint - s.Position).Normalized;
            s.State = SilentOrders.Core.ActionState.Launch;
            s.SupportReady = false;
            SilentOrders.Core.TacticalAI.Stop(s);
            bool reactionReady = ReactionTiming.Ready(b, s, contact);
            if (b.Time - s.RocketAimSince < LauncherAimSeconds || !reactionReady)
            {
                s.Reason = coverCell >= 0 ? "Сношу полезное укрытие противника" : "Прицеливаюсь из гранатомёта";
                return true;
            }

            s.RocketAimTarget = -1;
            float num4 = ((b.SmokeTransmission(s.Position, aimPoint) < 0.3f) ? 1.2f : 0.35f) + HandLauncher.ExtraScatter(s);
            num4 = Math.Max(0, num4 - (s.Side == Side.Green && s.Role == Role.Grenadier ? 1f : 0f));
            SilentOrders.Core.V2 v2 = CounterSniper.Scatter(b, aimPoint, contact.Uncertainty) + new SilentOrders.Core.V2(b.Random.Range(0f - num4, num4), b.Random.Range(0f - num4, num4));
            SilentOrders.Core.V2 v3 = v2;
            v2 = b.Map.LauncherImpact(s.Position, v2);
            if (!UsefulImpact(b, s, contact.Inferred ? v3 : aimPoint, v2))
            {
                s.NextSpecial = b.Time + 1f;
                s.Reason = "Пуск закрыт укрытием. Берегу заряд";
                return false;
            }

            foreach (SilentOrders.Core.Soldier soldier2 in b.Soldiers)
            {
                if (soldier2.Side == s.Side && soldier2.Alive && SilentOrders.Core.V2.Distance(soldier2.Position, v2) < Radius(s) + 0.4f)
                {
                    s.NextSpecial = b.Time + 1f;
                    return false;
                }
            }

            if (coverCell >= 0 && (LauncherCoverDenial.BreakChance(b.Map, coverCell, s.LauncherKind) <= 0 || V2.Distance(v2, aimPoint) > Radius(s) - .5f || !LauncherCoverDenial.Safe(b, s, contact, v3, v2)))
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
                b.History.Event(b, "coverDenialLaunch", s.Id, contact.EnemyId, s.Position, aimPoint, coverCell, shot.Kind == 4 ? "RSHG" : "RPG");
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
            s.State = SilentOrders.Core.ActionState.Launch;
            s.SupportReady = false;
            s.Facing = (target - s.Position).Normalized;
            b.Stats.RocketsFired++;
            b.AudioEvents.Emit(b.Time, 3, s.Id);
            SilentOrders.Core.V2 v4 = target;
            float wallAimHeight;bool wallAim=DynamicStructures.RocketHeight(b.Map,s.Position,target,b.Random,out wallAimHeight);
            b.Projectiles.Add(new SilentOrders.Core.ArcProjectile { HasWallAimHeight=wallAim,WallAimEndHeight=wallAimHeight,ShooterId = s.Id, Kind = ((s.LauncherKind == 1) ? 4 : 3), From = s.Position, To = v4, Position = s.Position, Height = 1.55f, Born = b.Time, Duration = System.Math.Max(0.18f, SilentOrders.Core.V2.Distance(s.Position, v4) / 22f) });
            bool closeFriend = false;
            if (!b.Historical01225)
                foreach (var friend in b.Soldiers)
                    if (friend != s && friend.Side == s.Side && friend.Alive && friend.Deployed && V2.Distance(friend.Position, s.Position) <= 1.6f)
                    {
                        closeFriend = true;
                        break;
                    }

            if (closeFriend)
                b.Say(s, "ВЫСТРЕЛ!!", true);
            else
                SilentOrders.Core.Dialogue.Say(b, s, 27, urgent: true);
            b.Log(s.Side, s.Name + ": одноразовый гранатомет использован.");
            LauncherLogistics.ReadyReserve(s);
        }

        public static int DirectHit(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier shooter, ref SilentOrders.Core.V2 impact)
        {
            SilentOrders.Core.V2 a = impact - shooter.Position;
            float length = a.Length;
            if (length < 0.01f)
            {
                return -1;
            }

            SilentOrders.Core.V2 v = a / length;
            float num = length;
            int num2 = -1;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (!soldier.Alive || soldier.Id == shooter.Id)
                {
                    continue;
                }

                SilentOrders.Core.V2 a2 = soldier.Position - shooter.Position;
                float num3 = SilentOrders.Core.V2.Dot(a2, v);
                float num4 = System.Math.Max(0f, a2.Length * a2.Length - num3 * num3);
                if (!(num3 < 0f) && !(num4 > 0.1024f))
                {
                    float num5 = num3 - (float)System.Math.Sqrt(0.1024f - num4);
                    if (!(num5 < 0f) && !(num5 > num) && b.Map.LineOfSight(shooter.Position, soldier.Position) && b.Map.Walkable(soldier.Position))
                    {
                        num = num5;
                        num2 = soldier.Id;
                    }
                }
            }

            if (num2 >= 0)
            {
                impact = shooter.Position + v * num;
            }

            return num2;
        }

        public static void ResolveBlast(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier shooter, SilentOrders.Core.V2 impact, int directVictim = -1, int launcherKind = -1, V2? origin = null, float impactHeight = 1.35f)
        {
            if ((launcherKind < 0 ? shooter.LauncherKind : launcherKind) == 1)
            {
                SilentOrders.Core.ArcedOrdnance.Resolve(b, shooter, impact, 4.3f, 50f, 65f, -1, 0f, thermobaric: true, origin: origin, impactHeight: impactHeight);
                return;
            }

            var shielded=DynamicStructures.Shielded(b,impact,impact-(origin??shooter.Position),impactHeight);var already=new System.Collections.Generic.HashSet<int>();
            int revision = b.Map.Revision;
            bool flag = b.Map.At(impact) == SilentOrders.Core.Tile.LowCover;
            var affected = new System.Collections.Generic.List<Soldier>();
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                float num = SilentOrders.Core.V2.Distance(soldier.Position, impact);
                if (soldier.Alive && !(num > 3.3f) && b.Map.LineOfSight(impact-(impact-(origin??shooter.Position)).Normalized*.03f, soldier.Position))
                {
                    float num2 = 1f - num / 4.3f;
                    float num3 = CombatTraining.Cover(b, soldier, soldier.Position, impact);
                    float num4 = 1f - num3 * (flag ? 0.35f : 0.55f);
                    already.Add(soldier.Id);soldier.LastBlast = b.Time;
                    float amount = ((soldier.Id == directVictim) ? b.Random.Range(80f, 95f) : (b.Random.Range(34.15f, 54.4f) * num2 * num4));
                    b.Damage(soldier, shooter, amount, 0.85f * num2, 0.4f * num2);
                    ConcussionEffects.Apply(b, soldier, num, 3.3f);
                    if (soldier.Alive)
                        affected.Add(soldier);
                }
            }

            DynamicStructures.Blast(b,impact,3.3f,1,false,impact-(origin??shooter.Position),impactHeight);
            DynamicStructures.BehindDamage(b,shooter,impact,shielded,1,already,impactHeight);
            CoverDestruction.Blast(b, impact, 3.3f, CoverBlast.Rpg, impact - (origin ?? shooter.Position));
            if (b.Map.Revision != revision)
            {
                foreach (var survivor in affected)
                {
                    b.RecordStress(shooter, survivor, 0f, .035f);
                    survivor.Morale = System.Math.Max(0f, survivor.Morale - .035f);
                }

                b.Log(shooter.Side, "Попадание в укрытие: защита повреждена, позиции пересчитываются.");
            }
        }
    }
}
