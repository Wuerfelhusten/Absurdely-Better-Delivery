// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using AbsurdelyBetterDelivery.Utils;
using HarmonyLib;
using Il2CppScheduleOne.UI.Shop;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Debug patch for ShopInterface.ListingClicked().
    /// Logs listing data for development purposes.
    /// </summary>
    [HarmonyPatch(typeof(ShopInterface), nameof(ShopInterface.ListingClicked))]
    public static class ShopInterface_ListingClicked_Patch
    {
        /// <summary>
        /// Logs the clicked listing for debugging.
        /// </summary>
        /// <param name="listingUI">The clicked listing UI element.</param>
        [HarmonyPostfix]
        public static void Postfix(ListingUI listingUI)
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[Shop] Listing Clicked!");
            if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
            {
                ClassInspector.InspectInstance(listingUI);
            }
        }
    }
}