using System;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public static class DeveloperBuild
    {
        public static bool Enabled
        {
            get
            {
#if RUBEZH_WEB_PREVIEW
                return false;
#elif RUBEZH_DEVELOPER
                return true;
#elif UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool("Rubezh.DeveloperPreview", false);
#else
                return false;
#endif
            }
        }

        public static bool WebPreview
        {
            get
            {
#if RUBEZH_WEB_PREVIEW
                return true;
#else
                return false;
#endif
            }
        }

        public static bool OpenCampaign
        {
            get { return false; }
        }

        public static bool InterestingAvailable
        {
            get { return !WebPreview; }
        }

        public static bool DebugAvailable
        {
            get { return !WebPreview; }
        }

        // Deterministic, affordable starting squad with the exact pre-mission XP ceiling.
        // The remaining XP can be redistributed in the ordinary preparation screen.
        public static Progress ForMission(int mission)
        {
            if (mission < 1 || mission > 20)
                throw new ArgumentOutOfRangeException("mission");
            var p = new Progress { completed = mission - 1, budgetMission = mission };
            int completed = mission - 1;
            Action<int, Action<Progress>> buy = (unlock, change) =>
            {
                if (completed < unlock)
                    return;
                var next = JsonUtility.FromJson<Progress>(JsonUtility.ToJson(p));
                change(next);
                if (next.Valid())
                    p = next;
            };
            buy(
                1,
                q =>
                {
                    q.kits[2].SetExtra(2);
                    q.kits[2].bag[7] = completed >= 6 ? 1 : 0;
                    if (mission == 16)
                    {
                        q.kits[2].bag[6] = 1;
                        q.kits[2].bag[7] = 0;
                    }
                }
            );
            buy(
                3,
                q =>
                {
                    q.kits[4].SetExtra(3);
                    q.kits[4].bag[1] = completed >= 4 ? 1 : 0;
                    q.kits[4].bag[2] = completed >= 10 ? 2 : 0;
                    if (mission == 16)
                    {
                        q.kits[4].bag[0] = 10;
                        q.kits[4].bag[1] = q.kits[4].bag[2] = 0;
                    }
                }
            );
            buy(4, q => q.kits[1].SetWeapon(1));
            buy(8, q => q.kits[3].SetWeapon(4));
            buy(7, q => q.kits[0].SetWeapon(2));
            buy(10, q => q.kits[0].SetExtra(1));
            for (int rank = 1; rank <= 3; rank++)
            for (int skill = 0; skill < Progress.TrainingCount; skill++)
            {
                int r = rank,
                    k = skill;
                buy(
                    k == 4 ? new[] { 0, 6, 14, 17 }[r] : CampaignProgression.TrainingAfter(r),
                    q =>
                    {
                        if (q.training[k] == r - 1)
                            q.training[k] = r;
                    }
                );
            }

            for (int i = 0; i < 5; i++)
            {
                int slot = i;
                if (mission == 6 && i == 4)
                    continue;
                buy(0, q => q.kits[slot].count = 1);
            }

            return p;
        }
    }
}
