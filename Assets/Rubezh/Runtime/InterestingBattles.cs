using System;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class InterestingOrder
    {
        public float time;
        public int style, intent;
    }

    [Serializable]
    public sealed class InterestingExpected
    {
        public int Id, Shots, Kills, Ammo, Reserve;
        public float Health, DamageDealt;
    }

    [Serializable]
    public sealed class InterestingBattle
    {
        public int id, seed;
        public string kit, outcome, title, description;
        public int[] training;
        public FormationSettings formation;
        public InterestingOrder[] orders;
        public float duration;
        public InterestingExpected[] expected;
    }

    [Serializable]
    public sealed class InterestingArchive
    {
        public const string SupportedSourceVersion = "AI-0.12.25-test";
        public const string CompatibilityWarning = "Архив AI-0.12.25. Это пересчёт текущим ИИ, не точный повтор: историческая совместимость нарушена, исход и действия могут отличаться.";
        public string engineVersion;
        public BuiltinCampaign.Map map;
        public InterestingBattle[] battles;
        // The archived engine is not bundled. Matching a final result alone cannot prove replay parity.
        public bool HistoricalCompatibilityVerified => false;

        public void Validate()
        {
            if (engineVersion != SupportedSourceVersion || map == null || map.formatVersion != 3 || battles == null || battles.Length == 0)
                throw new InvalidOperationException("Неподдерживаемая версия или структура архива.");
            if (battles.Any(e => e == null) || battles.Select(e => e.id).Distinct().Count() != battles.Length)
                throw new InvalidOperationException("Повреждён список архивных боёв.");
            foreach (var entry in battles)
                ValidateEntry(entry);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static void ValidateEntry(InterestingBattle entry)
        {
            if (entry == null || entry.id < 0 || string.IsNullOrEmpty(entry.kit) || entry.training == null || entry.training.Length != Progress.TrainingCount || entry.training.Any(t => t < 0 || t > 3) || entry.formation == null || !entry.formation.Valid() || entry.orders == null || entry.orders.Length == 0 || entry.expected == null || entry.expected.Length == 0 || !Finite(entry.duration) || entry.duration <= 0 || !Enum.TryParse<Outcome>(entry.outcome, out var outcome) || !Enum.IsDefined(typeof(Outcome), outcome) || outcome == Outcome.Running)
                throw new InvalidOperationException("Повреждены исходные данные архивного боя.");
            float previous = -1;
            foreach (var order in entry.orders)
            {
                if (order == null || !Finite(order.time) || order.time < 0 || order.time < previous || order.style < 0 || order.style > 4 || order.intent < 0 || order.intent > 8)
                    throw new InvalidOperationException("Повреждена последовательность приказов архива.");
                previous = order.time;
            }

            if (entry.expected.Any(e => e == null || e.Id < 0 || !Finite(e.Health) || !Finite(e.DamageDealt)) || entry.expected.Select(e => e.Id).Distinct().Count() != entry.expected.Length)
                throw new InvalidOperationException("Повреждён ожидаемый результат архивного боя.");
        }

        public static InterestingArchive Load()
        {
            var asset = Resources.Load<TextAsset>("InterestingBattles");
            if (asset == null)
                throw new InvalidOperationException("Не найден архив интересных боёв.");
            var archive = JsonUtility.FromJson<InterestingArchive>(asset.text);
            if (archive == null)
                throw new InvalidOperationException("Пустой архив интересных боёв.");
            archive.Validate();
            return archive;
        }
    }

    // Re-simulation in the real game engine. Never reseed after construction: the original
    // test consumed RNG during scenario setup. No campaign overrides or career state apply.
    public sealed class InterestingSession
    {
        public readonly InterestingBattle Entry;
        public readonly BattleSimulation Battle;
        int nextOrder;
        public bool HistoricalCompatibilityVerified => false;
        public string ResultStatus => Battle.Outcome == Outcome.Running ? "Пересчёт текущим ИИ продолжается; точный исторический повтор не поддерживается." : MatchesResult() ? "Итоговые показатели совпали. Точность исторического хода боя не подтверждена." : "Пересчёт текущим ИИ отличается от архива AI-0.12.25. Исторический движок не восстановлен.";

        public InterestingSession(InterestingArchive archive, InterestingBattle entry)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            archive.Validate();
            InterestingArchive.ValidateEntry(entry);
            Entry = entry;
            Battle = new BattleSimulation(entry.seed);
            Battle.Historical01225 = true;
            if (!SquadLoadout.Apply(Battle, entry.kit) || !CampaignScenario.Apply(Battle, "20,19," + string.Join(",", entry.training)))
                throw new InvalidOperationException("Повреждён исходный состав боя.");
            var level = archive.map.Create();
            try
            {
                if (!RubezhLevelRuntime.ApplyLevel(Battle, level))
                    throw new InvalidOperationException(RubezhLevelRuntime.LastError);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(level);
            }

            Battle.Green.Formation = entry.formation.Copy();
            Battle.Diagnostics.Enabled = true;
            Battle.Outcomes.Enabled = true;
        }

        public void Step()
        {
            if (Battle.Outcome != Outcome.Running)
                return;
            while (nextOrder < Entry.orders.Length && Entry.orders[nextOrder].time <= Battle.Time + .0001f)
            {
                var order = Entry.orders[nextOrder++];
                CommanderPriorities.Request(Battle, order.style, order.intent);
            }

            Battle.Step();
        }

        public bool MatchesResult()
        {
            if (Battle.Outcome.ToString() != Entry.outcome || Math.Abs(Battle.Time - Entry.duration) > .06f)
                return false;
            foreach (var expected in Entry.expected)
            {
                var actual = Battle.Soldiers.FirstOrDefault(s => s.Id == expected.Id);
                if (actual == null || Math.Abs(actual.Health - expected.Health) > .02f || actual.Shots != expected.Shots || actual.Kills != expected.Kills || actual.Ammo != expected.Ammo || actual.Reserve != expected.Reserve || Math.Abs(actual.DamageDealt - expected.DamageDealt) > .05f)
                    return false;
            }

            return true;
        }
    }

    public sealed partial class RubezhApp
    {
        InterestingArchive interestingArchive;
        InterestingSession interestingSession;
        bool WatchingInteresting
        {
            get
            {
                return interestingSession != null && battle == interestingSession.Battle;
            }
        }



        void StartInteresting(InterestingBattle entry)
        {
            if (!DeveloperBuild.InterestingAvailable)
                return;
            try
            {
                if (interestingArchive == null)
                    interestingArchive = InterestingArchive.Load();
                var session = new InterestingSession(interestingArchive, entry);
                // Replays share native presentation but never adopt live-operation rewards.
                testBattle=false;campBattle=null;
#if UNITY_5_3_OR_NEWER
                unityTestBattle=true;
#endif
                QueueBattleLoad(session.Battle, () =>
                {
                    CancelTouchInput();
                    interestingSession = session;
                    battle = session.Battle;
                    battle.AudioEvents.Enabled = true;
                    campaignMode = false;
                    mission = 20;
                    screen = 2;
                    paused = false;
                    awarded = true;
                    accumulator = 0;
                    selected = 0;
                    speed = 1;
                    drawer = "";
                    pickOptions = null;
                    pickApply = null;
                    zoom = 1;
                    pan = Vector2.zero;
                    style = entry.orders[0].style;
                    maneuver = entry.orders[0].intent;
                    message = "";
                    nextFrame = 0;
                    renderedBattle = null;
                    newlyUnlocked = new EquipmentUnlock[0];
                    unlockScroll = Vector2.zero;
                });
            }
            catch (Exception ex)
            {
                message = "Не удалось открыть бой: " + ex.Message;
            }
        }
    }
}
