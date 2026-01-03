// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Patches;
using AbsurdelyBetterDelivery.Services;
using AbsurdelyBetterDelivery.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;
using MelonLoader.Utils;

namespace AbsurdelyBetterDelivery.Managers
{
    /// <summary>
    /// Central manager for delivery history data.
    /// Handles storage, retrieval, and persistence of delivery records.
    /// </summary>
    public static class DeliveryHistoryManager
    {
        #region Private Fields

        private static string _currentSaveName = "Default";

        #endregion

        #region Properties

        /// <summary>
        /// Path to the history JSON file for the current save.
        /// </summary>
        private static string HistoryPath => 
            Path.Combine(MelonEnvironment.UserDataDirectory, $"DeliveryHistory_{_currentSaveName}.json");

        /// <summary>
        /// The list of all delivery records.
        /// </summary>
        public static List<DeliveryRecord> History { get; private set; } = new List<DeliveryRecord>();

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the history manager for a specific save file.
        /// </summary>
        /// <param name="saveName">Name of the save file.</param>
        public static void Initialize(string saveName = "Default")
        {
            _currentSaveName = saveName;
            LoadHistory();
        }

        /// <summary>
        /// Initializes the UI for the delivery app.
        /// </summary>
        /// <param name="app">The DeliveryApp instance.</param>
        public static void InitializeUI(DeliveryApp app)
        {
            DeliveryHistoryUI.InitializeUI(app);
        }

        #endregion

        #region History Management

        /// <summary>
        /// Clears all delivery history.
        /// </summary>
        public static void ClearHistory()
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[History] Clearing history. Current count: {History.Count}, Save: {_currentSaveName}");
            History.Clear();
            AbsurdelyBetterDeliveryMod.DebugLog($"[History] History list cleared. New count: {History.Count}");
            SaveHistory();
            AbsurdelyBetterDeliveryMod.DebugLog("[History] All delivery history has been cleared and saved.");
        }

        /// <summary>
        /// Adds a new delivery to the history.
        /// </summary>
        /// <param name="delivery">The delivery instance to record.</param>
        public static void AddDelivery(DeliveryInstance delivery)
        {
            if (!AbsurdelyBetterDeliveryMod.EnableHistory.Value)
            {
                return;
            }

            var deliveryRecord = new DeliveryRecord
            {
                ID = delivery.DeliveryID,
                StoreName = delivery.StoreName,
                Timestamp = DateTime.Now
            };

            // Extract delivery details
            ExtractDeliveryDetails(delivery, deliveryRecord);
            ExtractItems(delivery, deliveryRecord);

            // Add to history
            History.Insert(0, deliveryRecord);

            // Enforce max history limit
            int maxItems = AbsurdelyBetterDeliveryMod.MaxHistoryItems.Value;
            if (History.Count > maxItems)
            {
                History.RemoveRange(maxItems, History.Count - maxItems);
            }

            SaveHistory();
            AbsurdelyBetterDeliveryMod.DebugLog($"[History] Added delivery from {deliveryRecord.StoreName}. Items: {deliveryRecord.Items.Count}");

            // Broadcast to clients if we're the host
            if (MultiplayerManager.IsHost)
            {
                HostSyncService.BroadcastHistoryUpdate(deliveryRecord, HistoryUpdateType.Add);
            }
        }

        #endregion

        #region Repurchase Delegation

        /// <summary>
        /// Repurchases a delivery record.
        /// Delegates to RepurchaseService.
        /// </summary>
        /// <param name="record">The record to repurchase.</param>
        /// <param name="app">The DeliveryApp instance (optional).</param>
        public static void RepurchaseRecord(DeliveryRecord record, DeliveryApp? app = null)
        {
            RepurchaseService.RepurchaseRecord(record, app);
        }

        /// <summary>
        /// Repurchases the most recent delivery.
        /// Delegates to RepurchaseService.
        /// </summary>
        public static void RepurchaseLastDelivery()
        {
            RepurchaseService.RepurchaseLastDelivery();
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Saves the history to disk.
        /// </summary>
        public static void SaveHistory()
        {
            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[History] Saving history to: {HistoryPath}, Count: {History.Count}");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string contents = JsonSerializer.Serialize(History, options);
                File.WriteAllText(HistoryPath, contents);
                AbsurdelyBetterDeliveryMod.DebugLog($"[History] History saved successfully. File size: {new FileInfo(HistoryPath).Length} bytes");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[History] Failed to save history: " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the history from disk.
        /// </summary>
        private static void LoadHistory()
        {
            if (!File.Exists(HistoryPath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(HistoryPath);
                History = JsonSerializer.Deserialize<List<DeliveryRecord>>(json) ?? new List<DeliveryRecord>();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[History] Failed to load history: " + ex.Message);
            }
        }

        #endregion

        #region Data Extraction

        /// <summary>
        /// Extracts destination, dock, and price from a delivery.
        /// </summary>
        private static void ExtractDeliveryDetails(DeliveryInstance delivery, DeliveryRecord record)
        {
            try
            {
                // Extract destination code
                ExtractDestination(delivery, record);
                
                // Extract loading dock index
                ExtractLoadingDock(delivery, record);
                
                // Extract price from tracker
                ExtractPrice(delivery, record);

                AbsurdelyBetterDeliveryMod.DebugLog($"[History] Captured Destination: {record.Destination}, Dock: {record.LoadingDockIndex}, Price: {record.TotalPrice}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[History] Failed to capture destination/dock/price: " + ex.Message);
            }
        }

        /// <summary>
        /// Extracts the destination from a delivery.
        /// </summary>
        private static void ExtractDestination(DeliveryInstance delivery, DeliveryRecord record)
        {
            var destProp = delivery.GetType().GetProperty("DestinationCode");
            if (destProp != null)
            {
                record.Destination = destProp.GetValue(delivery)?.ToString() ?? "";
                return;
            }

            // Try alternative property names
            var altProp = delivery.GetType().GetProperty("Destination")
                ?? delivery.GetType().GetProperty("Target")
                ?? delivery.GetType().GetProperty("DropOffPoint");

            if (altProp != null)
            {
                record.Destination = altProp.GetValue(delivery)?.ToString() ?? "";
            }
        }

        /// <summary>
        /// Extracts the loading dock index from a delivery.
        /// </summary>
        private static void ExtractLoadingDock(DeliveryInstance delivery, DeliveryRecord record)
        {
            var dockProp = delivery.GetType().GetProperty("LoadingDockIndex");
            if (dockProp != null)
            {
                var value = dockProp.GetValue(delivery);
                if (value != null)
                {
                    record.LoadingDockIndex = (int)value;
                }
            }
        }

        /// <summary>
        /// Extracts the price from the price tracker.
        /// </summary>
        private static void ExtractPrice(DeliveryInstance delivery, DeliveryRecord record)
        {
            if (DeliveryPriceTracker.PendingPrices.TryGetValue(delivery.StoreName, out var price))
            {
                record.TotalPrice = price;
                DeliveryPriceTracker.PendingPrices.Remove(delivery.StoreName);
                AbsurdelyBetterDeliveryMod.DebugLog($"[History] Captured TotalPrice: {record.TotalPrice} from price tracker");
            }
            else
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[History] No price found in tracker for " + delivery.StoreName);
            }
        }

        /// <summary>
        /// Extracts items from a delivery.
        /// </summary>
        private static void ExtractItems(DeliveryInstance delivery, DeliveryRecord record)
        {
            if (delivery.Items == null)
            {
                return;
            }

            foreach (var item in (Il2CppArrayBase<StringIntPair>)(object)delivery.Items)
            {
                var deliveryItem = ParseItem(item);
                record.Items.Add(deliveryItem);
            }
        }

        /// <summary>
        /// Parses a StringIntPair into a DeliveryItem.
        /// </summary>
        private static DeliveryItem ParseItem(StringIntPair item)
        {
            string name = "Unknown";
            int quantity = 0;

            try
            {
                var type = item.GetType();

                // Try properties first
                name = GetStringValue(type, item, "String", "Key", "Item1") ?? "Unknown";
                quantity = GetIntValue(type, item, "Int", "Value", "Item2");
            }
            catch (Exception ex)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[History] Error parsing item: " + ex.Message);
            }

            return new DeliveryItem { Name = name, Quantity = quantity };
        }

        /// <summary>
        /// Gets a string value from an object using multiple possible property/field names.
        /// </summary>
        private static string? GetStringValue(Type type, object obj, params string[] names)
        {
            foreach (var name in names)
            {
                // Try property
                var prop = type.GetProperty(name);
                if (prop != null)
                {
                    return prop.GetValue(obj)?.ToString();
                }

                // Try field
                var field = type.GetField(name);
                if (field != null)
                {
                    return field.GetValue(obj)?.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Gets an int value from an object using multiple possible property/field names.
        /// </summary>
        private static int GetIntValue(Type type, object obj, params string[] names)
        {
            foreach (var name in names)
            {
                // Try property
                var prop = type.GetProperty(name);
                if (prop != null)
                {
                    var val = prop.GetValue(obj);
                    if (val != null) return (int)val;
                }

                // Try field
                var field = type.GetField(name);
                if (field != null)
                {
                    var val = field.GetValue(obj);
                    if (val != null) return (int)val;
                }
            }

            return 0;
        }

        #endregion
    }
}