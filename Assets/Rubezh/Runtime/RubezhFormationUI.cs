using System;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        int formationPickerSlot = -1;
        static readonly string[] FormationChoice = { "Ближе", "Баланс", "Шире" };
        static readonly string[] FormationDepthChoice = { "Меньше", "Баланс", "Больше" };
        static readonly string[] FormationPositions = { "ВПЕРЕДИ", "ЦЕНТР", "ПОЗАДИ" };
        FormationSettings FormationForMenu
        {
            get
            {
                if (formationOrder != null)
                    return formationOrder.formation;
                if (campaignMode)
                {
                    if (draft == null)
                        draft = new Progress();
                    if (draft.formation == null || !draft.formation.Valid())
                        draft.formation = new FormationSettings();
                    return draft.formation;
                }

                if (!quickFormationLoaded)
                {
                    quickFormationLoaded = true;
                    var f = new FormationSettings();
                    f.quietWork = PlayerPrefs.GetInt("rubezh.form.quietWork", 0) == 1;
                    f.pairing = PlayerPrefs.GetInt("rubezh.form.pair", 0);
                    for (int i = 0; i < 4; i++)
                        f.pairSlots[i] = PlayerPrefs.GetInt("rubezh.form.slot." + i, -1);
                    f.width = PlayerPrefs.GetInt("rubezh.form.width", 1);
                    f.depth = PlayerPrefs.GetInt("rubezh.form.depth", 1);
                    for (int i = 1; i < 5; i++)
                        f.positions[i] = PlayerPrefs.GetInt("rubezh.form.pos." + i, 0);
                    quickFormation = f.Valid() ? f : new FormationSettings();
                }

                return quickFormation;
            }
        }

        void SaveQuickFormation()
        {
            if (formationOrder != null || campaignMode)
                return;
            var f = FormationForMenu;
            PlayerPrefs.SetInt("rubezh.form.quietWork", f.quietWork ? 1 : 0);
            PlayerPrefs.SetInt("rubezh.form.pair", f.pairing);
            for (int i = 0; i < 4; i++)
                PlayerPrefs.SetInt("rubezh.form.slot." + i, f.pairSlots == null ? -1 : f.pairSlots[i]);
            PlayerPrefs.SetInt("rubezh.form.width", f.width);
            PlayerPrefs.SetInt("rubezh.form.depth", f.depth);
            for (int i = 1; i < 5; i++)
                PlayerPrefs.SetInt("rubezh.form.pos." + i, f.positions[i]);
            PlayerPrefs.Save();
        }

        bool FormationWounded(int i)
        {
            return formationOrder == null
                ? WoundedSlot(i)
                : !editingSquadFormation && formationOrder.mode == "story" && formationOrder.mission == 6 && i == 4;
        }

        string FormationName(int i)
        {
            return formationOrder == null ? Names[i] : formationOrder.units[i].name;
        }

        Kit FormationKit(int i)
        {
            return formationOrder == null ? Kits[i] : formationOrder.units[i].Base();
        }

        bool FormationReady(FormationSettings f)
        {
            return f != null
                && f.Valid()
                && (
                    f.pairing != 4
                    || f.ManualComplete()
                    || FormationWounded(4) && f.Valid() && new[] { 1, 2, 3 }.All(id => f.pairSlots.Contains(id))
                );
        }

        // Retained UI implementation lives in NativeFormationMenu.cs.
    }
}
