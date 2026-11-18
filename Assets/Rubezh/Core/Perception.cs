using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Perception
    {
        public static void Observe(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            var reactionLook = s.Awareness.Initialized ? s.Awareness.Look : s.Facing;
            s.Visible.Clear();
            if (!s.Deployed)
                return;
            DirectionalVision.Update(b, s);
            DefenderRest.ObserveSmoke(b, s);
            bool flag = s.Contacts.Count == 0;
            float sightFront = DirectionalVision.FrontRange(b, s);
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == s.Side || !soldier.Deployed)
                {
                    continue;
                }

                float num = SilentOrders.Core.V2.Distance(s.Position, soldier.Position);
                if (soldier.Alive)
                    DirectionalVision.Hear(b, s, soldier.Position, soldier.LastFire);
                if (DirectionalVision.InView(b, s, soldier.Position, num, sightFront) && b.Map.LineOfSight(s.Position, soldier.Position) && b.SmokeTransmission(s.Position, soldier.Position) >= .25f)
                {
                    if (!soldier.Alive)
                    {
                        s.Contacts.Remove(soldier.Id);
                    }
                    else if (!soldier.Crouched || !(b.Time - soldier.LastFire > (soldier.Broken ? 0.5f : 2f)) || !(num > (soldier.Broken ? 4f : 10f)) || !(b.Map.Protection(soldier.Position, s.Position) > 0.5f))
                    {
                        s.Visible.Add(soldier.Id);
                        DefenderRest.Confirm(b, s, "visual_contact");
                        ReactionTiming.Observe(b, s, soldier.Id, soldier.Position, reactionLook);
                        s.Contacts.TryGetValue(soldier.Id, out var value);
                        s.Contacts[soldier.Id] = new SilentOrders.Core.Contact
                        {
                            EnemyId = soldier.Id,
                            TargetPriority = SilentOrders.Core.SniperBrain.Priority(b, soldier),
                            Position = soldier.Position,
                            ObservedFacing = soldier.Facing,
                            ObservedWeaponRange = b.WeaponRange(soldier),
                            ObservedVelocity = SilentOrders.Core.Ballistics.ObservedVelocity(b, value, soldier.Position),
                            SeenAt = b.Time,
                            Confidence = 1f,
                            Visual = true,
                            FireSeenAt = ((b.Time - soldier.LastFire < 0.8f) ? b.Time : (value?.FireSeenAt ?? (-100f)))
                        };
                    }
                }
            }

            System.Collections.Generic.List<int> list = new System.Collections.Generic.List<int>();
            foreach (System.Collections.Generic.KeyValuePair<int, SilentOrders.Core.Contact> contact in s.Contacts)
            {
                if (b.Time - contact.Value.SeenAt > b.Config.ContactLifetime)
                {
                    list.Add(contact.Key);
                }
            }

            foreach (int item in list)
            {
                s.Contacts.Remove(item);
            }

            if (s.Visible.Count <= 0 || !(b.Time - s.LastRadio > 1.5f))
            {
                return;
            }

            foreach (int item2 in s.Visible)
            {
                b.Radio.Add(new SilentOrders.Core.RadioReport { Sender = s.Id, Side = s.Side, Contact = s.Contacts[item2].Copy(), DeliverAt = b.Time + b.Config.RadioDelay });
            }

            s.LastRadio = b.Time;
            if (flag)
            {
                SilentOrders.Core.Dialogue.Say(b, s, 0, urgent: true);
                b.Log(s.Side, s.Name + ": обнаружен противник.");
            }
        }
    }
}
