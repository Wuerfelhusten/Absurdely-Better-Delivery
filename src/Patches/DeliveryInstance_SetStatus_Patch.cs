// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using AbsurdelyBetterDelivery.Services;
using HarmonyLib;
using Il2CppScheduleOne.Delivery;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches delivery status transitions to emit arrival phone messages exactly when a delivery reaches Arrived.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryInstance), nameof(DeliveryInstance.SetStatus))]
    public static class DeliveryInstance_SetStatus_Patch
    {
        /// <summary>
        /// Captures whether the delivery was already marked as arrived before status update.
        /// </summary>
        /// <param name="__instance">The delivery instance being updated.</param>
        /// <param name="__state">True when delivery was already arrived.</param>
        [HarmonyPrefix]
        public static void Prefix(DeliveryInstance __instance, ref bool __state)
        {
            __state = __instance != null && __instance.Status == EDeliveryStatus.Arrived;
        }

        /// <summary>
        /// Sends an arrival message when status changes to Arrived.
        /// </summary>
        /// <param name="__instance">The delivery instance being updated.</param>
        /// <param name="status">New delivery status.</param>
        /// <param name="__state">True when delivery was already arrived before this call.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryInstance __instance, EDeliveryStatus status, bool __state)
        {
            if (__instance == null || status != EDeliveryStatus.Arrived || __state)
            {
                return;
            }

            DeliveryArrivalMessageService.NotifyDeliveryArrived(__instance);
        }
    }
}