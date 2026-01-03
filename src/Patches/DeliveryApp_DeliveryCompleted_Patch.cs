// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.UI;
using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryApp.DeliveryCompleted() to record deliveries to history.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.DeliveryCompleted))]
    public static class DeliveryApp_DeliveryCompleted_Patch
    {
        /// <summary>
        /// Records the completed delivery to history and refreshes UI.
        /// Only the Host creates records - Clients receive them via sync.
        /// </summary>
        /// <param name="__instance">The DeliveryApp instance.</param>
        /// <param name="instance">The completed delivery instance.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryApp __instance, DeliveryInstance instance)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[Patch] DeliveryCompleted called for {instance.StoreName}");

            // In multiplayer, only the Host creates history records
            // Clients receive records via sync from the Host
            if (MultiplayerManager.IsClient)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[Patch] Client mode - skipping local record creation (will receive from Host)");
                return;
            }

            // Add to history (Host or Singleplayer)
            DeliveryHistoryManager.AddDelivery(instance);

            // Refresh UI
            DeliveryHistoryUI.RefreshHistoryUI(__instance);
        }
    }
}