using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CombatCoordination
    {
        public static bool Pressuring(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.Contact c)
        {
            if (c == null || !c.Visual || b.Time - c.SeenAt > 6f || b.Time - c.FireSeenAt > 4f)
            {
                return false;
            }

            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == s.Side && soldier.Alive && b.Time - soldier.LastIncoming < 4f && SilentOrders.Core.V2.Distance(soldier.Position, c.Position) < b.Config.WeaponRange + 5f && b.Map.LineOfSight(soldier.Position, c.Position))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CanSuppress(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (!s.Available || s.Broken || s.Moving || s.Role == SilentOrders.Core.Role.Medic || s.Sniper || s.Ammo < 3 || (float)(s.Ammo + s.Reserve) < (float)s.MagazineSize * 0.25f)
            {
                return false;
            }

            SilentOrders.Core.Squad squad = b.Team(s);
            if (!squad.SupportIds.Contains(s.Id) && s.Job != SilentOrders.Core.Job.Cover && s.Job != SilentOrders.Core.Job.Hold && s.Job != SilentOrders.Core.Job.Command && s.Role != SilentOrders.Core.Role.MachineGunner)
            {
                return false;
            }

            if (squad.Style != SilentOrders.Core.CombatStyle.Passive)
            {
                return true;
            }

            return s.Role == SilentOrders.Core.Role.MachineGunner && (squad.Phase == SilentOrders.Core.ManeuverPhase.Move || squad.EnvelopmentActive);
        }

        public static void ChooseFireMode(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.Contact target)
        {
            if (target == null || !CanSuppress(b, s))
            {
                s.SuppressiveFire = false;
                return;
            }

            if (b.Time >= s.NextSuppressDecision)
            {
                SilentOrders.Core.Squad squad = b.Team(s);
                float num = ((squad.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 0.8f : ((squad.Style == SilentOrders.Core.CombatStyle.Passive) ? 0.06f : ((squad.Style == SilentOrders.Core.CombatStyle.Withdraw) ? 0.66f : 0.43f)));
                if (s.Role == SilentOrders.Core.Role.MachineGunner)
                {
                    num += 0.32f;
                }

                if (squad.Side == SilentOrders.Core.Side.Red && squad.Defense.Active && squad.SupportIds.Contains(s.Id))
                {
                    num += 0.22f;
                }

                if (squad.EnvelopmentActive && squad.SupportIds.Contains(s.Id))
                {
                    num += 0.12f;
                }

                if (s.Role == SilentOrders.Core.Role.MachineGunner && Pressuring(b, s, target))
                {
                    num += 0.2f;
                }

                s.SuppressUntil = ((b.Random.Value() < num) ? (b.Time + ((s.Role != SilentOrders.Core.Role.MachineGunner) ? ((squad.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 3.5f : 1.8f) : ((squad.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 4.2f : 2.8f))) : 0f);
                s.NextSuppressDecision = b.Time + ((s.Role == SilentOrders.Core.Role.MachineGunner) ? 3.5f : 4.5f);
            }

            s.SuppressiveFire = b.Time < s.SuppressUntil;
            if (!s.Visible.Contains(target.EnemyId) && !s.SuppressiveFire)
            {
                s.NextShot = System.Math.Max(s.NextShot, b.Time + 0.15f);
            }
        }

        public static bool Effective(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.Contact contact)
        {
            return s.Available && !s.Broken && !s.Moving && s.Ammo >= 3 && s.ReloadUntil <= b.Time && s.Suppression < 0.72f && b.Time - contact.SeenAt < 8f && SilentOrders.Core.V2.Distance(s.Position, contact.Position) < b.WeaponRange(s) && b.Map.LineOfSight(s.Position, contact.Position) && b.SmokeTransmission(s.Position, contact.Position) > 0.25f && !b.FriendlyInLane(s, contact.Position);
        }

        public static bool WantsReload(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (ConsolidationService.WantsReload(b, s))
                return true;
            if (!s.Available || s.Broken || s.Moving || s.Ammo <= 0 || (float)s.Ammo > (float)s.MagazineSize * 0.22f || s.Reserve <= 0 || s.ReloadUntil > 0f || b.Time < s.NextTacticalReload || !PositionLogic.Protected(b, s, s.Position) || b.Time - s.LastIncoming < 3f)
            {
                return false;
            }

            // A support weapon with 1–2 rounds cannot suppress (minimum burst = 3).
            // In protected quiet conditions it must not wait forever for another
            // covering weapon before replenishing its nearly empty magazine.
            if (s.Ammo < 3 && b.Time - s.LastIncoming >= 6f)
                return true;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == s.Side && soldier.Id != s.Id && soldier.Available && !soldier.Broken && !soldier.Moving && (float)soldier.Ammo > (float)soldier.MagazineSize * 0.25f && soldier.ReloadUntil <= b.Time && SilentOrders.Core.V2.Distance(soldier.Position, s.Position) < 8f && b.Map.NearCover(soldier.Position))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Assess(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            bool flag = false;
            bool flag2 = false;
            foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
            {
                if (!(b.Time - value.SeenAt < 2.5f) || !value.Visual)
                {
                    continue;
                }

                bool flag3 = false;
                foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                {
                    if (q.Contains(soldier) && Effective(b, soldier, value) && b.Time - soldier.LastFire < 2f)
                    {
                        flag3 = true;
                    }
                }

                if (flag3)
                {
                    flag = true;
                    if (value.FireSeenAt > b.Time - 9f && b.Time - value.FireSeenAt > 3f)
                    {
                        flag2 = true;
                    }
                }
            }

            if (flag && flag2)
            {
                q.OpeningUntil = b.Time + 3f;
            }

            if (!q.EnvelopmentActive)
            {
                q.SupportStatus = (flag ? "Подавляет известную угрозу" : ((q.Contacts.Count > 0) ? "Наблюдение за известными секторами" : "Наблюдение, контактов нет"));
            }
        }

        public static void PauseMovers(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, bool pause)
        {
            foreach (int maneuverId in q.ManeuverIds)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[maneuverId];
                if (!soldier.Alive)
                {
                    continue;
                }

                if (pause && !soldier.ManeuverPaused && soldier.Available && b.Map.NearCover(soldier.Position) && !SilentOrders.Core.TacticalAI.SafeApproach(b, soldier))
                {
                    soldier.SavedManeuverAnchor = soldier.OrderAnchor;
                    soldier.ManeuverPaused = true;
                    SilentOrders.Core.TacticalAI.Stop(soldier);
                    soldier.Job = SilentOrders.Core.Job.Cover;
                    soldier.OrderAnchor = soldier.Position;
                    soldier.MoveAuthorized = false;
                }

                if (!pause && soldier.ManeuverPaused)
                {
                    soldier.ManeuverPaused = false;
                    if (soldier.Available)
                    {
                        SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier, SilentOrders.Core.Job.Reposition, soldier.SavedManeuverAnchor, q.Phase == SilentOrders.Core.ManeuverPhase.Move);
                    }
                }
            }
        }
    }
}
