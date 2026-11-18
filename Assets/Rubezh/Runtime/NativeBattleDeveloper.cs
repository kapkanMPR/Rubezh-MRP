#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.UIElements;
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        void NativeDeveloperReportsButton()
        {
            HudButton(nativeHudControls,"Отчёты",()=>NativeDrawer("reports"),W-190,260,165,65);
        }
        void NativeDeveloperReports(VisualElement content)
        {
            NativeParagraph(content,"Отчёты текущего боя",23,true,ink);
            NativeAction(content,"Производительность и рендер",ExportUnityTestReport);
            NativeAction(content,"Тест рендера 30 секунд",StartFixedBenchmark);
            NativeAction(content,"Тест движения камеры: 30 секунд",StartMovingBenchmark);
            NativeAction(content,"Сравнить рендеры: 60 секунд",StartPairedBenchmark);
            NativeAction(content,"Сменить способ отрисовки для сравнения",()=>{
                if(benchActive){message="Дождись окончания теста перед сменой рендера.";return;}
                if(hdBattle==null){message="Сначала открой бой.";return;}
                hdBattle.UnityCachedStaticCommands=!hdBattle.UnityCachedStaticCommands;
                renderedBattle=null;
                message=hdBattle.UnityCachedStaticCommands?"Включён рендер с сохранёнными командами.":"Включён прежний рендер для сравнения.";
            });
            NativeBound(content,()=>hdBattle==null?"":hdBattle.UnityCachedStaticCommands?"Рендер: сохранённые команды":"Рендер: прежний, через объекты",18);
            NativeAction(content,"Поведение ИИ",ExportAiReport);
            NativeAction(content,"Журнал боя",Export);
            NativeBound(content,()=>string.IsNullOrEmpty(message)?"После сохранения здесь появится путь к файлу.":message,20);
        }
    }
}
#endif
