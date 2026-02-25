// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using AbsurdelyBetterDelivery.UI;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Patches DeliveryApp.CreateDeliveryStatusDisplay() to hide vanilla displays
    /// and refresh our custom UI instead.
    /// </summary>
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.CreateDeliveryStatusDisplay))]
    public static class DeliveryApp_CreateDeliveryStatusDisplay_Patch
    {
        /// <summary>
        /// Hides newly created vanilla status displays and refreshes custom UI.
        /// Also applies delivery time multiplier to new deliveries.
        /// </summary>
        /// <param name="__instance">The DeliveryApp instance.</param>
        /// <param name="instance">The delivery instance created by the game.</param>
        [HarmonyPostfix]
        public static void Postfix(DeliveryApp __instance, Il2CppScheduleOne.Delivery.DeliveryInstance instance)
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[Patch] CreateDeliveryStatusDisplay called!");

            // Apply delivery time multiplier to the exact delivery that was just created.
            ApplyDeliveryTimeMultiplier(instance);

            // Hide all vanilla status displays
            if (__instance.statusDisplays != null)
            {
                foreach (var display in __instance.statusDisplays)
                {
                    if (display?.gameObject != null)
                    {
                        display.gameObject.SetActive(false);
                    }
                }
            }

            // Refresh our custom UI
            DeliveryHistoryUI.RefreshHistoryUI(__instance);
        }

        /// <summary>
        /// Applies the delivery time multiplier to a specific delivery instance.
        /// </summary>
        private static void ApplyDeliveryTimeMultiplier(Il2CppScheduleOne.Delivery.DeliveryInstance delivery)
        {
            try
            {
                float multiplier = AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value;

                // Skip if multiplier is 1.0 (normal speed)
                if (multiplier >= 0.99f && multiplier <= 1.01f)
                {
                    return;
                }

                if (delivery == null)
                {
                    return;
                }

                int originalTime = delivery.TimeUntilArrival;

                // Apply multiplier to delivery time
                // multiplier 0.5 = delivery arrives in 50% of original time
                // multiplier 2.0 = delivery takes 200% of original time
                int newTime = (int)Math.Round(originalTime * multiplier);

                // Clamp to at least 1 minute
                if (newTime < 1) newTime = 1;

                delivery.TimeUntilArrival = newTime;

                AbsurdelyBetterDeliveryMod.DebugLog($"[DeliveryTime] {delivery.StoreName}: Initial time {originalTime} → {newTime} min (multiplier: {multiplier:F2}x)");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeliveryTime] Error applying multiplier: {ex.Message}");
            }
        }
    }
}