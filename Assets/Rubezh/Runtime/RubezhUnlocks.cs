using System;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        EquipmentUnlock[] newlyUnlocked = new EquipmentUnlock[0];
        Vector2 unlockScroll;

        void ProcessBattleCompletion()
        {
            if (WatchingInteresting)
            {
                awarded = true;
                return;
            }

            if (awarded || battle == null || battle.Outcome == Outcome.Running)
                return;
            awarded = true;
            if (testBattle)
                return;
            if (IsRaid)
            {
                CompleteRaidWave();
                return;
            }
            newlyUnlocked = new EquipmentUnlock[0];
            unlockScroll = Vector2.zero;
            if (campBattle != null)
            {
                CampReceiptFor(campBattle, battle.Outcome == Outcome.GreenCaptured);
                return;
            }

            if (DeveloperBuild.OpenCampaign || battle.CampaignLevel <= 0)
                return;
            int before = progress.completed;
            if (!progress.Award(battle.CampaignLevel, battle.Outcome))
                return;
            newlyUnlocked = CampaignUnlocks.Between(before, progress.completed);
            if (newlyUnlocked.Length > 0)
                CancelTouchInput();
            message =
                "+"
                + CampaignProgression.Reward(battle.CampaignLevel)
                + " опыта. "
                + UnlockNotice(battle.CampaignLevel);
            Save();
        }

        void DismissUnlocks()
        {
            newlyUnlocked = new EquipmentUnlock[0];
            unlockScroll = Vector2.zero;
            CancelTouchInput();
        }
    }
}
