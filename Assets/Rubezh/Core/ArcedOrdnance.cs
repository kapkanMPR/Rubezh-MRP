using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class ArcedOrdnance
    {
        public const float UnderbarrelAimSeconds = 2f, HandAimSeconds = 1f;
        public const float UnderbarrelRadius = 2.7f;
        public const float HandRadius = 2.5f;
        public const float F1Radius = HandRadius * .75f;
        public static float BlastRadius(ArcProjectile p) => p.F1 ? F1Radius : p.Kind == 2 ? UnderbarrelRadius : HandRadius;
        public static float Range(SilentOrders.Core.BattleSimulation b, int type)
        {
            return (type == 2) ? (SilentOrders.Core.SpecialWeapons.Range(b) - 2f) : (HandGrenades.Heavy(type) ? 8f : 9f);
        }

        public static float Height(float t, int type)
        {
            return (type >= 3) ? (1.55f - 0.8f * t) : (((type == 2) ? 1.55f : 1.35f) * (1f - t) + 24f * t * (1f - t));
        }

        private static bool FriendlyArea(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 point, float radius)
        {
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == s.Side && soldier.Alive && (SilentOrders.Core.V2.Distance(soldier.Position, point) < radius + 0.8f || (soldier.Moving && SilentOrders.Core.V2.Distance(soldier.OrderAnchor, point) < radius + 0.8f)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryUse(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (s.Underbarrel && s.LoadedUnderbarrel > 0)
            {
                int pocketType = s.GrenadeType, pocketCount = s.GrenadeCharges;
                bool used;
                s.GrenadeType = 2;
                s.GrenadeCharges = s.LoadedUnderbarrel;
                try
                {
                    used = TryUseCurrent(b, s);
                    s.LoadedUnderbarrel = s.GrenadeCharges;
                }
                finally
                {
                    s.GrenadeType = pocketType;
                    s.GrenadeCharges = pocketCount;
                }

                if (used || s.OrdnanceAimTarget >= 0)
                    return used;
            }

            if (TryUseCurrent(b, s))
                return true;
            if (s.OrdnanceAimTarget >= 0)
                return false;
            for (int slot = 0; slot < 2; slot++)
            {
                if ((slot == 0 ? s.OtherGrenadeCharges : s.ThirdGrenadeCharges) <= 0)
                    continue;
                GrenadePockets.Swap(s, slot == 1);
                if (TryUseCurrent(b, s))
                    return true;
                if (s.OrdnanceAimTarget >= 0)
                    return false;
                GrenadePockets.Swap(s, slot == 1);
            }

            return false;
        }

        static bool TryUseCurrent(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (!BreakthroughTactics.OrdnanceAllowed(b, s, false) || b.Time < s.ConcussionReactUntil || !s.Available || s.Broken || MotionCombat.InMotion(b, s) || s.Motion.Fast || s.Suppression > 0.65f || s.GrenadeCharges <= 0 || s.GrenadeType == 0 || (s.GrenadeType == 2 && !s.Underbarrel) || s.ReloadUntil > b.Time || b.Time < s.NextOrdnance)
            {
                s.OrdnanceAimTarget = -1;
                return false;
            }

            float num = HandGrenades.Heavy(s.GrenadeType) ? F1Radius : ((s.GrenadeType == 2) ? 2.7f : 2.5f);
            SilentOrders.Core.Contact contact = null;
            float num2 = -10000f;
            foreach (SilentOrders.Core.Contact value in s.Contacts.Values)
            {
                float num3 = SilentOrders.Core.V2.Distance(s.Position, value.Position);
                if (!BreakthroughTactics.TargetAllowed(b, s, value) || (!value.Visual && !CounterSniper.AreaFire(b, s, value)) || value.Confidence < 0.6f || b.Time - value.SeenAt > (CounterSniper.AreaFire(b, s, value) ? 8f : 3f) || num3 > Range(b, s.GrenadeType) + (s.Side == Side.Green && s.Role == Role.Rifleman && s.GrenadeType != 2 ? 1f : 0f) || num3 < num + 1.5f || !b.Map.Walkable(value.Position) || FriendlyArea(b, s, value.Position, num + (value.Inferred ? GrenadeSafety.ScatterRadius(b, s, value, s.GrenadeType) : 0)))
                {
                    continue;
                }

                SilentOrders.Core.V2 v = ArcImpact(b, s.Position, value.Position, HandGrenades.ArcKind(s.GrenadeType));
                if (SilentOrders.Core.V2.Distance(v, value.Position) > num * 0.6f || FriendlyArea(b, s, v, num))
                {
                    continue;
                }

                int num4 = 0;
                foreach (SilentOrders.Core.Contact value2 in s.Contacts.Values)
                {
                    if (value2.EnemyId != value.EnemyId && value2.Visual && value2.Confidence >= 0.6f && b.Time - value2.SeenAt < 3f && SilentOrders.Core.V2.Distance(value2.Position, value.Position) < num && b.Map.LineOfSight(value.Position, value2.Position))
                    {
                        num4++;
                    }
                }

                bool flag = !b.Map.LineOfSight(s.Position, value.Position);
                float num5 = (CounterSniper.AreaFire(b, s, value) ? 700f : 0f) + (float)((num4 > 0) ? (1000 + num4 * 100) : (flag ? 500 : 0)) + b.Map.Protection(value.Position, s.Position) * 3f + (float)((b.Time - value.FireSeenAt < 3f) ? 2 : 0) - num3 * 0.025f;
                if (num5 > num2)
                {
                    contact = value;
                    num2 = num5;
                }
            }

            if (contact == null)
            {
                s.OrdnanceAimTarget = -1;
                return false;
            }

            if (s.GrenadeType == 2 && s.OrdnanceAimTarget < 0 && !HeavyWeaponPolicy.Allow(b, s, contact, false))
            {
                s.OrdnanceAimTarget = -1;
                return false;
            }

            DirectionalVision.WorkOnTarget(b, s, contact);
            if (s.OrdnanceAimTarget != contact.EnemyId || b.Time - s.OrdnanceAimLast > 0.2f)
            {
                s.OrdnanceAimTarget = contact.EnemyId;
                s.OrdnanceAimSince = b.Time;
            }

            s.OrdnanceAimLast = b.Time;
            s.State = SilentOrders.Core.ActionState.GrenadeAim;
            s.SupportReady = false;
            if (!CombatCrouch.FireAllowed(b, s, contact.Position))
                return false;
            s.Facing = (contact.Position - s.Position).Normalized;
            SilentOrders.Core.TacticalAI.Stop(s);
            float num6 = ((s.GrenadeType == 2) ? UnderbarrelAimSeconds : HandAimSeconds);
            bool reactionReady = s.GrenadeType != 2 || ReactionTiming.Ready(b, s, contact);
            if (b.Time - s.OrdnanceAimSince < num6 || !reactionReady)
            {
                s.Reason = ((s.GrenadeType == 2) ? "Навожу подствольник" : "Готовлю бросок гранаты");
                return true;
            }

            SilentOrders.Core.V2 v2 = GrenadeSafety.Scatter(b, s, contact, s.GrenadeType);
            if (v2.X < 0 || v2.Y < 0 || !b.Map.Inside((int)v2.X, (int)v2.Y) || FriendlyArea(b, s, v2, num))
            {
                s.OrdnanceAimTarget = -1;
                s.NextOrdnance = b.Time + 1f;
                return false;
            }

            SilentOrders.Core.V2 v3 = ArcImpact(b, s.Position, v2, HandGrenades.ArcKind(s.GrenadeType));
            if (SilentOrders.Core.V2.Distance(v3, v2) > num * 0.6f || FriendlyArea(b, s, v3, num))
            {
                s.OrdnanceAimTarget = -1;
                s.NextOrdnance = b.Time + 2f;
                return false;
            }

            s.GrenadeCharges--;
            if (s.GrenadeType == 2)
                b.AudioEvents.Emit(b.Time, 4, s.Id);
            BreakthroughTactics.Fired(b, s);
            s.NextOrdnance = b.Time + 6f;
            s.NextShot = b.Time + 1f;
            s.LastFire = b.Time;
            s.OrdnanceAimTarget = -1;
            b.Projectiles.Add(new SilentOrders.Core.ArcProjectile { RoofLowArc = RoofRules.LowArc(b.Map, s.Position, v2, HandGrenades.ArcKind(s.GrenadeType)), ShooterId = s.Id, Kind = HandGrenades.ArcKind(s.GrenadeType), F1 = HandGrenades.Heavy(s.GrenadeType), ImpactFuse = HandGrenades.Impact(s.GrenadeType), HandType = s.GrenadeType, From = s.Position, To = v2, Position = s.Position, Height = Height(0f, HandGrenades.ArcKind(s.GrenadeType)), Born = b.Time, Duration = ((s.GrenadeType == 2) ? (0.8f + SilentOrders.Core.V2.Distance(s.Position, v2) / 12f) : (1f + SilentOrders.Core.V2.Distance(s.Position, v2) / 7f)) });
            if (HandGrenades.IsHand(s.GrenadeType) && s.Side == Side.Green)
                FriendlyGrenadeSafety.Warn(b, s, b.Projectiles[b.Projectiles.Count - 1]);
            else
                b.Say(s, (s.GrenadeType == 2) ? "Подствольник — выстрел!" : "Граната!", urgent: true);
            return true;
        }

        public static SilentOrders.Core.V2 ArcImpact(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.V2 from, SilentOrders.Core.V2 to, int kind)
        {
            bool low = RoofRules.LowArc(b.Map, from, to, kind);
            V2 previous = from;
            float previousHeight = RoofRules.ArcHeight(0, kind, low);
            int num = System.Math.Max(2, (int)System.Math.Ceiling(SilentOrders.Core.V2.Distance(from, to) * 16f));
            for (int i = 1; i <= num; i++)
            {
                float num2 = (float)i / (float)num;
                SilentOrders.Core.V2 v = from + (to - from) * num2;
                float height = RoofRules.ArcHeight(num2, kind, low);
                V2 roofHit;
                if (RoofRules.Crosses(b.Map, previous, previousHeight, v, height, out roofHit))
                    return roofHit;
                previous = v;
                previousHeight = height;
                if (Blocked(b, v, height))
                {
                    return v;
                }
            }

            return to;
        }

        private static bool Blocked(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.V2 p, float height)
        {
            if(DynamicStructures.Material(b.Map,p,height))return true;
            SilentOrders.Core.Tile tile = b.Map.At(p);
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            int cell = y * b.Map.Columns + x;
            if (b.Map.Inside(x, y) && b.Map.ProceduralCells != null && b.Map.ProceduralCells[cell])
                return b.Map.DynamicBuildings!=null ? DynamicStructures.Material(b.Map,p,height) : (tile == Tile.Wall || tile == Tile.LowCover) && height <= b.Map.ProceduralHeight[cell] && b.Map.ProceduralHeight[cell] > 0;
            var o = b.Map.Inside(x, y) ? StudioRules.At(b.Map, y * b.Map.Columns + x) : null;
            bool blocked;
            if (ArcOpening.TryBlock(o, p, height, out blocked))
                return blocked;
            return (tile == SilentOrders.Core.Tile.Wall && height <= 2.4f) || (tile == SilentOrders.Core.Tile.LowCover && height <= 1f);
        }

        public static void Update(SilentOrders.Core.BattleSimulation b)
        {
            SmokeThrow.Update(b);
            for (int num = b.Projectiles.Count - 1; num >= 0; num--)
            {
                SilentOrders.Core.ArcProjectile arcProjectile = b.Projectiles[num];
                if (arcProjectile.StoryMortarIndex >= 0)
                {
                    if (MissionMortarSupport.UpdateProjectile(b, arcProjectile))
                        b.Projectiles.RemoveAt(num);
                    continue;
                }
                if (!arcProjectile.RoofProfileReady)
                {
                    arcProjectile.RoofLowArc = RoofRules.LowArc(b.Map, arcProjectile.From, arcProjectile.To, arcProjectile.Kind);
                    arcProjectile.RoofProfileReady = true;
                }

                if (arcProjectile.Landed && b.Time + 0.00001f < arcProjectile.DetonateAt)
                    continue;
                if (arcProjectile.Rethrown && b.Time >= arcProjectile.DetonateAt)
                {
                    arcProjectile.Landed = true;
                    arcProjectile.Height = 0;
                }

                float num2 = SilentOrders.Core.Util.Clamp((b.Time - arcProjectile.Born) / arcProjectile.Duration, 0f, 1f);
                bool flag = false;
                int num3 = -1;
                int num4 = System.Math.Max(1, (int)System.Math.Ceiling((num2 - arcProjectile.LastProgress) * SilentOrders.Core.V2.Distance(arcProjectile.From, arcProjectile.To) * 20f));
                for (int i = 1; !arcProjectile.Landed && i <= num4; i++)
                {
                    float num5 = arcProjectile.LastProgress + (num2 - arcProjectile.LastProgress) * (float)i / (float)num4;
                    V2 nextArcPosition = arcProjectile.From + (arcProjectile.To - arcProjectile.From) * num5;
                    arcProjectile.DistanceTravelled += V2.Distance(arcProjectile.Position, nextArcPosition);
                    float nextHeight = arcProjectile.Kind>=3&&arcProjectile.HasWallAimHeight ? 1.55f+(arcProjectile.WallAimEndHeight-1.55f)*num5 : RoofRules.ArcHeight(num5, arcProjectile.Kind, arcProjectile.RoofLowArc);
                    V2 roofHit;
                    if (RoofRules.Crosses(b.Map, arcProjectile.Position, arcProjectile.Height, nextArcPosition, nextHeight, out roofHit))
                    {
                        arcProjectile.RoofTopHit = arcProjectile.Height >= RoofRules.Height;
                        arcProjectile.Position = roofHit;
                        arcProjectile.Height = RoofRules.Height;
                        flag = true;
                        break;
                    }

                    V2 wallHit;float wallFraction;
                    if(DynamicStructures.Ray(b.Map,arcProjectile.Position,arcProjectile.Height,nextArcPosition,nextHeight,out wallHit,out wallFraction)){
                        DynamicStructures.Glass(b.Map,arcProjectile.Position,arcProjectile.Height,wallHit,arcProjectile.Height+(nextHeight-arcProjectile.Height)*wallFraction);
                        arcProjectile.Position=wallHit;arcProjectile.Height+=(nextHeight-arcProjectile.Height)*wallFraction;flag=true;break;
                    }
                    DynamicStructures.Glass(b.Map,arcProjectile.Position,arcProjectile.Height,nextArcPosition,nextHeight);
                    arcProjectile.Position = nextArcPosition;
                    arcProjectile.Height = nextHeight;
                    if (Blocked(b, arcProjectile.Position, arcProjectile.Height))
                    {
                        flag = true;
                        break;
                    }

                    if ((arcProjectile.Kind >= 3 || (arcProjectile.Kind == 2 && num5 > 0.5f)) && arcProjectile.Height < 1.7f)
                    {
                        foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                        {
                            if (soldier.Id != arcProjectile.ShooterId && soldier.Alive && SilentOrders.Core.V2.Distance(soldier.Position, arcProjectile.Position) < 0.32f)
                            {
                                num3 = soldier.Id;
                                flag = true;
                                break;
                            }
                        }
                    }

                    if (flag)
                    {
                        break;
                    }
                }

                arcProjectile.LastProgress = num2;
                if (arcProjectile.Landed || flag || !(num2 < 1f))
                {
                    if (arcProjectile.Kind == 1 && !arcProjectile.Landed)
                    {
                        arcProjectile.Landed = true;
                        if (!arcProjectile.RoofTopHit)
                            arcProjectile.Height = 0;
                        if (!arcProjectile.ImpactFuse)
                        {
                            if (!arcProjectile.Rethrown)
                                arcProjectile.DetonateAt = b.Time + OrdnanceExperiment.LandingFuse(b, arcProjectile.DistanceTravelled, arcProjectile.F1 ? 8f : 9f);
                            continue;
                        }

                        arcProjectile.DetonateAt = b.Time;
                    }

                    // A roof-top impact never turns into a ground-floor explosion or smoke cloud.
                    if (arcProjectile.RoofTopHit)
                    {
                        if(arcProjectile.Kind>0){float roofRadius=arcProjectile.Kind>=3?(arcProjectile.Kind==4?4.3f:3.3f):BlastRadius(arcProjectile);
                            DynamicStructures.Blast(b,arcProjectile.Position,roofRadius,arcProjectile.Kind==4?2:arcProjectile.Kind==3?1:0,true,arcProjectile.Position-arcProjectile.From);
                            b.Blasts.Add(new BlastEffect{Position=arcProjectile.Position,Height=RoofRules.Height,Born=b.Time,Until=b.Time+1.1f,Radius=roofRadius,Kind=1});b.AudioEvents.Emit(b.Time,5,arcProjectile.ShooterId);}

                        FriendlyGrenadeSafety.Exploded(b, arcProjectile);
                        b.Projectiles.RemoveAt(num);
                        continue;
                    }

                    FriendlyGrenadeSafety.Exploded(b, arcProjectile);
                    SilentOrders.Core.Soldier shooter = b.Soldiers[arcProjectile.ShooterId];
                    if (arcProjectile.Kind > 0)
                        b.AudioEvents.Emit(b.Time, 5, shooter.Id);
                    if (arcProjectile.Kind == 0)
                    {
                        b.Smoke.Add(new SilentOrders.Core.SmokeCloud { SourceSide = (int)shooter.Side, Position = arcProjectile.Position, Born = b.Time, Until = b.Time + 20f });
                        b.Projectiles.RemoveAt(num);
                    }
                    else if (arcProjectile.Kind >= 3)
                    {
                        b.Blasts.Add(new SilentOrders.Core.BlastEffect { Position = arcProjectile.Position, Born = b.Time, Until = b.Time + 1.1f, Radius = ((arcProjectile.Kind == 4) ? 4.3f : 3.3f), Kind = ((arcProjectile.Kind == 4) ? 2 : 0) });
                        bool denialIntact = arcProjectile.DenialCover >= 0 && b.Map.Tiles[arcProjectile.DenialCover] == Tile.LowCover;
                        SilentOrders.Core.SpecialWeapons.ResolveBlast(b, shooter, arcProjectile.Position, num3, arcProjectile.Kind == 4 ? 1 : 0, arcProjectile.From,arcProjectile.Height);
                        if (arcProjectile.DenialCover >= 0)
                            b.History.Event(b, "coverDenialResult", shooter.Id, arcProjectile.DenialEnemy, arcProjectile.From, arcProjectile.Position, denialIntact && b.Map.Tiles[arcProjectile.DenialCover] != Tile.LowCover ? 1 : 0, "cover=" + arcProjectile.DenialCover + "; intactBefore=" + denialIntact);
                        b.Projectiles.RemoveAt(num);
                    }
                    else
                    {
                        float radius = BlastRadius(arcProjectile);
                        float damageScale = arcProjectile.Kind == 1 && arcProjectile.ImpactFuse ? .9f : 1f;
                        b.Blasts.Add(new SilentOrders.Core.BlastEffect { Position = arcProjectile.Position, Born = b.Time, Until = b.Time + 1.1f, Radius = radius, Kind = 1 });
                        Resolve(b, shooter, arcProjectile.Position, radius, (HandGrenades.Type(arcProjectile) == 5 ? 40 : arcProjectile.F1 ? 48 : (arcProjectile.Kind == 2) ? 30 : 32) * damageScale, (HandGrenades.Type(arcProjectile) == 5 ? 60 : arcProjectile.F1 ? 72 : (arcProjectile.Kind == 2) ? 42 : 48) * damageScale, num3, 50f, thermobaric: false, origin: arcProjectile.From, f1: arcProjectile.F1, fragmentRange: HandGrenades.Type(arcProjectile) == 5 ? 13f : -1f, impactHeight: arcProjectile.Height);
                        b.Projectiles.RemoveAt(num);
                    }
                }
            }
        }

        public static void Resolve(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier shooter, SilentOrders.Core.V2 point, float radius, float min, float max, int direct, float directDamage, bool thermobaric, V2? origin = null, bool f1 = false, float fragmentRange = -1f, float impactHeight = 1.35f, float destructionScale = 1f)
        {
            var shielded=thermobaric?DynamicStructures.Shielded(b,point,point-(origin??shooter.Position),impactHeight):new System.Collections.Generic.List<Soldier>();var already=new System.Collections.Generic.HashSet<int>();
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                float num = SilentOrders.Core.V2.Distance(soldier.Position, point);
                if (soldier.Alive && !(num > radius) && b.Map.LineOfSight(point-(point-(origin??shooter.Position)).Normalized*.03f, soldier.Position))
                {
                    float num2 = 1f - num / (radius + 0.5f);
                    float num3 = CombatTraining.Cover(b, soldier, soldier.Position, point);
                    float num4 = (thermobaric ? 1f : (1f - num3 * 0.7f));
                    float amount = (thermobaric ? (65f - 15f * SilentOrders.Core.Util.Clamp(num / radius, 0f, 1f)) : ((soldier.Id == direct) ? directDamage : (b.Random.Range(min, max) * num2 * num4)));
                    float morale = (thermobaric ? (0.7f * (1f - 0.3f * SilentOrders.Core.Util.Clamp(num / radius, 0f, 1f))) : (0.42f * num2 * num4));
                    already.Add(soldier.Id);soldier.LastBlast = b.Time;
                    b.Damage(soldier, shooter, amount, (thermobaric ? 0.95f : 0.65f) * num2 * num4, morale);
                    ConcussionEffects.Apply(b, soldier, num, radius);
                }
            }

            DynamicStructures.Blast(b,point,radius,thermobaric?2:0,false,point-(origin??shooter.Position),impactHeight,destructionScale);
            if(thermobaric)DynamicStructures.BehindDamage(b,shooter,point,shielded,2,already,impactHeight);
            CoverDestruction.Blast(b, point, radius, thermobaric ? CoverBlast.Rshg : CoverBlast.Grenade, point - (origin ?? shooter.Position));
            if (!thermobaric)
                Ballistics.Shrapnel(b, shooter, point, f1 ? 3 : radius == UnderbarrelRadius ? 2 : 1, radius, min, max, fragmentRange);
        }
    }
}
