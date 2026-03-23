// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using AbsurdelyBetterDelivery.Managers;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryApp.Awake() to capture the app instance as early as possible.
    /// UI initialization is intentionally deferred to the Start() patch below so that
    /// other mods' Awake() postfixes (e.g. shop-injecting mods like FurnitureDelivery)
    /// are guaranteed to have completed before we build the history panel.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.Awake))]
    public static class DeliveryApp_Awake_Patch
    {
        /// <summary>
        /// Captures the DeliveryApp instance. Does not initialize UI here.
        /// </summary>
        /// <param name="__instance">The DeliveryApp instance.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryApp __instance)
        {
            AbsurdelyBetterDeliveryMod.DeliveryAppInstance = __instance;
            AbsurdelyBetterDeliveryMod.DebugLog("[DeliveryApp] Awake! Instance captured.");
        }
    }

    /// <summary>
    /// Patches DeliveryApp.Start() to initialize the history UI.
    /// Unity guarantees that Start() is called only after all Awake() calls across all
    /// components and mods have completed, so DeliveryApp.deliveryShops is fully
    /// populated (including any shops injected by other mods) by this point.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.Start))]
    public static class DeliveryApp_Start_Patch
    {
        /// <summary>
        /// Initializes the history UI once the app and all mod-injected shops are ready.
        /// </summary>
        /// <param name="__instance">The DeliveryApp instance.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryApp __instance)
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[DeliveryApp] Start! Initializing history UI.");
            DeliveryHistoryManager.InitializeUI(__instance);
        }
    }
}