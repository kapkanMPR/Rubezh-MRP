using System;
using System.Collections.Generic;
using System.IO;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class BehaviorPlanLibrary
    {
        public int version = 1;
        public bool planning;
        public string selected;
        public List<BehaviorPlan> plans = new List<BehaviorPlan>();
    }

    public sealed partial class RubezhApp
    {
        BehaviorPlanLibrary behaviorLibrary;
        BehaviorPlan behaviorDraft;
        BehaviorPlanRunner behaviorRunner;
        BattleSimulation behaviorBattle;
        Vector2 behaviorScroll;
        Vector2 behaviorSidebarScroll;
        Action behaviorPendingNavigation;
        string behaviorCleanJson;
        int behaviorRuleIndex = -1;
        string behaviorMessage = "";
        string[] behaviorChoiceIds,
            behaviorChoiceNames;
        Action<string> behaviorChoiceApply;
        Vector2 behaviorChoiceScroll;

        static readonly string[] BehaviorEvents =
        {
            "wounded",
            "flanked",
            "cluster",
            "sniper",
            "suppressed",
            "lowammo",
            "contact",
            "clear",
            "travel",
        };
        static readonly string[] BehaviorEventNames =
        {
            "Есть раненый",
            "Угроза с фланга",
            "Скопление врагов",
            "Замечен снайпер",
            "Бойца подавили",
            "Мало боеприпасов",
            "Вступили в бой",
            "Вышли из боя",
            "До первого контакта",
        };
        static readonly string[] BehaviorActions =
        {
            "advance",
            "hold",
            "left",
            "right",
            "auto",
            "smoke",
            "grenade",
            "heavy",
            "rescue",
            "resupply",
            "retreat",
            "suppress",
            "wait",
        };
        static readonly string[] BehaviorActionNames =
        {
            "Продвигаться",
            "Удерживать позицию",
            "Левый фланг",
            "Правый фланг",
            "Базовый манёвр",
            "Запросить дым",
            "Граната / подствольник",
            "Гранатомёт",
            "Помочь раненым",
            "Передать боеприпасы",
            "Отходить",
            "Подавлять огнём",
            "Ждать на позиции",
        };
        static readonly string[] BehaviorActors =
        {
            "group",
            "commander",
            "rifle",
            "gunner",
            "launcher",
            "medic",
            "sniper",
        };
        static readonly string[] BehaviorActorNames =
        {
            "Группа",
            "Командир",
            "Стрелок",
            "Пулемётчик",
            "Гранатомётчик",
            "Медик",
            "Снайпер",
        };
        string BehaviorPath
        {
            get { return Path.Combine(Application.persistentDataPath, "behavior-plans.json"); }
        }

        void LoadBehaviorPlans()
        {
            if (behaviorLibrary != null)
                return;
            try
            {
                if (File.Exists(BehaviorPath))
                    behaviorLibrary = JsonUtility.FromJson<BehaviorPlanLibrary>(File.ReadAllText(BehaviorPath));
            }
            catch (Exception e)
            {
                behaviorMessage = "Не удалось прочитать планы: " + e.Message;
            }
            if (behaviorLibrary == null)
                behaviorLibrary = new BehaviorPlanLibrary();
            if (behaviorLibrary.plans == null)
                behaviorLibrary.plans = new List<BehaviorPlan>();
            behaviorLibrary.plans.RemoveAll(p => p == null);
            foreach (var p in behaviorLibrary.plans)
                p.Normalize();
            if (behaviorLibrary.plans.Count == 0)
                behaviorLibrary.plans.Add(BehaviorPlan.Default());
            if (SelectedBehaviorPlan() == null)
                behaviorLibrary.selected = behaviorLibrary.plans[0].id;
        }

        BehaviorPlan SelectedBehaviorPlan()
        {
            return behaviorLibrary.plans.Find(p => p.id == behaviorLibrary.selected);
        }

        bool SaveBehaviorPlans()
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string temp = BehaviorPath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(behaviorLibrary, true));
                if (File.Exists(BehaviorPath))
                    File.Replace(temp, BehaviorPath, BehaviorPath + ".bak");
                else
                    File.Move(temp, BehaviorPath);
                behaviorMessage = "Сохранено на устройстве";
                return true;
            }
            catch (Exception e)
            {
                behaviorMessage = "Сохранение не удалось: " + e.Message;
                return false;
            }
        }

        void BehaviorBeforeStep()
        {
            if (WatchingInteresting || battle == null)
                return;
            if (!ReferenceEquals(behaviorBattle, battle))
            {
                LoadBehaviorPlans();
                behaviorBattle = battle;
                behaviorRunner = null;
                var selected = SelectedBehaviorPlan();
                if (behaviorLibrary.planning && selected != null)
                    behaviorRunner = new BehaviorPlanRunner(
                        JsonUtility.FromJson<BehaviorPlan>(JsonUtility.ToJson(selected))
                    );
            }
            if (behaviorRunner != null)
                behaviorRunner.Tick(battle);
        }

        bool BehaviorDirty
        {
            get { return behaviorDraft != null && JsonUtility.ToJson(behaviorDraft) != behaviorCleanJson; }
        }

        void OpenBehaviorDraft(BehaviorPlan plan)
        {
            behaviorDraft = JsonUtility.FromJson<BehaviorPlan>(JsonUtility.ToJson(plan));
            behaviorCleanJson = JsonUtility.ToJson(behaviorDraft);
            behaviorRuleIndex = -1;
            behaviorScroll = Vector2.zero;
        }

        void NavigateBehavior(Action action)
        {
            if (BehaviorDirty)
                behaviorPendingNavigation = action;
            else
                action();
        }

        bool CommitBehaviorDraft()
        {
            if (behaviorDraft == null)
                return SaveBehaviorPlans();
            behaviorDraft.Normalize();
            var old = behaviorLibrary.plans.Find(p => p.id == behaviorDraft.id);
            var previous = behaviorLibrary.selected;
            int index = old == null ? behaviorLibrary.plans.Count : behaviorLibrary.plans.IndexOf(old);
            var saved = JsonUtility.FromJson<BehaviorPlan>(JsonUtility.ToJson(behaviorDraft));
            if (old == null)
                behaviorLibrary.plans.Add(saved);
            else
                behaviorLibrary.plans[index] = saved;
            behaviorLibrary.selected = saved.id;
            if (SaveBehaviorPlans())
            {
                behaviorCleanJson = JsonUtility.ToJson(behaviorDraft);
                return true;
            }
            if (old == null)
                behaviorLibrary.plans.RemoveAt(index);
            else
                behaviorLibrary.plans[index] = old;
            behaviorLibrary.selected = previous;
            return false;
        }

        static string BehaviorName(string value, string[] ids, string[] names)
        {
            int index = Array.IndexOf(ids, value);
            return index >= 0 ? names[index] : value;
        }

        void ChooseBehavior(string[] ids, string[] names, Action<string> apply)
        {
            behaviorChoiceIds = ids;
            behaviorChoiceNames = names;
            behaviorChoiceApply = apply;
            behaviorChoiceScroll = Vector2.zero;
        }

        void AddBehaviorBlock()
        {
            if (behaviorDraft == null || behaviorDraft.rules.Count >= 40)
                return;
            ChooseBehavior(
                BehaviorEvents,
                BehaviorEventNames,
                value =>
                {
                    behaviorDraft.rules.Add(
                        new BehaviorRule
                        {
                            when = value,
                            actions = new List<BehaviorAction> { new BehaviorAction() },
                        }
                    );
                    behaviorRuleIndex = behaviorDraft.rules.Count - 1;
                    behaviorScroll = Vector2.zero;
                    behaviorSidebarScroll = new Vector2(0, 100000);
                }
            );
        }
        // Retained UI implementation lives in NativeFormationMenu.cs.
    }
}
