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
                float itemsTotal = CalculateItemsTotal(__instance);
                float deliveryFee = __instance.DeliveryFee;
                float totalPrice = itemsTotal + deliveryFee;

                string storeName = __instance.MatchingShopInterfaceName;
                DeliveryPriceTracker.PendingPrices[storeName] = totalPrice;

                AbsurdelyBetterDeliveryMod.DebugLog($"[Patch] OrderPressed: Captured price {totalPrice} for {storeName} (Items: {itemsTotal}, Fee: {deliveryFee})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Patch] Failed to capture order price: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Calculates the total price of all items in the order.
        /// </summary>
        private static float CalculateItemsTotal(DeliveryShop shop)
        {
            float total = 0f;

            if (shop.listingEntries == null) return total;

            foreach (var entry in shop.listingEntries)
            {
                if (entry == null || entry.SelectedQuantity <= 0 || entry.MatchingListing == null)
                {
                    continue;
                }

                float itemPrice = entry.MatchingListing.Price;
                int quantity = entry.SelectedQuantity;
                float subtotal = itemPrice * quantity;

                total += subtotal;

                AbsurdelyBetterDeliveryMod.DebugLog($"[Patch] Item: {entry.MatchingListing.name} x{quantity} @ ${itemPrice} = ${subtotal}");
            }

            return total;
        }
    }
}