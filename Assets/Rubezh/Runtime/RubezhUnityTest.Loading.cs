#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_CONTENT_CONTRACT
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Rubezh.Camp;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        void BeginNativeTestLoad(CampOrder order)
        {
            HideUnityTestHud();
            PrepareFireLoading();
            loading = true;
            loadingDisposed = false;
            loadingError = null;
            loadingTarget = loadingShown = 0;
            loadingStatus = "Загрузка карты Unity";
            StartCoroutine(GuardLoading(LoadNativeTest(order)));
        }

        IEnumerator LoadNativeTest(CampOrder order)
        {
            yield return null;
            var handle = Addressables.LoadAssetAsync<TextAsset>(
                NativeBattleMap.Address(order.mode, order.block, order.mission)
            );
            RubezhLevel map = null;
            SilentOrders.Core.BattleSimulation sim;
            try
            {
                while (!handle.IsDone)
                {
                    if (loadingDisposed)
                        yield break;
                    yield return null;
                }
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(
                        "Не загружена карта операции. Выполните подготовку нативного контента.",
                        handle.OperationException
                    );
                map = JsonUtility.FromJson<BuiltinCampaign.Map>(PackedLevel.Read(handle.Result)).Create();
                loadingStatus = "Создание боевого состояния";
                yield return null;
                sim = BuildCampSimulation(order, map);
                if (order.mode == "raid")
                    PrepareRaidWave(sim);
            }
            finally
            {
                if (map != null)
                    Destroy(map);
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            var load = LoadBattlePages(
                sim,
                () =>
                {
                    campBusy = false;
                    raidChanging = false;
                    campBattle = order;
                    CompleteBattleStart(sim);
                }
            );
            try
            {
                while (load.MoveNext())
                    yield return load.Current;
            }
            finally
            {
                (load as IDisposable)?.Dispose();
            }
        }
    }
}
#endif
