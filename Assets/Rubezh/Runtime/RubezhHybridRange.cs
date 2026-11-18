using System;
using UnityEngine;
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        Hybrid.HybridRange hybridRange;
        bool hybridPaused,hybridUnfocused;
#endif
        void OpenHybridRange()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if(loading || hybridRange!=null)return;
            FinishFixedBenchmark(false);
            HideUnityTestHud();ReleaseSceneGraphics();ReleaseBattleSamples();
            battle=null;campBattle=null;campBusy=false;testBattle=unityTestBattle=false;
            paused=false;screen=8;nativeCamp?.Hide();
            try
            {
                hybridRange=new Hybrid.HybridRange(ExitHybridRange,(json)=>{
                    ExportDeveloperText(json,"Rubezh-Hybrid-221-");
                    return lastDeveloperExportSucceeded;
                });
            }
            catch(Exception error)
            {
                DisposeHybridRange();screen=0;ShowCamp();
                message="Полигон не открылся: "+error.Message;
                nativeCamp?.Notify(message);
                Debug.LogException(error);
            }
#endif
        }
        bool TickHybridRange()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if(hybridRange==null)return false;
            hybridRange.Tick(Time.unscaledDeltaTime);return true;
#else
            return false;
#endif
        }
        void SuspendHybridRange(bool value,bool focusEvent=false)
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if(focusEvent)hybridUnfocused=value;else hybridPaused=value;
            hybridRange?.Suspend(hybridPaused||hybridUnfocused);
#endif
        }
        void DisposeHybridRange()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            hybridRange?.Dispose();hybridRange=null;
#endif
        }
        void ExitHybridRange(){DisposeHybridRange();screen=0;ShowCamp();}
    }
}
