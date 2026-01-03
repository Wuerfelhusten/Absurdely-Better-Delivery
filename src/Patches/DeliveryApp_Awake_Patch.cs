// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using AbsurdelyBetterDelivery.Managers;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryApp.Awake() to capture the app instance and initialize UI.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.Awake))]
    public static class DeliveryApp_Awake_Patch
    {
        /// <summary>
        /// Captures the DeliveryApp instance and initializes the history UI.
        /// </summary>
        /// <param name="__instance">The DeliveryApp instance.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryApp __instance)
        {
            AbsurdelyBetterDeliveryMod.DeliveryAppInstance = __instance;
            AbsurdelyBetterDeliveryMod.DebugLog("[DeliveryApp] Awake! Instance captured.");
            DeliveryHistoryManager.InitializeUI(__instance);
        }
    }
}