using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        void BeginBattleDiagnostics()
        {
            if(battle==null)return;
            battle.Diagnostics.Enabled=DeveloperBuild.Enabled;
            battle.Outcomes.Enabled=DeveloperBuild.Enabled;
            if(DeveloperBuild.Enabled)battle.History.Sample(battle);
        }
        void DrawDeveloperReports()
        {
            if(B(W-180,260,155,65,"Отчёты"))OpenDeveloperReports();
        }
        void OpenDeveloperReports()
        {
            if(!DeveloperBuild.Enabled||battle==null)return;
            Pick("Отчёты",new[]{"Отчёт ИИ","Производительность","Боевой журнал"},i=>{
                if(i==0)ExportAiReport();else if(i==1)ExportUnityTestReport();else Export();
            });
        }
        void ExportAiReport()
        {
            if(WatchingInteresting||!DeveloperBuild.Enabled||battle==null)return;
            var report=AiBattleReport.Capture(battle,"");
            report.performance=CapturePerformance();
            ExportDeveloperText(JsonUtility.ToJson(report,false),"Rubezh-ai-report-");
        }

        bool lastDeveloperExportSucceeded;
        void ExportDeveloperText(string json, string prefix)
        {
            lastDeveloperExportSucceeded=false;
            if (!DeveloperBuild.Enabled && !testBattle)
                return;
            try
            {
                string name = prefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                string path = Path.Combine(Application.persistentDataPath, name);
                File.WriteAllText(path, json, Encoding.UTF8);
                bool copied = json.Length <= 250000;
                if (copied)
                    GUIUtility.systemCopyBuffer = json;
                string copyNotice = copied ? " · также скопирован" : " · большой отчёт сохранён файлом";
#if UNITY_ANDROID && !UNITY_EDITOR
    using(var version=new AndroidJavaClass("android.os.Build$VERSION")){
     if(version.GetStatic<int>("SDK_INT")>=29){
      using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
      using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))
      using(var resolver=activity.Call<AndroidJavaObject>("getContentResolver"))
      using(var downloads=new AndroidJavaClass("android.provider.MediaStore$Downloads"))
      using(var values=new AndroidJavaObject("android.content.ContentValues")){
       values.Call("put","_display_name",name);values.Call("put","mime_type","application/json");values.Call("put","relative_path","Download/Rubezh");
       using(var uri=resolver.Call<AndroidJavaObject>("insert",downloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"),values)){
        if(uri==null)throw new IOException("Android не создал файл в Downloads.");
        try{using(var stream=resolver.Call<AndroidJavaObject>("openOutputStream",uri)){if(stream==null)throw new IOException("Нет доступа к Downloads.");stream.Call("write",Encoding.UTF8.GetBytes(json));stream.Call("close");}}
        catch{resolver.Call<int>("delete",uri,null,null);throw;}
       }
      }
      message="Отчёт: Downloads/Rubezh/"+name+copyNotice;
     }else{
      if(!copied){message="Большой отчёт сохранён: "+path;lastDeveloperExportSucceeded=true;return;}
      using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))using(var intent=new AndroidJavaObject("android.content.Intent","android.intent.action.SEND"))using(var cls=new AndroidJavaClass("android.content.Intent")){
       intent.Call<AndroidJavaObject>("setType","text/plain");intent.Call<AndroidJavaObject>("putExtra","android.intent.extra.TEXT",json);using(var chooser=cls.CallStatic<AndroidJavaObject>("createChooser",intent,"Отправить отчёт Рубеж"))activity.Call("startActivity",chooser);
      }message="Отчёт сохранён и скопирован; выберите приложение для отправки текста.";
     }
    }
#else
                message = "Отчёт сохранён: " + path + copyNotice;
                Application.OpenURL(new Uri(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar).AbsoluteUri);
#endif
                lastDeveloperExportSucceeded=true;
            }
            catch (Exception ex)
            {
                message = "Экспорт: " + ex.Message + " · локальный отчёт/буфер могут быть доступны";
            }
        }
    }
}
