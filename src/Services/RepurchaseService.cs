// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Utils;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;
using UnityEngine;

namespace AbsurdelyBetterDelivery.Services
{
    /// <summary>
    /// Service responsible for repurchasing delivery orders from history.
    /// Handles shop navigation, item selection, and order submission.
    /// </summary>
    public static class RepurchaseService
    {
        #region Public API

        /// <summary>
        /// Repurchases a delivery record.
        /// Returns true if the order was placed successfully, false otherwise.
        /// </summary>
        /// <param name="record">The delivery record to repurchase.</param>
        /// <param name="app">The DeliveryApp instance (optional, will be found if null).</param>
        public static bool RepurchaseRecord(DeliveryRecord record, DeliveryApp? app = null)
        {
            if (record == null)
            {
                MelonLogger.Error("[Repurchase] Record is null!");
                return false;
            }

            // If we're a client in multiplayer, request host to execute
            if (MultiplayerManager.IsClient)
            {
                ClientSyncService.RequestExecuteRecurringOrder(record.ID);
                return true; // Return true since we sent the request
            }

            // Get or find the DeliveryApp
            app = EnsureDeliveryApp(app);
            if (app == null)
            {
                MelonLogger.Error("[Repurchase] DeliveryApp instance not found!");
                return false;
            }

            AbsurdelyBetterDeliveryMod.DebugLog($"[Repurchase] Repurchasing {record.StoreName}...");
            return ExecuteRepurchase(record, app);
        }

        /// <summary>
        /// Repurchases the most recent delivery from history.
        /// </summary>
        public static void RepurchaseLastDelivery()
        {
            if (Managers.DeliveryHistoryManager.History.Count == 0)
            {
                return;
            }

            var app = EnsureDeliveryApp(null);
            if (app == null)
            {
                MelonLogger.Error("[Repurchase] DeliveryApp instance not found!");
                return;
            }

            var lastDelivery = Managers.DeliveryHistoryManager.History[0];
            ExecuteRepurchase(lastDelivery, app);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Ensures we have a valid DeliveryApp reference.
        /// </summary>
        private static DeliveryApp? EnsureDeliveryApp(DeliveryApp? app)
        {
            if (app != null) return app;

            app = AbsurdelyBetterDeliveryMod.DeliveryAppInstance;
            if (app != null) return app;

            app = UnityEngine.Object.FindObjectOfType<DeliveryApp>();
            if (app != null)
            {
                AbsurdelyBetterDeliveryMod.DeliveryAppInstance = app;
            }

            return app;
        }

        /// <summary>
        /// Executes the repurchase operation.
        /// Returns true if order was placed, false otherwise.
        /// </summary>
        private static bool ExecuteRepurchase(DeliveryRecord record, DeliveryApp app)
        {
            // Find the matching shop
            var targetShop = FindShop(record.StoreName, app);
            if (targetShop == null)
            {
                MelonLogger.Error($"[Repurchase] Shop '{record.StoreName}' not found!");
                return false;
            }

            AbsurdelyBetterDeliveryMod.DebugLog($"[Repurchase] Found shop: {targetShop.name}");
            
            // Pre-check if we can order before touching the UI
            if (!CanPlaceOrder(targetShop, record))
            {
                return false;
            }

            targetShop.SetIsExpanded(true);

            // Set item quantities
            int itemsFound = SetItemQuantities(record, targetShop);
            if (itemsFound == 0)
            {
                return false;
            }

            // Set destination and loading dock
            SetDestination(targetShop, record);
            SetLoadingDock(targetShop, record);

            // Submit order and return success status
            return SubmitOrder(targetShop, app, record.StoreName);
        }

        /// <summary>
        /// Finds the shop matching the store name.
        /// </summary>
        private static DeliveryShop? FindShop(string storeName, DeliveryApp app)
        {
            foreach (var shop in app.deliveryShops)
            {
                string shopName = shop.name;

                // Try to get the interface name property
                var interfaceNameProp = shop.GetType().GetProperty("MatchingShopInterfaceName");
                if (interfaceNameProp != null)
                {
                    var val = interfaceNameProp.GetValue(shop)?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        shopName = val;
                    }
                }

                if (shopName.Trim().Equals(storeName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return shop;
                }
            }

            return null;
        }

        /// <summary>
        /// Sets the quantities for all items in the order.
        /// </summary>
        private static int SetItemQuantities(DeliveryRecord record, DeliveryShop shop)
        {
            int itemsFound = 0;

            foreach (var item in record.Items)
            {
                bool found = false;

                foreach (var entry in shop.listingEntries)
                {
                    string? uiName = entry.ItemNameLabel?.text;
                    string? dataName = null;

                    if (entry.MatchingListing != null)
                    {
                        var itemProp = entry.MatchingListing.GetType().GetProperty("Item");
                        if (itemProp != null)
                        {
                            var itemObj = itemProp.GetValue(entry.MatchingListing);
                            if (itemObj is UnityEngine.Object unityObj)
                            {
                                string objName = unityObj.name;
                                if (!string.IsNullOrEmpty(objName) && 
                                    objName.Trim().Equals(item.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    dataName = objName;
                                }
                            }
                        }
                    }

                    // Check if item matches
                    bool uiMatch = !string.IsNullOrEmpty(uiName) && 
                                   uiName.Trim().Equals(item.Name.Trim(), StringComparison.OrdinalIgnoreCase);

                    if (uiMatch || !string.IsNullOrEmpty(dataName))
                    {
                        entry.SetQuantity(item.Quantity, true);
                        found = true;
                        itemsFound++;
                        break;
                    }
                }

                if (!found)
                {
                    MelonLogger.Warning($"  - Could not find listing for item: {item.Name}");
                }
            }

            return itemsFound;
        }

        /// <summary>
        /// Sets the destination dropdown.
        /// </summary>
        private static void SetDestination(DeliveryShop shop, DeliveryRecord record)
        {
            if (shop.DestinationDropdown == null || shop.DestinationDropdown.options.Count == 0)
            {
                return;
            }

            int selectedIndex = 0;
            bool found = false;

            if (!string.IsNullOrEmpty(record.Destination))
            {
                string destTrimmed = record.Destination.Trim();
                string destNormalized = destTrimmed.Replace(" ", "").ToLowerInvariant();

                for (int i = 0; i < shop.DestinationDropdown.options.Count; i++)
                {
                    var option = shop.DestinationDropdown.options[i];
                    string optionText = option.text.Trim();
                    string optionNormalized = optionText.Replace(" ", "").ToLowerInvariant();

                    if (optionText.Equals(destTrimmed, StringComparison.OrdinalIgnoreCase) ||
                        optionNormalized.Equals(destNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        found = true;
                        break;
                    }
                }
            }

            // Default to second option if not found (first is usually placeholder)
            if (!found && shop.DestinationDropdown.options.Count > 1)
            {
                selectedIndex = 1;
            }

            shop.DestinationDropdown.value = selectedIndex;
            shop.DestinationDropdownSelected(selectedIndex);

            // Try to set the destination property
            SetDestinationProperty(shop, selectedIndex);
        }

        /// <summary>
        /// Sets the destination property via reflection.
        /// </summary>
        private static void SetDestinationProperty(DeliveryShop shop, int selectedIndex)
        {
            try
            {
                var potentialDestinations = shop.GetPotentialDestinations();
                if (potentialDestinations == null) return;

                string selectedText = shop.DestinationDropdown.options[selectedIndex].text.Trim();

                foreach (var dest in potentialDestinations)
                {
                    string propName = dest.ToString() ?? "";

                    var nameProp = dest.GetType().GetProperty("Name");
                    if (nameProp != null)
                    {
                        var nameVal = nameProp.GetValue(dest);
                        if (nameVal != null)
                        {
                            propName = nameVal.ToString() ?? "";
                        }
                    }

                    string propNormalized = propName.Replace(" ", "").ToLowerInvariant();
                    string selectedNormalized = selectedText.Replace(" ", "").ToLowerInvariant();

                    if (propNormalized.Contains(selectedNormalized) || selectedNormalized.Contains(propNormalized))
                    {
                        shop.destinationProperty = dest;
                        shop.RefreshLoadingDockUI();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Repurchase] Failed to set destinationProperty: " + ex.Message);
            }
        }

        /// <summary>
        /// Sets the loading dock dropdown.
        /// </summary>
        private static void SetLoadingDock(DeliveryShop shop, DeliveryRecord record)
        {
            if (shop.LoadingDockDropdown == null || shop.LoadingDockDropdown.options.Count == 0)
            {
                MelonLogger.Warning("[Repurchase] LoadingDockDropdown is null or has no options!");
                return;
            }

            int dockIndex = record.LoadingDockIndex;
            bool hasPlaceholder = false;

            // Check if first option is a placeholder
            string firstOption = shop.LoadingDockDropdown.options[0].text.Trim().ToLower();
            if (firstOption.Contains("select") || firstOption.Contains("-") || string.IsNullOrWhiteSpace(firstOption))
            {
                hasPlaceholder = true;
            }

            // Adjust index for placeholder - if there's a placeholder, add 1 to get the correct dropdown index
            if (hasPlaceholder)
            {
                dockIndex = record.LoadingDockIndex + 1;
            }

            // Clamp to valid range
            if (dockIndex < 0 || dockIndex >= shop.LoadingDockDropdown.options.Count)
            {
                dockIndex = (hasPlaceholder && shop.LoadingDockDropdown.options.Count > 1) ? 1 : 0;
            }

            shop.LoadingDockDropdown.value = dockIndex;
            shop.LoadingDockDropdownSelected(dockIndex);
            // Don't manually set loadingDockIndex - LoadingDockDropdownSelected should handle it
            // The dropdown handles the internal index based on whether there's a placeholder
        }

        /// <summary>
        /// Pre-checks if an order can be placed without modifying the shop state.
        /// </summary>
        private static bool CanPlaceOrder(DeliveryShop shop, DeliveryRecord record)
        {
            try
            {
                // Check if shop allows ordering at all (basic checks)
                string reason = "";
                bool basicCheck = shop.CanOrder(out reason);
                
                // If the reason is "Delivery already in progress", that's expected for ASAP orders
                if (!basicCheck && !string.IsNullOrEmpty(reason) && reason.Contains("Delivery already in progress"))
                {
                    return false;
                }
                
                // Check loading dock availability if specified
                if (shop.LoadingDockDropdown != null && shop.LoadingDockDropdown.options.Count > 0)
                {
                    int dockIndex = record.LoadingDockIndex;
                    bool hasPlaceholder = false;

                    string firstOption = shop.LoadingDockDropdown.options[0].text.Trim().ToLower();
                    if (firstOption.Contains("select") || firstOption.Contains("-") || string.IsNullOrWhiteSpace(firstOption))
                    {
                        hasPlaceholder = true;
                    }

                    if (hasPlaceholder)
                    {
                        dockIndex = record.LoadingDockIndex + 1;
                    }

                    if (dockIndex < 0 || dockIndex >= shop.LoadingDockDropdown.options.Count)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[Repurchase] Pre-check error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Submits the order if possible.
        /// Returns true if order was placed, false otherwise.
        /// </summary>
        private static bool SubmitOrder(DeliveryShop shop, DeliveryApp app, string storeName)
        {
            shop.RefreshOrderButton();

            string reason = "";
            bool canOrder = shop.CanOrder(out reason);
            bool fitsInVehicle = shop.WillCartFitInVehicle();

            if (canOrder && fitsInVehicle)
            {
                shop.OrderPressed();
                app.RefreshContent(true);
                app.RefreshNoDeliveriesIndicator();
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[Repurchase] Order placed for {storeName}");
                return true;
            }
            else
            {
                // Only log warning if debug mode is on to avoid spamming the console
                if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                {
                    MelonLogger.Warning($"[Repurchase] Cannot place order: {reason}");
                }
                return false;
            }
        }

        #endregion
    }
}