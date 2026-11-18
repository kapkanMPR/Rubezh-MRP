using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        sealed class ActorTransform
        {
            public NativeBattleComposer.ActorFrame Capture, Display;
            public bool Initialized;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            public Transform Root, Body, Weapon;
#endif
        }
        readonly Dictionary<int,ActorTransform> actorTransforms=new Dictionary<int,ActorTransform>();
        bool actorWeaponCapture;
        internal void ConfigureActorCapture(int id,NativeBattleComposer.ActorFrame frame)
        {
            ActorTransform actor;if(!actorTransforms.TryGetValue(id,out actor))actorTransforms[id]=actor=new ActorTransform();
            actor.Capture=frame;actorWeaponCapture=false;
        }
        internal void SetActorWeaponCapture(bool weapon){actorWeaponCapture=weapon;}
        void LocalizeActorPart(NativeDraw draw,ref NativeSpritePose pose)
        {
            if(visualId<0)return;
            ActorTransform actor;if(!actorTransforms.TryGetValue(visualId/4,out actor))return;
            var f=actor.Capture;
            draw.ParentKind=actorWeaponCapture?2:visualId%4==1?1:0;
            float x=pose.Position.x-f.Anchor.X,y=pose.Position.y-f.Anchor.Y;
            float depth=f.Depth;
            if(draw.ParentKind==2){
                x-=f.Weapon.X;y-=f.Weapon.Y;
                float a=-f.Angle*(float)Math.PI/180,cos=(float)Math.Cos(a),sin=(float)Math.Sin(a);
                float px=x*cos-y*sin;y=x*sin+y*cos;x=px;pose.Angle-=f.Angle;depth+=f.WeaponDepth;
            }else if(draw.ParentKind==1)y-=f.Bob;
            pose.Position=new Vector3(x,y,0);pose.Data.x-=depth;
        }
        internal void UpdateActorTransform(int id,NativeBattleComposer.ActorFrame frame,out bool moved,out bool posed)
        {
            moved=posed=false;ActorTransform actor;if(!actorTransforms.TryGetValue(id,out actor))return;
            var old=actor.Display;
            moved=!actor.Initialized||old.Anchor.X!=frame.Anchor.X||old.Anchor.Y!=frame.Anchor.Y||old.Depth!=frame.Depth;
            posed=!actor.Initialized||old.Bob!=frame.Bob||old.Angle!=frame.Angle||old.Weapon.X!=frame.Weapon.X||old.Weapon.Y!=frame.Weapon.Y||old.WeaponDepth!=frame.WeaponDepth;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if(actor.Root==null)return;
            if(moved)actor.Root.localPosition=new Vector3(frame.Anchor.X,frame.Anchor.Y,frame.Depth/1024f);
            if(posed){
                actor.Body.localPosition=new Vector3(0,frame.Bob,0);
                actor.Weapon.localPosition=new Vector3(frame.Weapon.X,frame.Weapon.Y,frame.WeaponDepth/1024f);
                actor.Weapon.localRotation=Quaternion.Euler(0,0,frame.Angle);
            }
#endif
            actor.Display=frame;actor.Initialized=true;
        }
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        Transform ActorParent(NativeDraw draw)
        {
            if(draw.Actor<0)return null;
            ActorTransform actor;if(!actorTransforms.TryGetValue(draw.Actor/4,out actor))return null;
            if(actor.Root==null){
                var root=new GameObject("Actor "+draw.Actor/4);root.transform.SetParent(unityRoot.transform,false);actor.Root=root.transform;
                var body=new GameObject("Body pose");body.transform.SetParent(actor.Root,false);actor.Body=body.transform;
                var weapon=new GameObject("Aim and recoil");weapon.transform.SetParent(actor.Root,false);actor.Weapon=weapon.transform;
            }
            return draw.ParentKind==2?actor.Weapon:draw.ParentKind==1?actor.Body:actor.Root;
        }
#endif
        readonly List<int> actorRemovalIds=new List<int>();
        readonly List<long> actorNodeRemovalKeys=new List<long>();
        internal void RemoveActorVisual(int id)
        {
            for(int section=0;section<4;section++)RemoveWorldItem(id*4+section);
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            // Include nodes left by a previous, larger pose topology as well as live parts.
            actorNodeRemovalKeys.Clear();
            foreach(var key in unityActorNodes.Keys){long group=(key>>32)-1;if(group>=0&&group/4==id)actorNodeRemovalKeys.Add(key);}
            foreach(var key in actorNodeRemovalKeys)RemoveWorldNode(key);
#endif
            ActorTransform actor;
            if(actorTransforms.TryGetValue(id,out actor)){
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
                if(actor.Root!=null){actor.Root.gameObject.SetActive(false);UnityEngine.Object.Destroy(actor.Root.gameObject);}
#endif
                actorTransforms.Remove(id);
            }
        }
        internal void ReleaseAllActorVisuals()
        {
            actorRemovalIds.Clear();
            foreach(var id in actorTransforms.Keys)actorRemovalIds.Add(id);
            foreach(var id in actorRemovalIds)RemoveActorVisual(id);
            actorRemovalIds.Clear();
            // Group cleanup also covers an interrupted capture before a root was created.
            foreach(var id in directGroups.Keys)if(id>=0)actorRemovalIds.Add(id);
            foreach(var id in actorRemovalIds)RemoveWorldItem(id);
        }
        void DisposeActorTransforms()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            foreach(var actor in actorTransforms.Values)if(actor.Root!=null)UnityEngine.Object.Destroy(actor.Root.gameObject);
#endif
            actorTransforms.Clear();
        }
    }
}
