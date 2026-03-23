// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Patches;
using AbsurdelyBetterDelivery.Utils;
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
        private static bool _sessionInitialized;

        /// <summary>
        /// Set to true when the game fires its own onSaveComplete event during a session.
        /// If the player returns to menu without having saved, we roll history back to the
        /// session-start snapshot so that unsaved deliveries don't persist.
        /// </summary>
        private static bool _gameSavedThisSession;

        private static readonly Dictionary<string, Queue<string>> PendingSuppressedRebuyByLocation = new(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Properties

        /// <summary>
        /// Path to the history JSON file for the current save.
        /// </summary>
        private static string HistoryPath => 
            Path.Combine(MelonEnvironment.UserDataDirectory, $"DeliveryHistory_{_currentSaveName}.json");

        /// <summary>
        /// Path to the per-session backup used for crash recovery.
        /// </summary>
        private static string SessionBackupPath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, $"DeliveryHistory_{_currentSaveName}.session.bak");

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
            if (_sessionInitialized && !string.Equals(_currentSaveName, saveName, StringComparison.OrdinalIgnoreCase))
            {
                CommitSession();
            }

            _currentSaveName = saveName;
            _gameSavedThisSession = false;

            // Clear per-session rebuy suppression state so it does not bleed into a new save.
            PendingSuppressedRebuyByLocation.Clear();

            RecoverHistoryAfterUnexpectedExit();
            LoadHistory();
            CreateSessionBackupSnapshot();
            _sessionInitialized = true;
        }

        /// <summary>
        /// Commits the current session history and clears crash-recovery backup.
        /// Call this on graceful exits (menu return / application quit).
        /// </summary>
        /// <remarks>
        /// If the game was saved at least once this session, the current history (including
        /// new deliveries) is kept as-is on disk. If the game was never saved, we restore
        /// history to the session-start snapshot so that deliveries from an unsaved session
        /// do not bleed into future loads of the same save slot.
        /// </remarks>
        public static void CommitSession()
        {
            try
            {
                if (!_gameSavedThisSession && File.Exists(SessionBackupPath))
                {
                    // The game was never saved this session; discard any history changes made
                    // since the session started by restoring the start-of-session snapshot.
                    string backupJson = File.ReadAllText(SessionBackupPath);
                    List<DeliveryRecord> baseline = DeserializeHistory(backupJson);
                    History = baseline;
                    SaveHistory();
                    MelonLogger.Msg($"[History] Session ended without a game save — rolled back to session-start baseline ({baseline.Count} entries) for save '{_currentSaveName}'.");
                }
                else
                {
                    // Game was saved; current history is already up-to-date on disk.
                    SaveHistory();
                }

                if (File.Exists(SessionBackupPath))
                {
                    File.Delete(SessionBackupPath);
                }

                _sessionInitialized = false;
                _gameSavedThisSession = false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[History] Failed to commit session: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the game fires its onSaveComplete event.
        /// Marks that at least one game save has occurred this session so
        /// CommitSession() will keep the current history rather than rolling back.
        /// </summary>
        public static void OnGameSaved()
        {
            _gameSavedThisSession = true;
            // Update the session backup to the newly-saved state so that a subsequent
            // crash would recover to the most recent game-save point, not the session start.
            CreateSessionBackupSnapshot();
            MelonLogger.Msg($"[History] Game saved — session checkpoint updated ({History.Count} entries) for save '{_currentSaveName}'.");
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

        /// <summary>
        /// Registers a repurchase request so completion can update the existing favorite/recurring record
        /// instead of creating a new history entry.
        /// </summary>
        /// <param name="sourceRecord">Source record used for repurchase.</param>
        public static void RegisterSuppressedRebuy(DeliveryRecord sourceRecord)
        {
            if (sourceRecord == null)
            {
                return;
            }

            if (!sourceRecord.IsFavorite && !sourceRecord.IsRecurring)
            {
                return;
            }

            string key = BuildLocationKey(sourceRecord.StoreName, sourceRecord.Destination, sourceRecord.LoadingDockIndex);
            if (!PendingSuppressedRebuyByLocation.TryGetValue(key, out Queue<string>? queue))
            {
                queue = new Queue<string>();
                PendingSuppressedRebuyByLocation[key] = queue;
            }

            queue.Enqueue(sourceRecord.ID);

            AbsurdelyBetterDeliveryMod.DebugLog(
                $"[History] Registered suppressed rebuy for ID={sourceRecord.ID}, key={key}, favorite={sourceRecord.IsFavorite}, recurring={sourceRecord.IsRecurring}");
        }

        /// <summary>
        /// Tries to resolve a completion to a previously registered favorite/recurring rebuy marker.
        /// </summary>
        /// <param name="delivery">Completed delivery instance.</param>
        /// <param name="matchedRecord">Matched source record when found.</param>
        /// <returns><c>true</c> if completion should skip creating a new history entry.</returns>
        public static bool TryConsumeSuppressedRebuy(DeliveryInstance delivery, out DeliveryRecord? matchedRecord)
        {
            matchedRecord = null;
            if (delivery == null)
            {
                return false;
            }

            string key = BuildLocationKey(delivery.StoreName, delivery.DestinationCode, delivery.LoadingDockIndex);
            if (!PendingSuppressedRebuyByLocation.TryGetValue(key, out Queue<string>? queue) || queue.Count == 0)
            {
                return false;
            }

            while (queue.Count > 0)
            {
                string recordId = queue.Dequeue();
                DeliveryRecord? record = FindRecordById(recordId);
                if (record == null)
                {
                    continue;
                }

                if (!record.IsFavorite && !record.IsRecurring)
                {
                    continue;
                }

                matchedRecord = record;
                if (queue.Count == 0)
                {
                    PendingSuppressedRebuyByLocation.Remove(key);
                }

                return true;
            }

            PendingSuppressedRebuyByLocation.Remove(key);
            return false;
        }

        /// <summary>
        /// Updates an existing record after a favorite/recurring rebuy completion without creating a new record.
        /// </summary>
        /// <param name="record">Existing history record.</param>
        /// <param name="delivery">Completed delivery instance.</param>
        public static void ApplyCompletionToExistingRecord(DeliveryRecord record, DeliveryInstance delivery)
        {
            if (record == null || delivery == null)
            {
                return;
            }

            // Keep stable identity (ID) so favorite/recurring references remain intact.
            record.StoreName = delivery.StoreName ?? record.StoreName;
            record.Timestamp = DateTime.Now;

            ExtractDeliveryDetails(delivery, record);
            record.Items.Clear();
            ExtractItems(delivery, record);

            SaveHistory();
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
                History = new List<DeliveryRecord>();
                return;
            }

            try
            {
                string json = File.ReadAllText(HistoryPath);
                History = DeserializeHistory(json);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[History] Failed to load history: " + ex.Message);
                History = new List<DeliveryRecord>();
            }
        }

        /// <summary>
        /// Restores history from session backup if the previous run ended unexpectedly.
        /// </summary>
        private static void RecoverHistoryAfterUnexpectedExit()
        {
            if (!File.Exists(SessionBackupPath))
            {
                return;
            }

            try
            {
                string backupJson = File.ReadAllText(SessionBackupPath);
                List<DeliveryRecord> recovered = DeserializeHistory(backupJson);

                History = recovered;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(HistoryPath, JsonSerializer.Serialize(recovered, options));

                File.Delete(SessionBackupPath);
                MelonLogger.Msg($"[History] Recovered from crash backup for save '{_currentSaveName}'. Restored {recovered.Count} entries.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[History] Failed crash recovery for save '{_currentSaveName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a snapshot of the starting history state for crash recovery.
        /// </summary>
        private static void CreateSessionBackupSnapshot()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string contents = JsonSerializer.Serialize(History, options);
                File.WriteAllText(SessionBackupPath, contents);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[History] Failed to create session backup: {ex.Message}");
            }
        }

        /// <summary>
        /// Deserializes history JSON safely.
        /// </summary>
        private static List<DeliveryRecord> DeserializeHistory(string json)
        {
            return JsonSerializer.Deserialize<List<DeliveryRecord>>(json) ?? new List<DeliveryRecord>();
        }

        private static DeliveryRecord? FindRecordById(string recordId)
        {
            if (string.IsNullOrWhiteSpace(recordId))
            {
                return null;
            }

            for (int i = 0; i < History.Count; i++)
            {
                DeliveryRecord record = History[i];
                if (record != null && string.Equals(record.ID, recordId, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }

            return null;
        }

        private static string BuildLocationKey(string storeName, string destination, int loadingDockIndex)
        {
            return $"{NormalizeForMatch(storeName)}|{NormalizeForMatch(destination)}|{loadingDockIndex}";
        }

        // Delegates to the shared utility — single implementation lives in NameFormatter.
        private static string NormalizeForMatch(string value) => NameFormatter.NormalizeForMatch(value);

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
            // Verified in IL2CPP: DeliveryInstance.DestinationCode is a direct field-backed property.
            record.Destination = delivery.DestinationCode ?? string.Empty;
        }

        /// <summary>
        /// Extracts the loading dock index from a delivery.
        /// </summary>
        private static void ExtractLoadingDock(DeliveryInstance delivery, DeliveryRecord record)
        {
            // Verified in IL2CPP: DeliveryInstance.LoadingDockIndex is a direct field-backed property.
            record.LoadingDockIndex = delivery.LoadingDockIndex;
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
            // Verified in IL2CPP: StringIntPair.String and StringIntPair.Int are field-backed properties.
            return new DeliveryItem
            {
                Name = item.String ?? "Unknown",
                Quantity = item.Int
            };
        }

        #endregion
    }
}