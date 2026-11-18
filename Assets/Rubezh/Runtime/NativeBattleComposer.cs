using System;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;

namespace Rubezh.UnityPort
{
    // Native orchestration owns persistent actors. Shared recipes only describe artwork;
    // there is no software renderer or intermediate command replay on this path.
    public sealed partial class NativeBattleComposer : BattleVisualRecipes
    {
        sealed class ActorState { public readonly int[] Values=new int[64]; public int Count; public Soldier Soldier; public int Status=int.MinValue, Underlay=int.MinValue; }
        readonly Dictionary<int,ActorState> actors=new Dictionary<int,ActorState>();
        readonly int[] state=new int[64]; int stateCount;
        BattleSimulation currentBattle;HdGpuRenderer currentRenderer;
        public int ActorRebuilds {get;private set;}
        public int ActorReuseHits {get;private set;}
        public NativeBattleComposer(float scale=Scale) : base(scale) {}
        internal NativeBattleComposer(PixelCanvas canvas) : base(canvas.Scale) {Canvas=canvas;}
        void Add(int value){state[stateCount++]=value;}
        void Add(bool value){Add(value?1:0);}
        void Add(float value){Add(BitConverter.SingleToInt32Bits(value));}
        bool ActorChanged(BattleSimulation b,Soldier s,bool selected)
        {
            stateCount=0;
            Add(FacingFrame(s.Facing)); WeaponPose(b,s,IsLaunching(b,s),out var ignoredCenter,out var ignoredMuzzle,out var ignoredAim,out var ignoredWeapon,out var weaponLeft);Add(weaponLeft);Add(s.Alive);Add(s.Incapacitated);Add(s.Broken);Add(s.Crouched);
            Add(s.ArmorLevel);Add(s.RigLevel);Add(s.Look?.headId??-1);Add(s.Look?.lightId??-1);Add(s.Look?.familyId??-1);Add(s.Look?.heavyId??-1);Add((int)s.Role);Add((int)s.Side);
            Add(s.State==ActionState.Treat);Add(s.RescuePhase==RescuePhase.Drag);
            Add(s.RescuerId>=0&&s.RescuerId<b.Soldiers.Count&&b.Soldiers[s.RescuerId].RescuePhase==RescuePhase.Drag);
            Add(b.Team(s).LeaderId==s.Id);Add(s.Rockets);Add(s.LauncherKind);Add(s.ReserveLauncherKind);
            Add(s.HasBackWeapon);Add((int)s.BackGunRole);Add(s.BackVariant);Add(s.WeaponModel);
            Add(s.WeaponUpgrade?.Rarity??-1);Add(s.BackWeaponUpgrade?.Rarity??-1);
            Add(SupplyRecovery.Medical(s));Add(s.SupplyPack);Add(s.Underbarrel);Add(HandLauncher.Active(s));
            Add(WeaponBalance.Index(s));Add(Rubezh.Graphics.PrimitiveBattleMode);Add(s.GunSniper);Add(s.GunMachineGunner);Add(IsLaunching(b,s));Add(SomaliFire.Visual(s,b.Time));
            Add(s.SomaliLow);
            if(!s.Alive){Add(Rubezh.Graphics.CorpseFrame(s));Add(Rubezh.Graphics.CorpseAngle(s));}
            ActorState previous;
            if(!actors.TryGetValue(s.Id,out previous))actors.Add(s.Id,previous=new ActorState());
            bool changed=previous.Soldier!=s||previous.Count!=stateCount;
            for(int i=0;!changed&&i<stateCount;i++)changed=previous.Values[i]!=state[i];
            if(changed){Array.Copy(state,previous.Values,stateCount);previous.Count=stateCount;previous.Soldier=s;}
            return changed;
        }
        public void Compose(BattleSimulation battle,HdGpuRenderer renderer,bool debug,int selected)
        {
            BeginWorldItems(renderer,currentBattle!=battle||currentRenderer!=renderer);
            if(currentBattle!=battle||currentRenderer!=renderer){
                currentRenderer?.ReleaseAllActorVisuals();
                if(currentRenderer!=renderer)renderer.ReleaseAllActorVisuals();
                actors.Clear();currentBattle=battle;currentRenderer=renderer;
            }
            ObserveTick(battle);
            // Canvas is shared with the native sink; resolution is prepared before creation.
            PrepareNativeScene(battle.Map);
            RenderNativeScene(battle.Map,renderer);
            renderer.BeginDynamic();
            try {
                Canvas.TestDepth=false;
                if(debug&&!battle.Fog.Enabled)DrawDebug(battle,selected);
                Canvas.TestDepth=true;Canvas.Depth=256f+battle.Map.Objective.Y-battle.Map.Objective.X;
                DrawObjective(battle);
                SetCompositionStage(renderer,-1);DrawBloodTraces(battle);
                SetCompositionStage(renderer,0);DrawGroundEquipment(battle,false);DrawCoverDebris(battle,false);
                SetCompositionStage(renderer,1);
                renderer.BeginVehicleVisual(-2);try{DrawVehicleArrival(battle,battle.GreenVehicle);}finally{renderer.EndVehicleVisual();}
                renderer.BeginVehicleVisual(-3);try{DrawVehicleArrival(battle,battle.RedVehicle);}finally{renderer.EndVehicleVisual();}
                foreach(var soldier in battle.Soldiers){
                    if(soldier==null||!soldier.Deployed||!battle.Fog.CanDraw(soldier))continue;
                    var frame=EnsureFrame(battle,soldier);
                    bool bodyChanged=ActorChanged(battle,soldier,soldier.Id==selected);
                    renderer.ConfigureActorCapture(soldier.Id,frame);
                    Canvas.TestDepth=true;Canvas.Depth=256f+soldier.Position.Y-soldier.Position.X+.06f;
                    SetCompositionStage(renderer,soldier.Alive?1:0);
                    var actor=actors[soldier.Id];
                    int underlay=(soldier.RescuerId>=0&&soldier.RescuerId<battle.Soldiers.Count&&battle.Soldiers[soldier.RescuerId].RescuePhase==RescuePhase.Drag?32:0)|(soldier.Id==selected?1:0)|(soldier.Alive?2:0)|(soldier.Incapacitated?4:0)|((int)soldier.Side<<3);
                    int status=StatusKey(battle,soldier,soldier.Id==selected);
                    DrawSection(battle,soldier,renderer,0,actor.Underlay!=underlay,soldier.Id==selected);
                    DrawSection(battle,soldier,renderer,1,bodyChanged,soldier.Id==selected);
                    DrawSection(battle,soldier,renderer,2,actor.Status!=status,soldier.Id==selected);
                    actor.Underlay=underlay;actor.Status=status;
                    if(bodyChanged)ActorRebuilds++;else ActorReuseHits++;
                }
                SetCompositionStage(renderer,1);Canvas.TestDepth=false;
                foreach(var soldier in battle.Soldiers){
                    if(soldier==null||!battle.Fog.CanDraw(soldier)||!soldier.Alive||soldier.RescuePhase!=RescuePhase.Drag||soldier.RescueTargetId<0||soldier.RescueTargetId>=battle.Soldiers.Count)continue;
                    Canvas.TestDepth=true;Canvas.Depth=256f+soldier.Position.Y-soldier.Position.X+.06f;
                    var from=P(soldier.Position);var to=P(battle.Soldiers[soldier.RescueTargetId].Position);
                    Canvas.Line((int)from.X,(int)from.Y-9,(int)to.X-8,(int)to.Y-2,C(223,190,143));
                }
                DrawGroundEquipment(battle,true);DrawCoverDebris(battle,true);
            }finally{EndWorldItems();renderer.EndDynamic();}
            DrawCachedRoofs(battle);
        }
    }
}
