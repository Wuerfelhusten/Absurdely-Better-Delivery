// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Text;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Utils;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Property;
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
            AbsurdelyBetterDeliveryMod.DebugLog(
                $"[Repurchase] Record details: ID={record.ID}, store={record.StoreName}, destination={record.Destination}, dock={record.LoadingDockIndex + 1}, items={record.Items.Count}");

            // Find the matching shop
            var targetShop = FindShop(record.StoreName, app);
            if (targetShop == null)
            {
                MelonLogger.Error($"[Repurchase] Shop '{record.StoreName}' not found!");
                return false;
            }

            AbsurdelyBetterDeliveryMod.DebugLog($"[Repurchase] Found shop: {targetShop.name}");

            targetShop.SetIsExpanded(true);

            // Set destination and dock before quantity/order checks.
            // This avoids false negatives when destination-specific dock options differ.
            if (!SetDestination(targetShop, record))
            {
                MelonLogger.Warning($"[Repurchase] Aborting order for '{record.StoreName}': destination '{record.Destination}' could not be resolved.");
                return false;
            }

            SetLoadingDock(targetShop, record);

            // Set item quantities
            int itemsFound = SetItemQuantities(record, targetShop);
            if (itemsFound == 0)
            {
                MelonLogger.Warning($"[Repurchase] No matching items found for '{record.StoreName}'.");
                return false;
            }

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
        private static bool SetDestination(DeliveryShop shop, DeliveryRecord record)
        {
            if (shop.DestinationDropdown == null || shop.DestinationDropdown.options.Count == 0)
            {
                return true;
            }

            if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
            {
                var optionTexts = new System.Collections.Generic.List<string>();
                foreach (var option in shop.DestinationDropdown.options)
                {
                    optionTexts.Add(option.text);
                }

                AbsurdelyBetterDeliveryMod.DebugLog(
                    $"[Repurchase] Destination setup for '{record.StoreName}': requested='{record.Destination}', dropdownOptions=[{string.Join(", ", optionTexts)}]");
            }

            int selectedIndex = shop.DestinationDropdown.value;
            if (selectedIndex < 0 || selectedIndex >= shop.DestinationDropdown.options.Count)
            {
                selectedIndex = 0;
            }

            bool found = false;

            if (!string.IsNullOrEmpty(record.Destination))
            {
                string destTrimmed = record.Destination.Trim();
                string destNormalized = NormalizeForMatch(destTrimmed);

                if (TryResolveDropdownIndexFromDestinationCode(shop, record.Destination, out int resolvedByCodeIndex))
                {
                    selectedIndex = resolvedByCodeIndex;
                    found = true;
                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[Repurchase] Destination code resolved '{record.Destination}' to dropdown '{shop.DestinationDropdown.options[selectedIndex].text}' (index {selectedIndex}).");
                }

                for (int i = 0; i < shop.DestinationDropdown.options.Count && !found; i++)
                {
                    var option = shop.DestinationDropdown.options[i];
                    string optionText = option.text.Trim();
                    string optionNormalized = NormalizeForMatch(optionText);

                    if (optionText.Equals(destTrimmed, StringComparison.OrdinalIgnoreCase) ||
                        optionNormalized.Equals(destNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        found = true;
                        break;
                    }
                }
            }

            if (!found && !string.IsNullOrWhiteSpace(record.Destination))
            {
                var optionTexts = new System.Collections.Generic.List<string>();
                foreach (var option in shop.DestinationDropdown.options)
                {
                    optionTexts.Add(option.text);
                }

                var options = string.Join(", ", optionTexts);
                MelonLogger.Warning($"[Repurchase] Destination '{record.Destination}' not found in dropdown options for '{record.StoreName}'. Options: [{options}]");
                return false;
            }

            if (selectedIndex < 0 || selectedIndex >= shop.DestinationDropdown.options.Count)
            {
                MelonLogger.Warning(
                    $"[Repurchase] Resolved destination index {selectedIndex} is out of range for '{record.StoreName}'. Dropdown count={shop.DestinationDropdown.options.Count}.");
                return false;
            }

            shop.DestinationDropdown.value = selectedIndex;
            shop.DestinationDropdownSelected(selectedIndex);
            AbsurdelyBetterDeliveryMod.DebugLog(
                $"[Repurchase] Destination dropdown selected index={selectedIndex}, text='{shop.DestinationDropdown.options[selectedIndex].text}', requested='{record.Destination}'");

            // Try to set the destination property
            bool destinationPropertySet = SetDestinationProperty(shop, selectedIndex, record.Destination);
            if (!destinationPropertySet && !string.IsNullOrWhiteSpace(record.Destination))
            {
                MelonLogger.Warning($"[Repurchase] Destination property could not be mapped for '{record.StoreName}' ({record.Destination}).");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to resolve the destination dropdown index by matching the stored destination code
        /// (for example "manor") to the shop's potential destination metadata and then to a dropdown label
        /// (for example "Hyland Manor").
        /// </summary>
        private static bool TryResolveDropdownIndexFromDestinationCode(DeliveryShop shop, string destinationCode, out int dropdownIndex)
        {
            dropdownIndex = -1;

            try
            {
                var potentialDestinations = shop.GetPotentialDestinations();
                if (potentialDestinations == null)
                {
                    return false;
                }

                string destinationCodeNormalized = NormalizeForMatch(destinationCode);

                for (int i = 0; i < potentialDestinations.Count; i++)
                {
                    var dest = potentialDestinations[i];
                    string propertyCode = string.Empty;
                    string propertyName = string.Empty;

                    var codeProp = dest.GetType().GetProperty("PropertyCode");
                    if (codeProp != null)
                    {
                        var codeValue = codeProp.GetValue(dest);
                        if (codeValue != null)
                        {
                            propertyCode = codeValue.ToString() ?? string.Empty;
                        }
                    }

                    var nameProp = dest.GetType().GetProperty("PropertyName")
                        ?? dest.GetType().GetProperty("Name");
                    if (nameProp != null)
                    {
                        var nameValue = nameProp.GetValue(dest);
                        if (nameValue != null)
                        {
                            propertyName = nameValue.ToString() ?? string.Empty;
                        }
                    }

                    string propertyCodeNormalized = NormalizeForMatch(propertyCode);
                    string propertyNameNormalized = NormalizeForMatch(propertyName);

                    if (propertyCodeNormalized.Equals(destinationCodeNormalized, StringComparison.OrdinalIgnoreCase) ||
                        propertyNameNormalized.Equals(destinationCodeNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        // Preferred: resolve by dropdown option text (works even when options are dynamically filtered/reordered).
                        if (!string.IsNullOrEmpty(propertyNameNormalized))
                        {
                            for (int optionIndex = 0; optionIndex < shop.DestinationDropdown.options.Count; optionIndex++)
                            {
                                string optionNormalized = NormalizeForMatch(shop.DestinationDropdown.options[optionIndex].text);
                                if (optionNormalized.Equals(propertyNameNormalized, StringComparison.OrdinalIgnoreCase))
                                {
                                    dropdownIndex = optionIndex;
                                    return true;
                                }
                            }
                        }

                        // Fallback: Mono source behavior uses "-" placeholder + i+1 index mapping.
                        int placeholderStyleIndex = i + 1;
                        if (placeholderStyleIndex >= 0 && placeholderStyleIndex < shop.DestinationDropdown.options.Count)
                        {
                            dropdownIndex = placeholderStyleIndex;
                            return true;
                        }

                        // Last fallback for dropdowns without placeholder.
                        if (i >= 0 && i < shop.DestinationDropdown.options.Count)
                        {
                            dropdownIndex = i;
                            return true;
                        }

                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Repurchase] Destination fallback resolution failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Sets the destination property via reflection.
        /// </summary>
        private static bool SetDestinationProperty(DeliveryShop shop, int selectedIndex, string recordDestination)
        {
            try
            {
                var potentialDestinations = shop.GetPotentialDestinations();
                if (potentialDestinations == null) return false;

                string selectedText = shop.DestinationDropdown.options[selectedIndex].text.Trim();
                string selectedNormalized = NormalizeForMatch(selectedText);
                string recordDestinationNormalized = NormalizeForMatch(recordDestination);

                Property? selectedByRecord = null;
                Property? selectedByDropdown = null;

                foreach (var dest in potentialDestinations)
                {
                    string propName = dest.ToString() ?? "";
                    string propertyCode = "";

                    var codeProp = dest.GetType().GetProperty("PropertyCode");
                    if (codeProp != null)
                    {
                        var codeValue = codeProp.GetValue(dest);
                        if (codeValue != null)
                        {
                            propertyCode = codeValue.ToString() ?? "";
                        }
                    }

                    var nameProp = dest.GetType().GetProperty("PropertyName")
                        ?? dest.GetType().GetProperty("Name");
                    if (nameProp != null)
                    {
                        var nameVal = nameProp.GetValue(dest);
                        if (nameVal != null)
                        {
                            propName = nameVal.ToString() ?? "";
                        }
                    }

                    string propNormalized = NormalizeForMatch(propName);
                    string codeNormalized = NormalizeForMatch(propertyCode);

                    bool matchesRecord =
                        !string.IsNullOrEmpty(recordDestinationNormalized) &&
                        (
                            (!string.IsNullOrEmpty(codeNormalized) && codeNormalized.Equals(recordDestinationNormalized, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(propNormalized) && propNormalized.Equals(recordDestinationNormalized, StringComparison.OrdinalIgnoreCase))
                        );

                    bool matchesDropdown =
                        !string.IsNullOrEmpty(selectedNormalized) &&
                        (
                            (!string.IsNullOrEmpty(codeNormalized) && codeNormalized.Equals(selectedNormalized, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(propNormalized) && propNormalized.Equals(selectedNormalized, StringComparison.OrdinalIgnoreCase))
                        );

                    if (matchesRecord && selectedByRecord == null)
                    {
                        selectedByRecord = dest;
                    }

                    if (matchesDropdown && selectedByDropdown == null)
                    {
                        selectedByDropdown = dest;
                    }
                }

                Property? resolvedDestination = selectedByRecord ?? selectedByDropdown;
                if (resolvedDestination != null)
                {
                    shop.destinationProperty = resolvedDestination;
                    shop.RefreshLoadingDockUI();

                    string resolvedCode = string.Empty;
                    var codeProp = resolvedDestination.GetType().GetProperty("PropertyCode");
                    if (codeProp != null)
                    {
                        var codeValue = codeProp.GetValue(resolvedDestination);
                        if (codeValue != null)
                        {
                            resolvedCode = codeValue.ToString() ?? string.Empty;
                        }
                    }

                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[Repurchase] destinationProperty resolved to code='{resolvedCode}' for requested='{recordDestination}' (dropdown='{selectedText}')");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Repurchase] Failed to set destinationProperty: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Normalizes text for robust matching (case-insensitive, alphanumeric only).
        /// </summary>
        private static string NormalizeForMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
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

            AbsurdelyBetterDeliveryMod.DebugLog(
                $"[Repurchase] Loading dock selected for '{record.StoreName}': recordDock={record.LoadingDockIndex + 1}, dropdownIndex={dockIndex}, optionText='{shop.LoadingDockDropdown.options[dockIndex].text}'");
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
            if (IsDockCurrentlyOccupied(app, shop, storeName))
            {
                MelonLogger.Warning($"[Repurchase] Blocking order for {storeName}: selected loading dock is currently occupied by another active delivery.");
                return false;
            }

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
                    string selectedDestinationCode = string.Empty;
                    int selectedDock = shop.loadingDockIndex - 1;

                    try
                    {
                        if (shop.destinationProperty != null)
                        {
                            selectedDestinationCode = shop.destinationProperty.PropertyCode ?? string.Empty;
                        }
                    }
                    catch
                    {
                        selectedDestinationCode = string.Empty;
                    }

                    MelonLogger.Warning(
                        $"[Repurchase] Cannot place order for '{storeName}': reason='{reason}', canOrder={canOrder}, fitsInVehicle={fitsInVehicle}, selectedDestination='{selectedDestinationCode}', selectedDock={selectedDock + 1}");
                }
                return false;
            }
        }

        /// <summary>
        /// Checks whether the currently selected destination and loading dock are already in use
        /// by another active delivery entry in the delivery app.
        /// </summary>
        private static bool IsDockCurrentlyOccupied(DeliveryApp app, DeliveryShop shop, string storeName)
        {
            if (app.statusDisplays == null || app.statusDisplays.Count == 0)
            {
                return false;
            }

            string selectedDestinationCode = string.Empty;
            int selectedDockIndex = shop.loadingDockIndex - 1;

            try
            {
                if (shop.destinationProperty != null)
                {
                    selectedDestinationCode = shop.destinationProperty.PropertyCode ?? string.Empty;
                }
            }
            catch
            {
                selectedDestinationCode = string.Empty;
            }

            if (selectedDockIndex < 0 || string.IsNullOrWhiteSpace(selectedDestinationCode))
            {
                return false;
            }

            string selectedDestinationNormalized = NormalizeForMatch(selectedDestinationCode);

            foreach (var display in app.statusDisplays)
            {
                if (display == null || display.DeliveryInstance == null)
                {
                    continue;
                }

                DeliveryInstance activeDelivery = display.DeliveryInstance;

                string activeDestination = activeDelivery.DestinationCode ?? string.Empty;
                int activeDockIndex = activeDelivery.LoadingDockIndex;

                bool sameDestination = NormalizeForMatch(activeDestination)
                    .Equals(selectedDestinationNormalized, StringComparison.OrdinalIgnoreCase);

                bool sameDock = activeDockIndex == selectedDockIndex;

                if (sameDestination && sameDock)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[Repurchase] Dock occupied check hit: destination={activeDestination}, dock={activeDockIndex + 1}, existingStore={activeDelivery.StoreName}, requestedStore={storeName}");
                    return true;
                }

                if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[Repurchase] Dock occupied check: active destination={activeDestination}, dock={activeDockIndex + 1}, selected destination={selectedDestinationCode}, dock={selectedDockIndex + 1}");
                }
            }

            return false;
        }

        #endregion
    }
}