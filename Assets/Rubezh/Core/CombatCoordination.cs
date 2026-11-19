using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CombatCoordination
    {
        public static bool Pressuring(BattleSimulation b, Soldier s, Contact c)
        {
            if (c == null || !c.Visual || b.Time - c.SeenAt > 6f || b.Time - c.FireSeenAt > 4f)
            {
                return false;
            }

            foreach (Soldier soldier in b.Soldiers)
            {
                if (
                    soldier.Side == s.Side
                    && soldier.Alive
                    && b.Time - soldier.LastIncoming < 4f
                    && V2.Distance(soldier.Position, c.Position) < b.Config.WeaponRange + 5f
                    && b.Map.LineOfSight(soldier.Position, c.Position)
                )
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CanSuppress(BattleSimulation b, Soldier s)
        {
            if (
                !s.Available
                || s.Broken
                || s.Moving
                || s.Role == Role.Medic
                || s.Sniper
                || s.Ammo < 3
                || (float)(s.Ammo + s.Reserve) < (float)s.MagazineSize * 0.25f
            )
            {
                return false;
            }

            Squad squad = b.Team(s);
            if (
                !squad.SupportIds.Contains(s.Id)
                && s.Job != Job.Cover
                && s.Job != Job.Hold
                && s.Job != Job.Command
                && s.Role != Role.MachineGunner
            )
            {
                return false;
            }

            if (squad.Style != CombatStyle.Passive)
            {
                return true;
            }

            return s.Role == Role.MachineGunner && (squad.Phase == ManeuverPhase.Move || squad.EnvelopmentActive);
        }

        public static void ChooseFireMode(BattleSimulation b, Soldier s, Contact target)
        {
            if (target == null || !CanSuppress(b, s))
            {
                s.SuppressiveFire = false;
                return;
            }

            if (b.Time >= s.NextSuppressDecision)
            {
                Squad squad = b.Team(s);
                float suppressChance = (
                    (squad.Style == CombatStyle.Aggressive)
                        ? 0.8f
                        : (
                            (squad.Style == CombatStyle.Passive)
                                ? 0.06f
                                : ((squad.Style == CombatStyle.Withdraw) ? 0.66f : 0.43f)
                        )
                );
                if (s.Role == Role.MachineGunner)
                {
                    suppressChance += 0.32f;
                }

                if (squad.Side == Side.Red && squad.Defense.Active && squad.SupportIds.Contains(s.Id))
                {
                    suppressChance += 0.22f;
                }

                if (squad.EnvelopmentActive && squad.SupportIds.Contains(s.Id))
                {
                    suppressChance += 0.12f;
                }

                if (s.Role == Role.MachineGunner && Pressuring(b, s, target))
                {
                    suppressChance += 0.2f;
                }

                s.SuppressUntil = (
                    (b.Random.Value() < suppressChance)
                        ? (
                            b.Time
                            + (
                                (s.Role != Role.MachineGunner)
                                    ? ((squad.Style == CombatStyle.Aggressive) ? 3.5f : 1.8f)
                                    : ((squad.Style == CombatStyle.Aggressive) ? 4.2f : 2.8f)
                            )
                        )
                        : 0f
                );
                s.NextSuppressDecision = b.Time + ((s.Role == Role.MachineGunner) ? 3.5f : 4.5f);
            }

            s.SuppressiveFire = b.Time < s.SuppressUntil;
            if (!s.Visible.Contains(target.EnemyId) && !s.SuppressiveFire)
            {
                s.NextShot = Math.Max(s.NextShot, b.Time + 0.15f);
            }
        }

        public static bool Effective(BattleSimulation b, Soldier s, Contact contact)
        {
            return s.Available
                && !s.Broken
                && !s.Moving
                && s.Ammo >= 3
                && s.ReloadUntil <= b.Time
                && s.Suppression < 0.72f
                && b.Time - contact.SeenAt < 8f
                && V2.Distance(s.Position, contact.Position) < b.WeaponRange(s)
                && b.Map.LineOfSight(s.Position, contact.Position)
                && b.SmokeTransmission(s.Position, contact.Position) > 0.25f
                && !b.FriendlyInLane(s, contact.Position);
        }

        public static bool WantsReload(BattleSimulation b, Soldier s)
        {
            if (ConsolidationService.WantsReload(b, s))
                return true;
            if (
                !s.Available
                || s.Broken
                || s.Moving
                || s.Ammo <= 0
                || (float)s.Ammo > (float)s.MagazineSize * 0.22f
                || s.Reserve <= 0
                || s.ReloadUntil > 0f
                || b.Time < s.NextTacticalReload
                || !PositionLogic.Protected(b, s, s.Position)
                || b.Time - s.LastIncoming < 3f
            )
            {
                return false;
            }

            // A support weapon with 1–2 rounds cannot suppress (minimum burst = 3).
            // In protected quiet conditions it must not wait forever for another
            // covering weapon before replenishing its nearly empty magazine.
            if (s.Ammo < 3 && b.Time - s.LastIncoming >= 6f)
                return true;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (
                    soldier.Side == s.Side
                    && soldier.Id != s.Id
                    && soldier.Available
                    && !soldier.Broken
                    && !soldier.Moving
                    && (float)soldier.Ammo > (float)soldier.MagazineSize * 0.25f
                    && soldier.ReloadUntil <= b.Time
                    && V2.Distance(soldier.Position, s.Position) < 8f
                    && b.Map.NearCover(soldier.Position)
                )
                {
                    return true;
                }
            }

            return false;
        }

        public static void Assess(BattleSimulation b, Squad q)
        {
            bool hasShooter = false;
            bool hasOpeningAfterEnemyFire = false;
            foreach (Contact value in q.Contacts.Values)
            {
                if (!(b.Time - value.SeenAt < 2.5f) || !value.Visual)
                {
                    continue;
                }

                bool coversContact = false;
                foreach (Soldier soldier in b.Soldiers)
                {
                    if (q.Contains(soldier) && Effective(b, soldier, value) && b.Time - soldier.LastFire < 2f)
                    {
                        coversContact = true;
                    }
                }

                if (coversContact)
                {
                    hasShooter = true;
                    if (value.FireSeenAt > b.Time - 9f && b.Time - value.FireSeenAt > 3f)
                    {
                        hasOpeningAfterEnemyFire = true;
                    }
                }
            }

            if (hasShooter && hasOpeningAfterEnemyFire)
            {
                q.OpeningUntil = b.Time + 3f;
            }

            if (!q.EnvelopmentActive)
            {
                q.SupportStatus = (
                    hasShooter
                        ? "Подавляет известную угрозу"
                        : ((q.Contacts.Count > 0) ? "Наблюдение за известными секторами" : "Наблюдение, контактов нет")
                );
            }
        }

        public static void PauseMovers(BattleSimulation b, Squad q, bool pause)
        {
            foreach (int maneuverId in q.ManeuverIds)
            {
                Soldier soldier = b.Soldiers[maneuverId];
                if (!soldier.Alive)
                {
                    continue;
                }

                if (
                    pause
                    && !soldier.ManeuverPaused
                    && soldier.Available
                    && b.Map.NearCover(soldier.Position)
                    && !TacticalAI.SafeApproach(b, soldier)
                )
                {
                    soldier.SavedManeuverAnchor = soldier.OrderAnchor;
                    soldier.ManeuverPaused = true;
                    TacticalAI.Stop(soldier);
                    soldier.Job = Job.Cover;
                    soldier.OrderAnchor = soldier.Position;
                    soldier.MoveAuthorized = false;
                }

                if (!pause && soldier.ManeuverPaused)
                {
                    soldier.ManeuverPaused = false;
                    if (soldier.Available)
                    {
                        SquadBrain.GiveOrder(
                            b,
                            q,
                            soldier,
                            Job.Reposition,
                            soldier.SavedManeuverAnchor,
                            q.Phase == ManeuverPhase.Move
                        );
                    }
                }
            }
        }
    }
}
