using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class UncoveredBoundState
    {
        public List<V2> Route;
        public V2 GroupAtStart;
        public bool Used;
        public float Until;
        public int Starts;
    }

    // Anti-deadlock release for tactical cover waits. Does not alter movement speed,
    // concussion, morale or the actual pathfinding/collision rules.
    public static class CoverWaitPolicy
    {
        public const float MaximumWait = 4.5f;
        static bool FreshContact(BattleSimulation b, Soldier s)
        {
            foreach (var c in b.Team(s).Contacts.Values)
                if (c.Confidence >= .25f && c.SeenAt <= b.Time && b.Time - c.SeenAt < b.Config.ContactLifetime)
                    return true;
            foreach (var c in s.Contacts.Values)
                if (c.Confidence >= .25f && c.SeenAt <= b.Time && b.Time - c.SeenAt < b.Config.ContactLifetime)
                    return true;
            return false;
        }

        static V2 GroupCenter(BattleSimulation b, Soldier s, out int count)
        {
            V2 sum=new V2();count=0;
            foreach(var ally in b.Soldiers)
                if(ally!=s && ally.Side==s.Side && ally.Available && ally.Deployed)
                {sum+=ally.Position;count++;}
            return count>0 ? sum/count : s.Position;
        }
        public static bool LocalBound(BattleSimulation b, Soldier s)
        {
            var q=b.Team(s);
            if(s.Side!=Side.Green || (q.Style!=CombatStyle.Balanced && q.Style!=CombatStyle.Aggressive) || !PositionLogic.ShelterEligible(b,s) || s.PriorityMovement.Transfer || s.ReloadUntil>b.Time || s.Morale<.4f || s.Suppression>.25f || b.Time-s.LastIncoming<5 || b.Time<s.ConcussionReactUntil || s.PathCursor>=s.Path.Count) return false;
            var state=s.UncoveredBound;
            if(state.Route==s.Path)
                return b.Time<state.Until && TacticalAI.SafeApproach(b,s);
            if(s.Moving || b.Time-s.LastMoved<1.5f || s.CoverWaitStartedAt<0 || b.Time-s.CoverWaitStartedAt<1.5f) return false;
            if(PositionLogic.Protected(b,s,s.Position) && PositionLogic.HasSector(b,s,s.Position)) return false;
            int count;var center=GroupCenter(b,s,out count);
            if(count==0 || state.Used && V2.Distance(center,state.GroupAtStart)<1.5f) return false;
            // One short, actual route prefix, not repeated three-cell permission along a long route.
            var route=new List<V2>();var prev=s.Position;float budget=3;
            for(int i=s.PathCursor;i<s.Path.Count && budget>.01f;i++)
            {
                var next=s.Path[i];float distance=V2.Distance(prev,next);
                if(distance>.001f && distance>budget) next=prev+(next-prev).Normalized*budget;
                if(!TacticalAI.SafeApproachTo(b,s,next)) return false;
                budget-=V2.Distance(prev,next);route.Add(next);prev=next;
            }
            if(route.Count==0 || V2.Distance(s.Position,prev)<.75f) return false;
            // This limit only applies to the unsupported fallback, not formation or explicit maneuvers.
            float nearest=1000;
            foreach(var ally in b.Soldiers)
                if(ally!=s && ally.Side==s.Side && ally.Available && ally.Deployed)
                    nearest=Math.Min(nearest,V2.Distance(prev,ally.Position));
            if(nearest>8 || V2.Distance(prev,center)>V2.Distance(s.Position,center)+2.5f) return false;
            s.Path=route;s.PathCursor=0;s.Destination=prev;
            state.Route=route;state.GroupAtStart=center;state.Used=true;state.Until=b.Time+10;state.Starts++;
            s.MoveAuthorized=true;s.ManeuverPaused=false;
            s.Reason="Прямой угрозы нет: короткая перебежка, затем оценка группы";
            b.Diagnostics.Add(b,s,"replan","UncoveredBound",s.Reason);
            return true;
        }

        public static bool Allow(BattleSimulation b, Soldier s)
        {
            if (s == null || !s.Alive || s.Incapacitated || s.Broken || !s.Deployed || s.PathCursor >= s.Path.Count)
            {
                if (s != null)
                    s.CoverWaitStartedAt = -1f;
                return false;
            }

            if (LocalBound(b,s)) return true;
            if (s.CoverWaitStartedAt < 0f)
                s.CoverWaitStartedAt = b.Time;
            // If no enemy is known and there is no recent incoming fire, waiting for
            // a buddy's confirmation is pointless even if buddy belongs to another pair.
            bool noThreat = !FreshContact(b, s) && b.Time - s.LastIncoming > 2.5f;
            bool expired = b.Time - s.CoverWaitStartedAt >= MaximumWait;
            if (!noThreat && (s.Side==Side.Green && (b.Team(s).Style==CombatStyle.Balanced || b.Team(s).Style==CombatStyle.Aggressive) && !s.PriorityMovement.Transfer || !expired))
                return false;
            // Waiting cannot substitute for fire support. Recheck the next route segment
            // against known enemies before allowing the timeout fallback.
            if (!TacticalAI.SafeApproach(b, s))
                return false;
            // An expired coordination timer is not immunity to bullets or injuries.
            if (s.Suppression > .7f || s.Morale < .26f || b.Time < s.ConcussionReactUntil || s.ReloadUntil > b.Time || b.Time - s.LastIncoming < 1.3f)
                return false;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Reason = noThreat ? "Нет контакта: продолжаю движение без ожидания прикрытия" : "Ближайший участок безопасен: продолжаю движение";
            return true;
        }
    }
}
