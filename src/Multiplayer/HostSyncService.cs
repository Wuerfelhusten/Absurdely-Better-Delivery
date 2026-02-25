// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Linq;
using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Services;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Multiplayer
{
    /// <summary>
    /// Service responsible for host-side state synchronization.
    /// Broadcasts state changes to all connected clients.
    /// </summary>
    public static class HostSyncService
    {
        private static bool _initialized = false;

        #region Initialization

        public static void Initialize()
        {
            if (_initialized)
            {
                MelonLogger.Warning("[HostSync] Already initialized!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[HostSync] Initializing host sync service...");
            
            // TODO: Register network message handlers
            // TODO: Set up event listeners for state changes
            
            _initialized = true;
            MelonLogger.Msg("[HostSync] Host sync service initialized.");
            
            // Schedule initial time multiplier broadcast after ModSyncBehaviour is ready
            MelonLoader.MelonCoroutines.Start(DelayedInitialBroadcast());
        }
        
        /// <summary>
        /// Delays initial broadcast until ModSyncBehaviour is initialized
        /// </summary>
        private static System.Collections.IEnumerator DelayedInitialBroadcast()
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[HostSync] Waiting for ModSyncBehaviour to be ready...");
            
            // Wait for ModSyncBehaviour to be fully initialized - check both Instance and IsReady
            int attempts = 0;
            while ((ModSyncBehaviour.Instance == null || !ModSyncBehaviour.Instance.IsReady) && attempts < 50)
            {
                float waitStart = UnityEngine.Time.realtimeSinceStartup;
                while (UnityEngine.Time.realtimeSinceStartup - waitStart < 0.1f)
                {
                    yield return null;
                }
                attempts++;
            }
            
            if (ModSyncBehaviour.Instance == null || !ModSyncBehaviour.Instance.IsReady)
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available after 5 seconds, skipping initial broadcast");
                yield break;
            }
            
            AbsurdelyBetterDeliveryMod.DebugLog("[HostSync] ModSyncBehaviour ready, broadcasting initial state...");
            
            // Broadcast initial time multiplier
            float currentMultiplier = AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value;
            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Broadcasting initial time multiplier: {currentMultiplier:F3}x");
            BroadcastTimeMultiplier(currentMultiplier);
        }

        public static void Shutdown()
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog("[HostSync] Shutting down host sync service...");
            
            // TODO: Unregister handlers
            
            _initialized = false;
        }

        #endregion

        #region Broadcasting

        /// <summary>
        /// Broadcasts the complete game state to all clients.
        /// Called when a new client joins or on request.
        /// </summary>
        public static void BroadcastFullState()
        {
            if (!_initialized)
            {
                MelonLogger.Warning("[HostSync] Cannot broadcast - not initialized!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[HostSync] Broadcasting full state to all clients...");

            var message = new FullStateSyncMessage
            {
                SenderId = "host",
                History = DeliveryHistoryManager.History.ToList(),
                RecurringOrders = DeliveryHistoryManager.History
                    .Where(r => r.IsRecurring && r.RecurringSettings != null)
                    .Select(r => new RecurringOrderData
                    {
                        RecordID = r.ID,
                        RecurringType = r.RecurringSettings!.Type,
                        Hour = r.RecurringSettings.Hour,
                        Minute = r.RecurringSettings.Minute,
                        DayOfWeek = r.RecurringSettings.DayOfWeek
                    })
                    .ToList(),
                TimeMultiplier = AbsurdelyBetterDeliveryMod.DeliveryTimeMultiplier.Value
            };

            // Log favorites and recurring for debugging
            int favoriteCount = message.History.Count(r => r.IsFavorite);
            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] State prepared: {message.History.Count} records, {favoriteCount} favorites, {message.RecurringOrders.Count} recurring orders");
            
            foreach (var record in message.History.Where(r => r.IsFavorite || r.IsRecurring))
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync]   - {record.StoreName}: Favorite={record.IsFavorite}, Recurring={record.IsRecurring}, RecurringSettings={record.RecurringSettings?.Type}");
            }
            
            // Broadcast to all clients via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.BroadcastToClients(message);
                MelonLogger.Msg($"[HostSync] Broadcasted full state: {message.History.Count} history items");
            }
            else
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available, cannot broadcast state");
            }
        }

        /// <summary>
        /// Broadcasts a history update to all clients.
        /// </summary>
        public static void BroadcastHistoryUpdate(Models.DeliveryRecord record, HistoryUpdateType updateType)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Broadcasting history update: {updateType} for {record.StoreName}");

            var message = new HistoryUpdateMessage
            {
                SenderId = "host",
                Record = record,
                UpdateType = updateType
            };

            // Broadcast to all clients via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.BroadcastToClients(message);
            }
            else
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available for history update");
            }
        }

        /// <summary>
        /// Broadcasts a recurring order update to all clients.
        /// </summary>
        public static void BroadcastRecurringOrderUpdate(string recordId, bool isRecurring, Models.RecurringSettings? settings)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Broadcasting recurring order update for {recordId}: {isRecurring}");

            var message = new RecurringOrderUpdateMessage
            {
                SenderId = "host",
                RecordId = recordId,
                IsRecurring = isRecurring,
                Settings = settings
            };

            // Broadcast to all clients via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.BroadcastToClients(message);
            }
            else
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available for recurring update");
            }
        }

        /// <summary>
        /// Broadcasts a favorite update to all clients.
        /// </summary>
        public static void BroadcastFavoriteUpdate(string recordId, bool isFavorite)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Broadcasting favorite update for {recordId}: {isFavorite}");

            var message = new FavoriteUpdateMessage
            {
                SenderId = "host",
                RecordId = recordId,
                IsFavorite = isFavorite
            };

            // Broadcast to all clients via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.BroadcastToClients(message);
            }
            else
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available for favorite update");
            }
        }

        /// <summary>
        /// Broadcasts time multiplier to all clients.
        /// Clients will override their local setting with host's value.
        /// </summary>
        public static void BroadcastTimeMultiplier(float multiplier)
        {
            if (!_initialized)
            {
                MelonLogger.Warning("[HostSync] Cannot broadcast time multiplier - not initialized!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Broadcasting time multiplier to all clients: {multiplier:F3}x");

            var message = new TimeMultiplierSyncMessage
            {
                SenderId = "host",
                Multiplier = multiplier
            };

            // Broadcast to all clients via FishNet
            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.BroadcastToClients(message);
                AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Time multiplier broadcast sent: {multiplier:F3}x");
            }
            else
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available for time multiplier sync");
            }
        }

        /// <summary>
        /// Broadcasts a clear data command to all clients.
        /// Clients will clear their local history, favorites, and recurring orders.
        /// </summary>
        public static void BroadcastClearData()
        {
            if (!_initialized)
            {
                MelonLogger.Warning("[HostSync] Cannot broadcast clear data - not initialized!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[HostSync] Broadcasting clear data to all clients...");

            var message = new ClearDataMessage
            {
                SenderId = "host"
            };

            if (ModSyncBehaviour.Instance != null)
            {
                ModSyncBehaviour.Instance.BroadcastToClients(message);
                MelonLogger.Msg("[HostSync] Clear data broadcast sent to all clients");
            }
            else
            {
                MelonLogger.Warning("[HostSync] ModSyncBehaviour not available for clear data sync");
            }
        }

        #endregion

        #region Message Handling

        /// <summary>
        /// Handles incoming messages from clients (via Steam P2P).
        /// </summary>
        public static void HandleClientMessage(NetworkMessage message, string steamId)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Received message from Steam client {steamId}: {message.Type}");
            
            // Override sender ID with Steam ID if not set
            if (string.IsNullOrEmpty(message.SenderId))
            {
                message.SenderId = steamId;
            }

            HandleClientMessage(message);
        }

        /// <summary>
        /// Handles incoming messages from clients.
        /// </summary>
        public static void HandleClientMessage(NetworkMessage message)
        {
            if (!_initialized) return;

            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Received message from client: {message.Type}");

            switch (message.Type)
            {
                case MessageType.RequestFullState:
                    HandleFullStateRequest(message.SenderId);
                    break;

                case MessageType.FavoriteUpdate:
                    HandleFavoriteUpdate((FavoriteUpdateMessage)message);
                    break;

                case MessageType.RecurringOrderUpdate:
                    HandleRecurringOrderUpdate((RecurringOrderUpdateMessage)message);
                    break;

                case MessageType.ExecuteRecurringOrder:
                    HandleExecuteRecurringOrder((ExecuteRecurringOrderMessage)message);
                    break;

                default:
                    MelonLogger.Warning($"[HostSync] Unhandled message type: {message.Type}");
                    break;
            }
        }

        private static void HandleFullStateRequest(string clientId)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Client {clientId} requested full state");
            BroadcastFullState();
        }

        private static void HandleFavoriteUpdate(FavoriteUpdateMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Client {message.SenderId} toggled favorite: {message.RecordId} = {message.IsFavorite}");
            
            var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.RecordId);
            if (record != null)
            {
                record.IsFavorite = message.IsFavorite;
                DeliveryHistoryManager.SaveHistory();
                
                // Refresh host's own UI
                if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
                {
                    UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Saved favorite state, broadcasting to other clients...");
                // Broadcast to all OTHER clients
                BroadcastFavoriteUpdate(message.RecordId, message.IsFavorite);
            }
            else
            {
                MelonLogger.Warning($"[HostSync] Record {message.RecordId} not found for favorite update from {message.SenderId}");
            }
        }

        private static void HandleRecurringOrderUpdate(RecurringOrderUpdateMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Client {message.SenderId} toggled recurring: {message.RecordId} = {message.IsRecurring}");
            
            var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.RecordId);
            if (record != null)
            {
                record.RecurringSettings = message.Settings;
                
                RecurringOrderService.SaveRecurringOrders();
                
                // Refresh host's own UI
                if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
                {
                    UI.DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Saved recurring settings, broadcasting to other clients...");
                // Broadcast to all OTHER clients
                BroadcastRecurringOrderUpdate(message.RecordId, message.IsRecurring, message.Settings);
            }
            else
            {
                MelonLogger.Warning($"[HostSync] Record {message.RecordId} not found for recurring update from {message.SenderId}");
            }
        }

        private static void HandleExecuteRecurringOrder(ExecuteRecurringOrderMessage message)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Client {message.SenderId} requested recurring order execution: {message.RecordId}");
            
            var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == message.RecordId);
            if (record != null)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Executing recurring order for client: {record.StoreName}");
                // Execute on host
                bool success = RepurchaseService.RepurchaseRecord(record, AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
                
                // Send result back to client
                var result = new RecurringOrderResultMessage
                {
                    SenderId = "host",
                    RecordId = message.RecordId,
                    Success = success,
                    Message = success ? "Order placed successfully" : "Failed to place order"
                };
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[HostSync] Recurring order execution result for {record.StoreName}: {(success ? "SUCCESS" : "FAILED")}");
                // TODO: Send result message to requesting client
            }
        }

        #endregion
    }
}