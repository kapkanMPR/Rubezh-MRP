#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
using System;
using SilentOrders.Core;

namespace SilentOrders.Web
{
    public static class Bridge
    {
        private static BattleSimulation battle;
        private static SilentOrders.Rendering.BattleRenderer painter;
        private static System.Runtime.InteropServices.GCHandle pixels;
        private static int selected;
        private static bool debug;

        public static int FrameWidth()
        {
            return 1707;
        }

        public static int FrameHeight()
        {
            return 1067;
        }

        public static void Reset(int seed)
        {
            battle = new BattleSimulation(seed);
            if (painter == null)
            {
                painter = new SilentOrders.Rendering.BattleRenderer();
                pixels = System.Runtime.InteropServices.GCHandle.Alloc(
                    painter.Canvas.Pixels,
                    System.Runtime.InteropServices.GCHandleType.Pinned
                );
            }

            selected = 0;
        }

        public static int Deploy(int seed, string kit, string campaign)
        {
            BattleSimulation b = new BattleSimulation(seed);
            if (!SquadLoadout.Apply(b, kit))
            {
                return -1;
            }

            if (!string.IsNullOrEmpty(campaign) && !CampaignScenario.Apply(b, campaign))
            {
                return -2;
            }

            battle = b;
            selected = 0;
            return 1;
        }

        public static int Campaign(string spec)
        {
            return CampaignScenario.Apply(battle, spec) ? 1 : 0;
        }

        public static int Configure(string encoded)
        {
            return SquadLoadout.Apply(battle, encoded) ? 1 : 0;
        }

        public static void Priorities(int style, int maneuver)
        {
            CommanderPriorities.Request(battle, style, maneuver);
        }

        public static void Select(int id, int showDebug)
        {
            selected = Math.Max(0, Math.Min(battle.Soldiers.Count - 1, id));
            debug = showDebug != 0;
        }

        public static void Step(int count)
        {
            for (int i = 0; i < Math.Min(24, count); i++)
            {
                battle.Step();
            }
        }

        public static int Frame()
        {
            painter.Render(battle, debug, selected);
            return pixels.AddrOfPinnedObject().ToInt32();
        }

        private static string N(float n)
        {
            return n.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Q(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        }

        public static string Snapshot()
        {
            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder(
                "{\"projectileCount\":"
                    + battle.Projectiles.Count
                    + ",\"campaignLevel\":"
                    + battle.CampaignLevel
                    + ",\"campaignReserve\":"
                    + battle.CampaignReserve
                    + ",\"redTotal\":"
                    + ((battle.CampaignTotal > 0) ? battle.CampaignTotal : 5)
                    + ",\"weaponStats\":{\"akRange\":"
                    + N(battle.Config.WeaponRange)
                    + ",\"mgRange\":"
                    + N(battle.Config.WeaponRange + 4f)
                    + ",\"rpgRange\":"
                    + N(SpecialWeapons.Range(battle))
                    + ",\"akMin\":"
                    + N(battle.Config.DamageMin)
                    + ",\"akMax\":"
                    + N(battle.Config.DamageMax)
                    + ",\"mgMin\":"
                    + N(battle.Config.DamageMin * 1.14f)
                    + ",\"mgMax\":"
                    + N(battle.Config.DamageMax * 1.14f)
                    + ",\"rpgMin\":"
                    + N(34.15f)
                    + ",\"rpgMax\":"
                    + N(54.4f)
                    + "},\"width\":"
                    + 1707
                    + ",\"height\":"
                    + 1067
                    + ",\"mapWidth\":"
                    + battle.Map.Columns
                    + ",\"mapHeight\":"
                    + battle.Map.Rows
                    + ",\"seed\":"
                    + battle.Seed
                    + ",\"time\":"
                    + N(battle.Time)
                    + ",\"capture\":"
                    + N(battle.Capture)
                    + ",\"contested\":"
                    + (battle.Contested ? "true" : "false")
                    + ",\"result\":"
                    + Q(battle.Outcome.ToString())
                    + ",\"green\":"
                    + battle.Alive(Side.Green)
                    + ",\"red\":"
                    + battle.Alive(Side.Red)
                    + ",\"greenPlan\":"
                    + Q(battle.Green.Plan)
                    + ",\"redPlan\":"
                    + Q(battle.Red.Plan)
                    + ",\"shots\":"
                    + battle.Stats.Shots
                    + ",\"bounds\":"
                    + battle.Stats.Bounds
                    + ",\"reports\":"
                    + battle.Stats.Reports
                    + ",\"coverRatio\":"
                    + N(
                        battle.Stats.CoverSeconds
                            / Math.Max(0.01f, battle.Stats.CoverSeconds + battle.Stats.ExposedSeconds)
                    )
                    + ",\"greenOrder\":"
                    + Q(battle.Green.CommanderOrder)
                    + ",\"redOrder\":"
                    + Q(battle.Red.CommanderOrder)
                    + ",\"cowers\":"
                    + battle.Stats.Cowers
                    + ",\"rallies\":"
                    + battle.Stats.Rallies
                    + ",\"evacuations\":"
                    + battle.Stats.Evacuations
                    + ",\"treatments\":"
                    + battle.Stats.Treatments
                    + ",\"smokeUsed\":"
                    + battle.Stats.SmokeUsed
                    + ",\"rocketsFired\":"
                    + battle.Stats.RocketsFired
                    + ",\"stalemates\":"
                    + battle.Stats.Stalemates
                    + ",\"units\":["
            );
            foreach (Soldier soldier in battle.Soldiers)
            {
                if (soldier.Id > 0)
                {
                    stringBuilder.Append(',');
                }

                V2 v = SilentOrders.Rendering.BattleRenderer.Project(soldier.Position, battle.Map) * 1.33333337f;
                stringBuilder.Append(
                    "{\"taskStatus\":"
                        + Q(soldier.Tactical.Status)
                        + ",\"knowledge\":"
                        + Q(soldier.Tactical.Knowledge)
                        + ",\"taskBlock\":"
                        + Q(soldier.Tactical.Block)
                        + ",\"coverObject\":"
                        + soldier.Tactical.CoverId
                        + ",\"medkits\":"
                        + soldier.Medkits
                        + ",\"treatmentKind\":"
                        + soldier.TreatmentKind
                        + ",\"packSmokes\":"
                        + soldier.PackSmokes
                        + ",\"packGrenades\":"
                        + soldier.PackGrenades
                        + ",\"packUnderbarrel\":"
                        + soldier.PackUnderbarrel
                        + ",\"weaponVariant\":"
                        + soldier.WeaponVariant
                        + ",\"grenadeType\":"
                        + soldier.GrenadeType
                        + ",\"grenadeCharges\":"
                        + soldier.GrenadeCharges
                        + ",\"launcherKind\":"
                        + soldier.LauncherKind
                        + ",\"damageDealt\":"
                        + N(soldier.DamageDealt)
                        + ",\"moraleDamage\":"
                        + N(soldier.MoraleDamage)
                        + ",\"suppressionDealt\":"
                        + N(soldier.SuppressionDealt)
                        + ",\"saved\":"
                        + soldier.SavedComrades.Count
                        + ",\"helped\":"
                        + soldier.HelpActions
                        + ",\"evacuated\":"
                        + soldier.Evacuated
                        + ",\"selfTreatments\":"
                        + soldier.SelfTreatments
                        + ",\"deployed\":"
                        + (soldier.Deployed ? "true" : "false")
                        + ",\"id\":"
                        + soldier.Id
                        + ",\"name\":"
                        + Q(soldier.Name)
                        + ",\"side\":"
                        + (int)soldier.Side
                        + ",\"px\":"
                        + N(v.X)
                        + ",\"py\":"
                        + N(v.Y)
                        + ",\"hp\":"
                        + N(soldier.Health)
                        + ",\"suppression\":"
                        + N(soldier.Suppression)
                        + ",\"morale\":"
                        + N(soldier.Morale)
                        + ",\"ammo\":"
                        + soldier.Ammo
                        + ",\"reserve\":"
                        + soldier.Reserve
                        + ",\"role\":"
                        + Q(soldier.Role.ToString())
                        + ",\"supplyPack\":"
                        + (soldier.SupplyPack ? "true" : "false")
                        + ",\"tourniquets\":"
                        + soldier.Tourniquets
                        + ",\"rockets\":"
                        + soldier.Rockets
                        + ",\"smokeCharges\":"
                        + soldier.SmokeCharges
                        + ",\"magazineSize\":"
                        + soldier.MagazineSize
                        + ",\"packRockets\":"
                        + soldier.PackRockets
                        + ",\"packBoxes\":"
                        + soldier.PackBoxes
                        + ",\"packMagazines\":"
                        + soldier.PackMagazines
                        + ",\"bleeding\":"
                        + (soldier.Bleeding ? "true" : "false")
                        + ",\"crawling\":"
                        + ((soldier.Incapacitated && soldier.Moving) ? "true" : "false")
                        + ",\"incapacitated\":"
                        + (soldier.Incapacitated ? "true" : "false")
                        + ",\"rescuer\":"
                        + soldier.RescuerId
                        + ",\"patient\":"
                        + soldier.RescueTargetId
                        + ",\"leader\":"
                        + ((battle.Team(soldier).LeaderId == soldier.Id) ? "true" : "false")
                        + ",\"element\":"
                        + soldier.Element
                        + ",\"ready\":"
                        + (SquadBrain.Ready(battle, soldier, battle.Team(soldier)) ? "true" : "false")
                        + ",\"broken\":"
                        + (soldier.Broken ? "true" : "false")
                        + ",\"sheltered\":"
                        + (PositionLogic.Protected(battle, soldier, soldier.Position) ? "true" : "false")
                        + ",\"focus\":"
                        + battle.Team(soldier).FocusEnemyId
                        + ",\"order\":"
                        + Q(soldier.Order)
                        + ",\"x\":"
                        + N(soldier.Position.X)
                        + ",\"y\":"
                        + N(soldier.Position.Y)
                        + ",\"job\":"
                        + Q(soldier.Job.ToString())
                        + ",\"action\":"
                        + Q(soldier.State.ToString())
                        + ",\"reason\":"
                        + Q(
                            soldier.Reason
                                + " · "
                                + soldier.FireStatus
                                + " · "
                                + soldier.Readiness
                                + " · вижу "
                                + soldier.Visible.Count
                                + ", память/доклады "
                                + Math.Max(0, soldier.Contacts.Count - soldier.Visible.Count)
                        )
                        + ",\"contacts\":"
                        + soldier.Contacts.Count
                        + ",\"speechAt\":"
                        + N(soldier.SpeechAt)
                        + ",\"speechUrgent\":"
                        + (soldier.SpeechUrgent ? "true" : "false")
                        + ",\"speechShort\":"
                        + Q(soldier.SpeechShort)
                        + ",\"speech\":"
                        + Q((soldier.Alive && battle.Time < soldier.SpeechUntil) ? soldier.Speech : "")
                        + "}"
                );
            }

            stringBuilder.Append(
                "],\"positionCorrections\":"
                    + battle.Tactics.Corrections
                    + ",\"taskReplans\":"
                    + battle.Tactics.Replans
                    + ",\"lostSupport\":"
                    + battle.Tactics.LostSupport
                    + ",\"verifiedArrivals\":"
                    + battle.Tactics.VerifiedArrivals
                    + ",\"defensePlans\":"
                    + battle.Red.Defense.Plans
                    + ",\"defenseCompleted\":"
                    + battle.Red.Defense.Completed
                    + ",\"defenseAborted\":"
                    + battle.Red.Defense.Aborted
                    + ",\"defenseMode\":"
                    + Q(battle.Red.Defense.Mode)
                    + ",\"supportStatus\":"
                    + Q(battle.Green.SupportStatus)
                    + ",\"delayReason\":"
                    + Q(battle.Green.DelayReason)
                    + ",\"nearMisses\":"
                    + battle.Stats.NearMisses
                    + ",\"coverImpacts\":"
                    + battle.Stats.CoverImpacts
                    + ",\"supportRelocations\":"
                    + battle.Stats.SupportRelocations
                    + ",\"smokePlans\":"
                    + battle.Stats.SmokePlans
                    + ",\"style\":"
                    + (int)battle.Green.Style
                    + ",\"maneuver\":"
                    + (int)battle.Green.Intent
                    + ",\"pendingStyle\":"
                    + (int)battle.Green.PendingStyle
                    + ",\"pendingManeuver\":"
                    + (int)battle.Green.PendingIntent
                    + ",\"priorityPending\":"
                    + (battle.Green.PriorityPending ? "true" : "false")
                    + ",\"priorityStatus\":"
                    + Q(battle.Green.PriorityStatus)
                    + ",\"execution\":"
                    + Q(battle.Green.Execution)
                    + ",\"character\":"
                    + Q(battle.Green.Character.ToString())
                    + ",\"coverDestroyed\":"
                    + battle.Stats.CoverDestroyed
                    + ",\"planAborts\":"
                    + battle.Stats.PlanAborts
                    + ",\"events\":["
            );
            for (int i = Math.Max(0, battle.Events.Count - 40); i < battle.Events.Count; i++)
            {
                if (i > Math.Max(0, battle.Events.Count - 40))
                {
                    stringBuilder.Append(',');
                }

                BattleEvent battleEvent = battle.Events[i];
                stringBuilder.Append(
                    "{\"time\":"
                        + N(battleEvent.Time)
                        + ",\"side\":"
                        + (int)battleEvent.Side
                        + ",\"text\":"
                        + Q(battleEvent.Text)
                        + "}"
                );
            }

            stringBuilder.Append("]}");
            return stringBuilder.ToString();
        }
    }
}

#endif
