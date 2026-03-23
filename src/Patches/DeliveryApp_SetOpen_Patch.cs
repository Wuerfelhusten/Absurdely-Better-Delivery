// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using AbsurdelyBetterDelivery.UI;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryApp.SetOpen() to scroll back to the top whenever the player opens the app.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.SetOpen))]
    public static class DeliveryApp_SetOpen_Patch
    {
        /// <summary>
        /// When the app is opened, mark the next UI refresh to scroll to top.
        /// </summary>
        /// <param name="open">Whether the app is being opened (<c>true</c>) or closed (<c>false</c>).</param>
        [HarmonyPrefix]
        public static void Prefix(bool open)
        {
            if (open)
            {
                DeliveryHistoryUI.RequestScrollToTop();
            }
        }
    }
}
