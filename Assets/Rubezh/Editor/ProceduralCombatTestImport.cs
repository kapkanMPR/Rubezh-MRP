#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;
namespace Rubezh.UnityPort.Editor {
 public static class ProceduralCombatTestImport {
  [MenuItem("Рубеж/Процедурные здания/Импорт тестовой локации JSON")]
  public static void Import(){
   var file=EditorUtility.OpenFilePanel("Локация из редактора 1.8.6","","json");if(string.IsNullOrEmpty(file))return;
   RubezhLevel level=null;
   try {level=ScriptableObject.CreateInstance<RubezhLevel>();JsonUtility.FromJsonOverwrite(LevelJson.ToUnity(File.ReadAllText(file)),level);string error;if(!LevelValidation.Valid(level,out error))throw new System.Exception(error);
    if(level.proceduralCombatVersion!=1||level.proceduralVisuals==null||level.proceduralVisuals.Count==0)throw new System.Exception("Пересохраните локацию в редакторе 1.8.6: отсутствует боевой экспорт дома");
    var path=EditorUtility.SaveFilePanelInProject("Сохранить тестовую карту","Procedural_Test","asset","Кампания не заменяется");if(string.IsNullOrEmpty(path))return;
    level.displayName="Процедурный тест";level.name=Path.GetFileNameWithoutExtension(path);level.campaignMission=0;level.enabledInGame=false;level.useInQuickBattle=false;AssetDatabase.CreateAsset(level,path);Selection.activeObject=level;EditorGUIUtility.PingObject(level);AssetDatabase.SaveAssets();level=null;
   }catch(System.Exception e){EditorUtility.DisplayDialog("Ошибка импорта",e.Message,"OK");}finally{if(level!=null)Object.DestroyImmediate(level);}
  }
 }
}
#endif
