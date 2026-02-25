// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Linq;
using AbsurdelyBetterDelivery.Managers;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Multiplayer
{
    /// <summary>
    /// Service responsible for client-side state synchronization.
    /// Receives state from host and sends local changes to host.
    /// </summary>
    public static class ClientSyncService
    {
        private static bool _initialized = false;

        #region Initialization

        public static void Initialize()
        {
            if (_initialized)
            {
                MelonLogger.Warning("[ClientSync] Already initialized!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[ClientSync] Initializing client sync service...");
            
            // Request initial state from host
            RequestFullState();
            
            _initialized = true;
            MelonLogger.Msg("[ClientSync] Client sync service initialized.");
        }

        public static void Shutdown()
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog("[ClientSync] Shutting down client sync service...");
            
            _initialized = false;
        }

        #endregion

        #region Requests

        /// <summary>
        /// Requests the full state from the host.
        /// Called on join or when sync is lost.
        /// </summary>
        public static void RequestFullState()
        {
            if (!_initialized)
            {
                MelonLogger.Warning("[ClientSync] Cannot request state - not initialized!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[ClientSync] Requesting full state from host...");

            // Request full state from server via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.RequestFullStateFromServer();
            }
            else
            {
                MelonLogger.Warning("[ClientSync] ModSyncBehaviour not available, cannot request state");
            }
        }

        #endregion

        #region Sending Updates

        /// <summary>
        /// Sends a favorite toggle to the host.
        /// </summary>
        public static void SendFavoriteUpdate(string recordId, bool isFavorite)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Sending favorite update: {recordId} = {isFavorite}");

            var message = new FavoriteUpdateMessage
            {
                SenderId = "client",
                RecordId = recordId,
                IsFavorite = isFavorite
            };

            // Send to server via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.SendToServer(message);
            }
            else
            {
                MelonLogger.Warning("[ClientSync] ModSyncBehaviour not available for favorite update");
            }
        }

        /// <summary>
        /// Sends a recurring order update to the host.
        /// </summary>
        public static void SendRecurringOrderUpdate(string recordId, bool isRecurring, Models.RecurringSettings? settings)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Sending recurring order update: {recordId} = {isRecurring}");

            var message = new RecurringOrderUpdateMessage
            {
                SenderId = "client",
                RecordId = recordId,
                IsRecurring = isRecurring,
                Settings = settings
            };

            // Send to server via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.SendToServer(message);
            }
            else
            {
                MelonLogger.Warning("[ClientSync] ModSyncBehaviour not available for recurring update");
            }
        }

        /// <summary>
        /// Requests the host to execute a recurring order.
        /// </summary>
        public static void RequestExecuteRecurringOrder(string recordId)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Requesting recurring order execution: {recordId}");

            var message = new ExecuteRecurringOrderMessage
            {
                SenderId = "client",
                RecordId = recordId
            };

            // Send execution request to server via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.SendToServer(message);
            }
            else
            {
                MelonLogger.Warning("[ClientSync] ModSyncBehaviour not available for execution request");
            }
            MelonLogger.Msg($"[ClientSync] Requesting host to execute order {recordId} (network sync pending)");
        }

        #endregion

        #region Message Handling

        /// <summary>
        /// Handles incoming messages from the server via Steam P2P.
        /// Alias for HandleHostMessage.
        /// </summary>
        public static void HandleServerMessage(NetworkMessage message)
        {
            HandleHostMessage(message);
        }

        /// <summary>
        /// Handles incoming messages from the host.
        /// </summary>
        public static void HandleHostMessage(NetworkMessage message)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received message from host: {message.Type}");

            switch (message.Type)
            {
                case MessageType.FullStateSync:
                    HandleFullStateSync((FullStateSyncMessage)message);
                    break;

                case MessageType.HistoryUpdate:
                    HandleHistoryUpdate((HistoryUpdateMessage)message);
                    break;

                case MessageType.FavoriteUpdate:
                    HandleFavoriteUpdate((FavoriteUpdateMessage)message);
                    break;

                case MessageType.RecurringOrderUpdate:
                    HandleRecurringOrderUpdate((RecurringOrderUpdateMessage)message);
                    break;

                case MessageType.RecurringOrderResult:
                    HandleRecurringOrderResult((RecurringOrderResultMessage)message);
                    break;

                case MessageType.TimeMultiplierSync:
                    HandleTimeMultiplierSync((TimeMultiplierSyncMessage)message);
                    break;

                case MessageType.ClearData:
                    HandleClearData((ClearDataMessage)message);
                    break;

                default:
                    MelonLogger.Warning($"[ClientSync] Unhandled message type: {message.Type}");
                    break;
            }
        }

        private static void HandleFullStateSync(FullStateSyncMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received full state: {message.History.Count} records, {message.RecurringOrders.Count} recurring, TimeMultiplier={message.TimeMultiplier:F3}x");
            
            // Apply TimeMultiplier from host
            float oldMultiplier = AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value;
            if (Math.Abs(oldMultiplier - message.TimeMultiplier) > 0.001f)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Syncing TimeMultiplier from host: {oldMultiplier:F3}x → {message.TimeMultiplier:F3}x");
                AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value = message.TimeMultiplier;
                MelonLogger.Msg($"[ClientSync] Time multiplier synchronized from host: {message.TimeMultiplier:F3}x");
            }
            
            // Log received favorites and recurring
            int favoriteCount = message.History.Count(r => r.IsFavorite);
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received state: {favoriteCount} favorites, {message.RecurringOrders.Count} recurring orders");
            
            foreach (var record in message.History.Where(r => r.IsFavorite))
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync]   - Favorite: {record.StoreName} (ID: {record.ID})");
            }
            
            foreach (var recurring in message.RecurringOrders)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync]   - Recurring: {recurring.RecordID}, Type={recurring.RecurringType}");
            }
            
            // Replace local state with host's state
            DeliveryHistoryManager.History.Clear();
            DeliveryHistoryManager.History.AddRange(message.History);
            
            // Apply recurring order settings
            foreach (var orderData in message.RecurringOrders)
            {
                var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == orderData.RecordID);
                if (record != null)
                {
                    record.RecurringSettings = new Models.RecurringSettings
                    {
                        Type = orderData.RecurringType,
                        Hour = orderData.Hour ?? 0,
                        Minute = orderData.Minute ?? 0,
                        DayOfWeek = orderData.DayOfWeek ?? System.DayOfWeek.Monday
                    };
                }
            }
            
            // Refresh UI
            if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
            {
                UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
            }
            
            MelonLogger.Msg($"[ClientSync] State synced successfully: {message.History.Count} history items");
        }

        private static void HandleHistoryUpdate(HistoryUpdateMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received history update from host: {message.UpdateType} for {message.Record.StoreName}");
            
            switch (message.UpdateType)
            {
                case HistoryUpdateType.Add:
                    // Check if record already exists (may have been created locally)
                    var existingAdd = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.Record.ID);
                    if (existingAdd != null)
                    {
                        // Update existing record with host's data (preserves host's price etc.)
                        int index = DeliveryHistoryManager.History.IndexOf(existingAdd);
                        DeliveryHistoryManager.History[index] = message.Record;
                        AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Updated existing record {message.Record.ID} with host data");
                    }
                    else
                    {
                        DeliveryHistoryManager.History.Insert(0, message.Record);
                        AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Added record {message.Record.ID} to local history");
                    }
                    break;
                    
                case HistoryUpdateType.Update:
                    var existing = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.Record.ID);
                    if (existing != null)
                    {
                        int index = DeliveryHistoryManager.History.IndexOf(existing);
                        DeliveryHistoryManager.History[index] = message.Record;
                        AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Updated record {message.Record.ID} in local history");
                    }
                    break;
                    
                case HistoryUpdateType.Remove:
                    DeliveryHistoryManager.History.RemoveAll(r => r.ID == message.Record.ID);
                    AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Removed record {message.Record.ID} from local history");
                    break;
            }
            
            // Refresh UI
            if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
            {
                UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
            }
        }

        private static void HandleFavoriteUpdate(FavoriteUpdateMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received favorite update from host: {message.RecordId} = {message.IsFavorite}");
            
            var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.RecordId);
            if (record != null)
            {
                record.IsFavorite = message.IsFavorite;
                
                // Refresh UI
                if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
                {
                    UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Applied favorite state for {message.RecordId}");
            }
            else
            {
                MelonLogger.Warning($"[ClientSync] Record {message.RecordId} not found for favorite update");
            }
        }

        private static void HandleRecurringOrderUpdate(RecurringOrderUpdateMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received recurring order update from host: {message.RecordId} = {message.IsRecurring}");
            
            var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.RecordId);
            if (record != null)
            {
                record.RecurringSettings = message.Settings;
                
                // Refresh UI
                if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
                {
                    UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Applied recurring settings for {message.RecordId}");
            }
            else
            {
                MelonLogger.Warning($"[ClientSync] Record {message.RecordId} not found for recurring update");
            }
        }

        private static void HandleRecurringOrderResult(RecurringOrderResultMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received recurring order result from host: {message.RecordId} = {(message.Success ? "SUCCESS" : "FAILED")}");
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Recurring order result: {message.RecordId} = {message.Success} ({message.Message})");
            
            if (message.Success)
            {
                MelonLogger.Msg($"[ClientSync] Recurring order executed successfully on host");
            }
            else
            {
                MelonLogger.Warning($"[ClientSync] Recurring order failed on host: {message.Message}");
            }
        }

        private static void HandleTimeMultiplierSync(TimeMultiplierSyncMessage message)
        {
            float oldValue = AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value;
            float newValue = message.Multiplier;
            
            AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Received time multiplier sync from host: {newValue:F3}x (local was: {oldValue:F3}x)");
            
            if (Math.Abs(oldValue - newValue) > 0.001f)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Overriding local time multiplier: {oldValue:F3}x → {newValue:F3}x");
                AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value = newValue;
                MelonLogger.Msg($"[ClientSync] Time multiplier synchronized from host: {newValue:F3}x");
            }
            else
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ClientSync] Time multiplier already in sync: {newValue:F3}x");
            }
        }

        private static void HandleClearData(ClearDataMessage message)
        {
            MelonLogger.Msg("[ClientSync] Received clear data command from host - clearing local data...");
            
            // Clear history
            DeliveryHistoryManager.ClearHistory();
            
            // Refresh UI
            if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
            {
                UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
            }
            
            MelonLogger.Msg("[ClientSync] Local data cleared successfully");
        }

        #endregion
    }
}