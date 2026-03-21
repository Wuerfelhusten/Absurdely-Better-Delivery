// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using AbsurdelyBetterDelivery.Services;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryShop.OrderPressed() to capture order prices before submission.
    /// Stores prices in DeliveryPriceTracker for later retrieval.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryShop), nameof(DeliveryShop.OrderPressed))]
    public static class DeliveryShop_OrderPressed_Patch
    {
        /// <summary>
        /// Captures the total order price before the order is submitted.
        /// </summary>
        /// <param name="__instance">The DeliveryShop instance.</param>
        [HarmonyPrefix]
        public static bool Prefix(DeliveryShop __instance)
        {
            if (!DeliveryWaitingQueueService.IsInternalPlacementActive &&
                DeliveryWaitingQueueService.TryQueueFromShopSelection(__instance))
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[Patch] Order queued for waiting: {__instance.MatchingShopInterfaceName}");
                return false;
            }

            try
            {
                // Ref: DeliveryShop.GetOrderTotal() introduced in 0.4.4f10 — replaces manual items+DeliveryFee sum.
                // DeliveryFee is no longer a property on DeliveryShop; it comes from ConfigurationService internally.
                float totalPrice = __instance.GetOrderTotal();

                string storeName = __instance.MatchingShopInterfaceName;
                DeliveryPriceTracker.PendingPrices[storeName] = totalPrice;

                AbsurdelyBetterDeliveryMod.DebugLog($"[Patch] OrderPressed: Captured price {totalPrice} for {storeName}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Patch] Failed to capture order price: {ex.Message}");
            }

            return true;
        }

    }
}