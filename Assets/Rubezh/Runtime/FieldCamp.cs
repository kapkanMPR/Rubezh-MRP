using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    [Serializable]
    public class CampUnit
    {
        public int headId,
            lightId,
            familyId,
            heavyId;
        public string name,
            fighterId;
        public int role,
            weapon,
            extra,
            extra2,
            rig,
            armor;
        public int weaponRarity,
            weaponLevel;
        public bool band,
            westernWeapon,
            noWeapon;
        public string[] pockets,
            bag,
            skills;
        public float aim,
            reload;

        public Kit Base()
        {
            if (
                (role == 2 || role == 6) && extra != 0
                || role == 6 && armor == 2
                || extra == 2 && role != 3
                || role != 1 && (extra == 3 && role != 5 || extra == 4 && role != 4)
                || role == 4 && extra != 0 && extra != 1 && extra != 4
                || extra2 != 0 && (role != 4 || extra2 != 1 && extra2 != 4)
            )
                throw new ArgumentException("Снаряжение запрещено классу бойца");
            var k = new Kit
            {
                classCode = role,
                weapon = weapon,
                extra = extra,
                rig = Math.Max(0, rig),
                armor = Math.Max(0, armor),
                personalTourniquet = band,
            };
            string stem =
                role == 1 ? "optic"
                : role == 3 ? "medheal"
                : role == 6 ? "vision"
                : null;
            if (stem != null)
                for (int i = 1; i <= 3; i++)
                    if (skills.Contains(stem + i))
                        k.classTraining = i;
            return k;
        }

        public void Apply(BattleSimulation b, Soldier s)
        {
            s.WesternGun = westernWeapon;
            if (weaponRarity > 0 && !noWeapon)
                s.WeaponUpgrade = WeaponProgression.At(WeaponBalance.Index(s), weaponRarity, weaponLevel);
            s.ArmorLevel = armor;
            s.RigLevel = rig;
            s.Magazines.Reset(s, s.ReserveCapacity);
            if (noWeapon)
            {
                s.Deployed = false;
                s.Health = 0;
                s.Ammo = 0;
                s.Reserve = 0;
            }
            s.Look = new Rubezh.GreenLook
            {
                headId = headId,
                lightId = lightId,
                familyId = familyId,
                heavyId = heavyId,
            };
            Rubezh.GreenLook.Ensure(s);
            int morale = skills.Count(x => x == "morale" || x == "morale2" || x == "morale3");
            MoraleDynamics.ConfigureTraining(b, s, morale);
            s.AidSeconds = 3f - .2f * skills.Count(x => x == "aid" || x == "aid2" || x == "aid3");
            s.Name = name;
            s.CampSkills = new HashSet<string>(skills);
            s.AccuracyFactor *= 1 + aim;
            s.ReloadFactor *= 1 - reload;
            var g = pockets
                .GroupBy(x => Array.IndexOf(new[] { "smoke", "rgd", "vog", "f1", "rgn", "rgo" }, x))
                .ToArray();
            if (g.Any(x => x.Key < 0) || pockets.Length > GrenadePockets.Limit(rig))
                throw new ArgumentException("Некорректные ячейки разгрузки");
            var rounds = g.Where(x => x.Key > 0).ToArray();
            int[] t = new int[3],
                n = new int[3];
            for (int i = 0; i < Math.Min(3, rounds.Length); i++)
            {
                t[i] = rounds[i].Key;
                n[i] = rounds[i].Count();
            }

            GrenadePockets.Equip(s, t[0], n[0], t[1], n[1], t[2], n[2]);
            s.SmokeCharges = g.Where(x => x.Key == 0).Sum(x => x.Count());
            if (s.SmokeCharges > 0)
                s.PocketMask |= 1;
            if (rounds.Length > 3)
            {
                s.CampReserveType = rounds[3].Key;
                s.CampReserveRounds = rounds[3].Count();
            }

            if (extra2 != 0)
            {
                if (s.Role != Role.Grenadier || (extra2 != 1 && extra2 != 4))
                    throw new ArgumentException("Второй дополнительный слот — только гранатомёт гранатомётчика");
                s.ReserveLauncherKind = extra2 == 4 ? 1 : 0;
                LauncherLogistics.ReadyReserve(s);
            }
            foreach (var x in bag)
                switch (x)
                {
                    case "rpg":
                        s.PackRockets++;
                        break;
                    case "rshg":
                        s.PackRshg++;
                        break;
                    case "kit":
                        s.Medkits++;
                        break;
                    case "band":
                        s.Tourniquets++;
                        break;
                    case "ammo":
                        s.PackMagazines++;
                        break;
                    case "belt":
                        s.PackBoxes++;
                        break;
                    case "smoke":
                        s.PackSmokes++;
                        break;
                    case "rgd":
                        s.PackGrenades++;
                        break;
                    case "vog":
                        s.PackUnderbarrel++;
                        break;
                    case "f1":
                        s.PackF1++;
                        break;
                    case "rgn":
                        s.PackRGN++;
                        break;
                    case "rgo":
                        s.PackRGO++;
                        break;
                }
        }
    }

    [Serializable]
    public class CampOrder
    {
        public string op,
            mode,
            id,
            option;
        public int value;
        public int block,
            mission;
        public CampUnit[] units;
        public FormationSettings formation;
    }

    [Serializable]
    public class CampReceipt
    {
        public string id,
            mode,
            error,
            kind;
        public string[] fighters;
        public FormationSettings formation;
        public int block,
            mission;
        public bool win;
    }

    public sealed partial class RubezhApp
    {
        NativeCampView nativeCamp;
        Rubezh.Camp.CampStore campStore;
        CampOrder campBattle,
            formationOrder;
        bool campBusy,
            campInitializing;
        internal bool NativeCampReady => !loading && nativeCamp != null && screen == ScreenId.Camp;

        string CampKey => DeveloperBuild.Enabled ? "rb56.dev." : "rb56.game.";

        string NativeCampPath =>
            System.IO.Path.Combine(
                Application.persistentDataPath,
                DeveloperBuild.Enabled ? "rubezh-native-camp-dev.json" : "rubezh-native-camp.json"
            );

        void ShowCamp()
        {
            ReleaseSceneGraphics();
            unityTestBattle = false;
            ReleaseBattleSamples();
            BuiltinCampaign.ReleaseCached();
            if (portraits != null)
                portraits.Clear();
            ReleaseImportedPortraits();
            Rubezh.Graphics.ReleaseSceneSprites();
            if (nativeCamp != null)
            {
                screen = ScreenId.Camp;
                nativeCamp.Show();
                ConsumeCampReceipt();
                return;
            }

            if (!campInitializing)
                StartCoroutine(InitializeNativeCamp());
        }

        System.Collections.IEnumerator InitializeNativeCamp()
        {
            campInitializing = true;
            string legacy = null,
                error = null;
            Rubezh.Camp.State state = null;
            try
            {
                state = Rubezh.Camp.CampStore.Read(NativeCampPath);
            }
            catch (Exception e)
            {
                error = e.Message;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (error == null && state == null)
            {
                AndroidJavaClass bridge = null;
                try
                {
                    bridge = new AndroidJavaClass("com.rubezh.camp.CampHost");
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        bridge.CallStatic("migrate", activity, DeveloperBuild.Enabled);
                }
                catch (Exception e)
                {
                    error = "Ошибка чтения прежнего прогресса: " + e.Message;
                }
                float deadline = Time.realtimeSinceStartup + 15f;
                string raw = null;
                while (error == null && string.IsNullOrEmpty(raw) && Time.realtimeSinceStartup < deadline)
                {
                    try
                    {
                        raw = bridge.CallStatic<string>("migrationResult");
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                    }
                    if (error == null && string.IsNullOrEmpty(raw))
                        yield return null;
                }
                if (bridge != null)
                    bridge.Dispose();
                if (error == null)
                {
                    if (string.IsNullOrEmpty(raw))
                        error = "Не удалось прочитать прежний прогресс. Перезапусти игру: сохранение не изменено.";
                    else
                        try
                        {
                            if (raw == "null")
                                throw new InvalidOperationException("Не удалось открыть прежнее хранилище Android");
                            legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(raw);
                        }
                        catch (Exception e)
                        {
                            error = "Ошибка переноса прогресса: " + e.Message;
                        }
                }
            }
#endif
            if (error == null)
                try
                {
                    var asset = Resources.Load<TextAsset>("FieldCamp/catalog");
                    if (asset == null)
                        throw new InvalidOperationException("Нет каталога лагеря");
                    Rubezh.Camp.Catalog catalog;
                    try
                    {
                        catalog = Newtonsoft.Json.JsonConvert.DeserializeObject<Rubezh.Camp.Catalog>(
                            asset.text,
                            Rubezh.Camp.CampModel.JsonSettings
                        );
                    }
                    finally
                    {
                        Resources.UnloadAsset(asset);
                    }
                    if (state == null && !string.IsNullOrEmpty(legacy))
                    {
                        state = Rubezh.Camp.CampModel.Decode(legacy);
                        System.IO.File.WriteAllText(NativeCampPath + ".legacy.json", legacy);
                    }

                    campStore = new Rubezh.Camp.CampStore(NativeCampPath, new Rubezh.Camp.CampModel(catalog, state));
                    campStore.Save();
                    nativeCamp = gameObject.AddComponent<NativeCampView>();
                    nativeCamp.Initialize(campStore, DeveloperBuild.Enabled);
                    nativeCamp.StartOperation = OpenNativeCampFormation;
                    nativeCamp.StartHybridRange = OpenHybridRange;
                    nativeCamp.SetOption = ApplyCampOption;
                    nativeCamp.GetOptions = ReadNativeCampOptions;
                    nativeCamp.ResetGame = () => ResetCareer(true);
                    message = "";
                    screen = ScreenId.Camp;
                    nativeCamp.Show();
                    ConsumeCampReceipt();
                }
                catch (Exception e)
                {
                    error = e.Message;
                    if (nativeCamp != null)
                    {
                        Destroy(nativeCamp);
                        nativeCamp = null;
                    }

                    Debug.LogException(e);
                }

            campInitializing = false;
            if (error != null)
            {
                message = "Лагерь не открылся: " + error;
                Debug.LogWarning(message);
            }

            yield break;
        }

        bool editingSquadFormation;
        string formationSquadId;

        internal static bool CampFormationReady(CampOrder q)
        {
            var f = q.formation;
            return f != null
                && f.Valid()
                && (
                    f.pairing != 4
                    || f.ManualComplete()
                    || q.mode == "story" && q.mission == 6 && new[] { 1, 2, 3 }.All(i => f.pairSlots.Contains(i))
                );
        }

        void OpenNativeCampFormation(CampOrder q)
        {
            if (loading || campBusy || campBattle != null || formationOrder != null)
                throw new InvalidOperationException("Дождись завершения операции");
            bool edit = q.op == "edit-formation";
            if (campStore == null || !edit && !campStore.Model.MissionOpen(q.mode, q.block, q.mission))
                throw new InvalidOperationException("Сначала пройди предыдущую миссию.");
            if (!edit)
            {
                if (!CampFormationReady(q))
                    throw new InvalidOperationException(
                        "Проверьте построение отряда: заполните двойки или включите автоподбор."
                    );
                StartCampBattle(q);
                return;
            }
            editingSquadFormation = true;
            formationSquadId = campStore.Model.S.selectedSquad;
            formationOrder = q;
            formationPickerSlot = -1;
            devDraftMission = 0;
            CancelTouchInput();
            message = "";
            screen = ScreenId.Formation;
            nativeCamp.Hide();
        }

        void CampReceiptFor(CampOrder q, bool win, string error = null, string kind = null)
        {
            if (q.mode == "test" || q.mode == "test-reference")
                return;
            var receipt = new CampReceipt
            {
                id = Guid.NewGuid().ToString("N"),
                fighters = q
                    .units?.Where(u => u != null && !string.IsNullOrEmpty(u.fighterId) && !u.noWeapon)
                    .Select(u => u.fighterId)
                    .ToArray(),
                mode = q.mode,
                mission = q.mission,
                block = q.block,
                win = win,
                error = error,
                kind = kind,
                formation = q.formation,
            };
            PlayerPrefs.SetString(CampKey + "receipt", JsonUtility.ToJson(receipt));
            PlayerPrefs.Save();
        }

        void ConsumeCampReceipt()
        {
            string raw = PlayerPrefs.GetString(CampKey + "receipt", "");
            if (string.IsNullOrEmpty(raw) || raw == "{}")
                return;
            try
            {
                nativeCamp.Receive(JsonUtility.FromJson<CampReceipt>(raw));
                PlayerPrefs.DeleteKey(CampKey + "receipt");
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                message = "Награда ожидает сохранения: " + e.Message;
                Debug.LogException(e);
            }
        }

        void ResetCareer(bool openCamp)
        {
            if (campStore != null)
                campStore.Change(() => campStore.Model.Reset());
            foreach (var suffix in new[] { "", ".bak", ".tmp" })
                System.IO.File.Delete(SavePath + suffix);
            for (int n = 1; n <= 20; n++)
                if (DeveloperBuild.Enabled)
                    PlayerPrefs.DeleteKey(DevDraftKey(n));
            for (int n = 1; n <= 5; n++)
                PlayerPrefs.DeleteKey(CampKey + "war" + n);
            PlayerPrefs.DeleteKey(CampKey + "story1");
            for (int i = 0; i < 5; i++)
                PlayerPrefs.DeleteKey(CampKey + "look57." + i);
            progress = new Progress();
            draft = new Progress();
            legacyRead = false;
            legacyDev = null;
            devDraftMission = 0;
            devLastJson = null;
            legacyLooks.Clear();
            campBattle = formationOrder = null;
            battle = null;
            interestingSession = null;
            mission = 1;
            kitSelected = selected = 0;
            awarded = false;
            paused = false;
            campaignMode = true;
            pickOptions = null;
            pickApply = null;
            CloseDrawer();
            screen = ScreenId.Camp;
            PlayerPrefs.DeleteKey(CampKey + "receipt");
            PlayerPrefs.Save();
            WebPersistence.Flush();
            if (openCamp)
                ShowCamp();
        }

        void SaveSquadFormationAndReturn()
        {
            var f = formationOrder == null ? null : formationOrder.formation;
            if (f == null || !f.Valid() || !FormationReady(f))
            {
                message = "Заполни места в двойках или выбери автоподбор.";
                return;
            }
            try
            {
                if (campStore.Model.S.selectedSquad != formationSquadId)
                    throw new InvalidOperationException("Выбран другой отряд");
                campStore.Change(
                    () =>
                        campStore.Model.S.formation = new Rubezh.Camp.Formation
                        {
                            quietWork = f.quietWork,
                            pairing = f.pairing,
                            width = f.width,
                            depth = f.depth,
                            pairSlots = (int[])f.pairSlots.Clone(),
                            positions = (int[])f.positions.Clone(),
                        }
                );
                editingSquadFormation = false;
                formationSquadId = null;
                formationOrder = null;
                formationPickerSlot = -1;
                CancelTouchInput();
                ShowCamp();
            }
            catch (Exception e)
            {
                message = e.Message;
                Debug.LogWarning(e);
            }
        }

        void BackFromCampFormation()
        {
            if (editingSquadFormation)
            {
                SaveSquadFormationAndReturn();
                return;
            }
            var q = formationOrder;
            formationOrder = null;
            formationPickerSlot = -1;
            screen = ScreenId.Camp;
            CancelTouchInput();
            CampReceiptFor(q, false, null, "formation-back");
            ShowCamp();
        }

        void LaunchFromFormation()
        {
            if (editingSquadFormation)
            {
                SaveSquadFormationAndReturn();
                return;
            }
            if (formationOrder == null)
            {
                formationPickerSlot = -1;
                screen = ScreenId.Camp;
                CancelTouchInput();
                CloseDrawer();
                message = "Не выбран состав для выхода. Открой подготовку в лагере и выбери бой заново.";
                Debug.LogWarning(message);
                ShowCamp();
                if (nativeCamp != null)
                    nativeCamp.Notify(message);
                return;
            }

            var q = formationOrder;
            if (!FormationReady(q.formation))
            {
                message = "Заполни места в двойках или выбери автоподбор.";
                return;
            }

            try
            {
                StartCampBattle(q);
                formationOrder = null;
            }
            catch (Exception e)
            {
                campBusy = false;
                message = e.Message;
                Debug.LogWarning(e);
            }
        }

        internal static BattleSimulation BuildCampSimulation(CampOrder q, RubezhLevel preparedMap = null)
        {
            if (q.units == null || q.units.Length != 5 || q.units.Any(u => u == null) || q.units.All(u => u.noWeapon))
                throw new ArgumentException("Нет вооружённых бойцов для выхода");
            bool test = q.mode == "test" || q.mode == "test-reference";
            bool story = q.mode == "story" || (test && q.mission == 20);
            int index =
                test ? (q.mission == 20 ? 20 : 1)
                : q.mode == "raid" ? (q.mission + 1) / 2
                : story ? (test ? 20 : q.mission)
                : (q.block - 1) * 10 + q.mission;
            var sim = new BattleSimulation(8000 + 97 * index);
            if (story)
            {
                var basic = string.Join(";", Enumerable.Range(0, 5).Select(i => new Kit().Encode()));
                if (
                    !SquadLoadout.Apply(sim, basic)
                    || !CampaignScenario.Apply(sim, index + "," + (index - 1) + ",0,0,0,0,0")
                )
                    throw new InvalidOperationException("Ошибка сценария.");
            }

            if (story)
            {
                if (
                    !(
                        preparedMap != null
                            ? RubezhLevelRuntime.ApplyLevel(sim, preparedMap)
                            : RubezhLevelRuntime.Apply(sim, true, index)
                    )
                )
                    throw new InvalidOperationException(RubezhLevelRuntime.LastError);
                if (index == 6)
                {
                    sim.Soldiers[4].Deployed = false;
                    sim.Soldiers[4].Health = 0;
                }
            }
            else if (preparedMap != null)
            {
                if (!RubezhLevelRuntime.ApplyLevel(sim, preparedMap))
                    throw new InvalidOperationException(RubezhLevelRuntime.LastError);
            }
            else
            {
                var a = Resources.Load<TextAsset>("War/war-" + index.ToString("00"));
                if (a == null)
                    throw new InvalidOperationException("Нет карты " + index);
                var l = JsonUtility.FromJson<BuiltinCampaign.Map>(PackedLevel.Read(a)).Create();
                try
                {
                    if (!RubezhLevelRuntime.ApplyLevel(sim, l))
                        throw new InvalidOperationException(RubezhLevelRuntime.LastError);
                }
                finally
                {
                    Destroy(l);
                    Resources.UnloadAsset(a);
                }
            }

            if (!SquadLoadout.Apply(sim, string.Join(";", q.units.Select(u => u.Base().Encode()))))
                throw new ArgumentException("Комплект не подходит классу бойца.");
            for (int i = 0; i < 5; i++)
            {
                var u = q.units[i];
                var soldier = sim.Soldiers[i];
                u.Apply(sim, soldier);
                if (
                    soldier.Side != Side.Green
                    || soldier.Id != i
                    || soldier.Name != u.name
                    || (int)soldier.Role + 1 != u.role
                    || soldier.WeaponVariant != u.weapon
                    || !soldier.ClassKitAssigned
                )
                    throw new InvalidOperationException("Не перенесён комплект бойца: " + u.name);
            }

            WeaponProgression.Configure(sim, index, !story);
            if (q.mode == "raid")
                RaidCombat.ConfigureEnemies(sim, q.mission);
            sim.Green.Formation = q.formation.Copy();
            OperationMode.Apply(sim);
            sim.InitializeCombatRandom(test ? (q.mission == 20 ? 200020 : 200001) : NewCombatSeed());
            return sim;
        }

        void StartCampBattle(CampOrder q)
        {
            if (q.mode == "test" || q.mode == "test-reference")
            {
                OpenHybridRange();
                return;
            }
            if (q.mode == "raid")
            {
                StartRaid(q);
                return;
            }
#if UNITY_5_3_OR_NEWER
            SetCampBattleContext(q);
            BeginNativeTestLoad(q);
#else
            var sim = BuildCampSimulation(q);
            SetCampBattleContext(q);
            QueueBattleLoad(
                sim,
                () =>
                {
                    campBusy = false;
                    campBattle = q;
                    CompleteBattleStart(sim);
                }
            );
#endif
        }

        void SetCampBattleContext(CampOrder q)
        {
            if (q.mode == "test-reference")
                q.mode = "test";
            testBattle = q.mode == "test";
            unityTestBattle = true;
            if (testBattle)
            {
                testMapLabel = q.mission == 20 ? "СЮЖЕТ · УРОВЕНЬ 20" : "ЛЕС · ПРОСЕКА";
                testMapSeed = q.mission == 20 ? 200020 : 200001;
                testPreviousQuality = graphicsTier;
                testPreviousSoftware = forceSoftware;
                testPreviousVisibility = fullVisibility;
                testPreviousFog = hardcoreFog;
                graphicsTier = 3;
                forceSoftware = false;
                fullVisibility = true;
                hardcoreFog = false;
                unityTestError = null;
                diagDisableSmoke = diagDisableRoofs = diagDisableScenery = false;
                diagnosticIndex = diagnosticCount = 0;
                diagnosticHistory.Clear();
            }
            mission = q.mode == "story" ? q.mission : (q.block - 1) * 10 + q.mission;
            campaignMode = q.mode == "story";
            campBusy = true;
            nativeCamp.Hide();
        }
    }
}
