// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using AbsurdelyBetterDelivery.UI;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryApp.RefreshContent() to keep custom UI in sync.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.RefreshContent))]
    public static class DeliveryApp_RefreshContent_Patch
    {
        /// <summary>
        /// Refreshes the custom history UI after content refresh.
        /// </summary>
        /// <param name="__instance">The DeliveryApp instance.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryApp __instance)
        {
            DeliveryHistoryUI.RefreshHistoryUI(__instance);
        }
    }
}