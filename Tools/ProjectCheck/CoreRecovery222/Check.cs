using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;

public static class CoreRecovery222Check
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static BattleSimulation Fixture()
    {
        var b=new BattleSimulation(222);
        Array.Clear(b.Map.Tiles,0,b.Map.Tiles.Length);
        b.Map.DynamicBuildings=null;b.Map.ProceduralCells=null;b.Map.Revision++;
        b.Time=20;b.Config.ContactLifetime=17;
        b.Green.Style=CombatStyle.Balanced;
        b.Green.Contacts.Clear();b.Red.Contacts.Clear();
        for(int i=0;i<b.Soldiers.Count;i++)
        {
            var a=b.Soldiers[i];a.Contacts.Clear();a.Visible.Clear();a.Health=100;a.Morale=1;
            a.LastIncoming=-100;a.LastMoved=0;a.Suppression=0;a.ConcussionReactUntil=0;
            a.ReloadUntil=0;a.Moving=false;a.MoveAuthorized=false;a.Deployed=true;
            a.Position=i<5?new V2(10.5f+i*.3f,10.5f):new V2(50,30);
            a.Path.Clear();a.PathCursor=0;
        }
        var s=b.Soldiers[0];s.Path=new List<V2>{new V2(11.5f,10.5f),new V2(18.5f,10.5f)};
        s.Destination=s.Path.Last();s.OrderAnchor=s.Destination;s.CoverWaitStartedAt=18;
        return b;
    }
    static void Bounds()
    {
        var b=Fixture();var s=b.Soldiers[0];var original=s.Path;
        Check(CoverWaitPolicy.LocalBound(b,s),"Unsupported safe waiting soldier can start a bounded move");
        Check(!ReferenceEquals(original,s.Path),"Fallback replaces long route with an actual bounded route");
        float length=0;var last=s.Position;foreach(var next in s.Path){length+=V2.Distance(last,next);last=next;}
        Check(length<=3.0001f&&length>=2.999f,"Unsupported fallback route is limited to three cells total");
        Check(V2.Distance(s.Destination,new V2(13.5f,10.5f))<.0001f,"Bound ends at real route prefix, not distant original goal");
        Check(s.MoveAuthorized&&!s.ManeuverPaused&&s.UncoveredBound.Starts==1,"Bound records its ownership and authorizes movement once");
        Check(CoverWaitPolicy.LocalBound(b,s)&&s.UncoveredBound.Starts==1,"Same active bound continues without starting another");
        s.PathCursor=s.Path.Count;Check(!CoverWaitPolicy.LocalBound(b,s),"Completed bound cannot continue past its actual route");
        s.Path=new List<V2>{new V2(17,10.5f)};s.PathCursor=0;
        Check(!CoverWaitPolicy.LocalBound(b,s),"New unsupported bound waits for the group to make progress");
        var independent=new Soldier();Check(!ReferenceEquals(independent.UncoveredBound,s.UncoveredBound),"Each soldier owns independent fallback state");

        Action<Soldier>[] exclude={a=>a.Broken=true,a=>a.Suppression=.3f,a=>a.Morale=.3f,a=>a.ReloadUntil=21,a=>a.LastIncoming=19,a=>a.ConcussionReactUntil=21,a=>a.Incapacitated=true,a=>a.Deployed=false,a=>a.Moving=true};
        foreach(var change in exclude){b=Fixture();s=b.Soldiers[0];change(s);var route=s.Path;Check(!CoverWaitPolicy.LocalBound(b,s),"Unsafe or unavailable soldier cannot use fallback");Check(ReferenceEquals(route,s.Path),"Rejected fallback preserves the pending route");}
        foreach(var style in new[]{CombatStyle.Passive,CombatStyle.Withdraw}){b=Fixture();b.Green.Style=style;Check(!CoverWaitPolicy.LocalBound(b,b.Soldiers[0]),"Fallback is restricted to its original command styles");}
        b=Fixture();s=b.Soldiers[0];s.CoverWaitStartedAt=19;Check(!CoverWaitPolicy.LocalBound(b,s),"Fallback respects initial coordination wait");
    }
    static void Contacts()
    {
        foreach(bool team in new[]{false,true})
        {
            var b=Fixture();var s=b.Soldiers[0];
            var c=new Contact{EnemyId=5,Position=new V2(12.5f,12.5f),SeenAt=8,Confidence=1,ObservedWeaponRange=30};
            (team?b.Green.Contacts:s.Contacts)[5]=c;
            Check(!TacticalAI.SafeApproach(b,s),"A 12-second-old contact remains a route threat with lifetime17");
            Check(!TacticalAI.SafeTravel(b,s),"Whole-route safety uses configured contact lifetime");
            b.Config.ContactLifetime=5;
            Check(TacticalAI.SafeApproach(b,s)&&TacticalAI.SafeTravel(b,s),"A custom short lifetime expires the same contact");
            b.Config.ContactLifetime=17;c.SeenAt=21;
            Check(TacticalAI.SafeApproach(b,s)&&TacticalAI.SafeTravel(b,s),"Future-dated contacts cannot invent a current route threat");
            c.SeenAt=20;c.Confidence=.1f;
            Check(TacticalAI.SafeApproach(b,s)&&TacticalAI.SafeTravel(b,s),"Low confidence does not create a route veto");
        }
        foreach(bool team in new[]{false,true})
        {
            var b=Fixture();b.Green.Style=CombatStyle.Passive;var s=b.Soldiers[0];s.CoverWaitStartedAt=19;
            var c=new Contact{EnemyId=5,Position=new V2(55,30),SeenAt=8,Confidence=1,ObservedWeaponRange=2};
            (team?b.Green.Contacts:s.Contacts)[5]=c;
            Check(!CoverWaitPolicy.Allow(b,s),"Fresh contact prevents immediate no-threat release before coordination timeout");
            b.Config.ContactLifetime=5;
            Check(CoverWaitPolicy.Allow(b,s),"No-threat release observes the configured short lifetime");
        }
    }
    public static int Main()
    {
        Bounds();Contacts();
        Console.WriteLine("PASS CoreRecovery222: "+checks+" behavioral checks; exact original LocalBound and configured contact lifetime restored.");
        return 0;
    }
}
