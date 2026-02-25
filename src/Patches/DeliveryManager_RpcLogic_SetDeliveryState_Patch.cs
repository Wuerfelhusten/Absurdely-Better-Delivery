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
    /// Fallback patch for delivery arrival notifications on DeliveryManager RPC state transitions.
    /// Ensures arrival messages still fire in IL2CPP execution paths where DeliveryInstance.SetStatus patching is unreliable.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryManager), "RpcLogic___SetDeliveryState_316609003")]
    public static class DeliveryManager_RpcLogic_SetDeliveryState_Patch
    {
        /// <summary>
        /// Sends arrival phone message when a delivery is transitioned to Arrived.
        /// </summary>
        /// <param name="__instance">Delivery manager instance.</param>
        /// <param name="deliveryID">Delivery identifier.</param>
        /// <param name="status">New delivery status.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryManager __instance, string deliveryID, EDeliveryStatus status)
        {
            if (__instance == null || status != EDeliveryStatus.Arrived || string.IsNullOrWhiteSpace(deliveryID))
            {
                return;
            }

            DeliveryInstance delivery = __instance.GetDelivery(deliveryID);
            if (delivery == null)
            {
                return;
            }

            DeliveryArrivalMessageService.NotifyDeliveryArrived(delivery);
        }
    }
}