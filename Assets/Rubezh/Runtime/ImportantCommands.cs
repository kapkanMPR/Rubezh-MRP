using System;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // Presentation-only queue: never touches RNG, orders, speech timers or simulation state.
    public sealed class ImportantCommandDisplay
    {
        sealed class Bubble
        {
            public string Text = "";
            public float At = -100, Remaining;
        }

        readonly Dictionary<int, Bubble> bubbles = new Dictionary<int, Bubble>();
        public string BubbleText(Soldier s)
        {
            Bubble v;
            return bubbles.TryGetValue(s.Id, out v) && v.Remaining > 0 ? v.Text : "";
        }

        public static bool BubbleImportant(string text) => Extract(text).Length > 0 || text == "ВЫСТРЕЛ!!" || text == "СВОЯ!";
        readonly Queue<string> pending = new Queue<string>();
        BattleSimulation source;
        BattleEvent last;
        string lastOrder = "", lastText = "";
        float lastAt = -100, remaining;
        public string Text { get; private set; } = "";

        public static string Extract(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            foreach (var marker in new[]
            {
                "КОНТАКТ!!",
                "Готовность. Движение",
                "Код СТЕНА!",
                "ДАВИМ!!!",
                "Залп прошёл.",
                "Рубеж достигнут.",
                "Группа контакт! ЗАСАДА!"
            }

            )
            {
                int i = text.IndexOf(marker, StringComparison.Ordinal);
                if (i >= 0)
                    return text.Substring(i);
            }

            return "";
        }

        void Add(string text, float at)
        {
            text = Extract(text);
            if (text.Length == 0 || text == lastText && Math.Abs(at - lastAt) < .2f)
                return;
            lastText = text;
            lastAt = at;
            pending.Enqueue(text);
            Advance();
        }

        void Advance()
        {
            if (Text.Length == 0 && pending.Count > 0)
            {
                Text = pending.Dequeue();
                remaining = Math.Max(8f, Math.Min(12f, Text.Length * .08f));
            }
        }

        public void Clear()
        {
            source = null;
            last = null;
            lastOrder = lastText = "";
            lastAt = -100;
            Text = "";
            remaining = 0;
            pending.Clear();
            bubbles.Clear();
        }

        public void Observe(BattleSimulation b)
        {
            if (source != b)
            {
                Clear();
                source = b;
            }

            if (b == null)
                return;
            foreach (var soldier in b.Soldiers)
            {
                if (soldier.Side != Side.Green || !soldier.Alive || !BubbleImportant(soldier.Speech))
                    continue;
                Bubble v;
                if (!bubbles.TryGetValue(soldier.Id, out v))
                {
                    v = new Bubble();
                    bubbles.Add(soldier.Id, v);
                }

                if (v.At == soldier.SpeechAt)
                    continue;
                v.At = soldier.SpeechAt;
                v.Text = soldier.Speech;
                v.Remaining = Extract(v.Text).Length > 0 ? 8f : 4.5f;
            }

            int start = last == null ? 0 : b.Events.IndexOf(last) + 1;
            for (int i = start; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if (e.Side == Side.Green)
                    Add(e.Text, e.Time);
            }

            if (b.Events.Count > 0)
                last = b.Events[b.Events.Count - 1];
            if (lastOrder != b.Green.CommanderOrder)
            {
                lastOrder = b.Green.CommanderOrder;
                Add(lastOrder, b.Time);
            }
        }

        public void Tick(float seconds)
        {
            foreach (var v in bubbles.Values)
                v.Remaining = Math.Max(0, v.Remaining - Math.Max(0, seconds));
            if (Text.Length == 0)
                return;
            remaining -= Math.Max(0, seconds);
            if (remaining <= 0)
            {
                Text = "";
                Advance();
            }
        }
    }

    public sealed partial class RubezhApp
    {
        readonly ImportantCommandDisplay importantCommands = new ImportantCommandDisplay();
    }
}
