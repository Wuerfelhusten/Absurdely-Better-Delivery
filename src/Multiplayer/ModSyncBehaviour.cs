// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Text.Json;
using Il2CppFishNet.Managing;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;

namespace AbsurdelyBetterDelivery.Multiplayer
{
    /// <summary>
    /// Network sync component using FishNet's Broadcast system for mod data synchronization.
    /// Handles all network communication between host and clients.
    /// </summary>
    public class ModSyncBehaviour : MonoBehaviour
    {
        #region Constructor
        
        // Il2Cpp requires explicit constructor with IntPtr
        public ModSyncBehaviour(IntPtr ptr) : base(ptr) { }
        
        #endregion
        
        #region Singleton

        private static ModSyncBehaviour? _instance;

        /// <summary>
        /// Gets the singleton instance of ModSyncBehaviour.
        /// </summary>
        public static ModSyncBehaviour? Instance => _instance;

        #endregion

        #region Fields

        private NetworkManager? _networkManager;
        private bool _isInitialized = false;
        
        /// <summary>
        /// Gets whether the sync behaviour is fully initialized and ready.
        /// </summary>
        public bool IsReady => _isInitialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Duplicate ModSyncBehaviour detected, destroying...");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] ModSyncBehaviour created");
        }

        private float _clientRefreshTimer = 0f;
        private const float CLIENT_REFRESH_INTERVAL = 2f; // Refresh every 2 seconds
        
        private void Start()
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Start() called");
            InitializeNetworking();
            AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Start() completed");
        }
        
        private void Update()
        {
            // Poll for Steam P2P messages every frame
            if (_isInitialized)
            {
                SteamP2PSync.PollMessages();
                
                // Periodically refresh client list for host
                _clientRefreshTimer += UnityEngine.Time.deltaTime;
                if (_clientRefreshTimer >= CLIENT_REFRESH_INTERVAL)
                {
                    _clientRefreshTimer = 0f;
                    SteamP2PSync.RefreshClientList();
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] ModSyncBehaviour destroyed");
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes FishNet networking connection and Steam P2P.
        /// </summary>
        private void InitializeNetworking()
        {
            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] InitializeNetworking started");
                
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Searching for NetworkManager...");
                _networkManager = UnityEngine.Object.FindObjectOfType<NetworkManager>();
                AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] NetworkManager search result: {(_networkManager != null ? "FOUND" : "NULL")}");
                
                if (_networkManager == null)
                {
                    MelonLogger.Warning("[ModSync] NetworkManager not found!");
                    return;
                }

                // Initialize Steam P2P sync
                bool isServer = _networkManager.IsServer;
                AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Initializing Steam P2P as {(isServer ? "HOST" : "CLIENT")}...");
                SteamP2PSync.Initialize(isServer);

                _isInitialized = true;
                
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Checking IsServer...");
                if (_networkManager.IsServer)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Initialized as SERVER");
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Checking IsClient...");
                if (_networkManager.IsClient)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Initialized as CLIENT");
                    
                    // Client requests initial state from server
                    if (!_networkManager.IsServer)
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Client will request initial state...");
                        // Delay request slightly to ensure connection is fully established
                        Invoke(nameof(DelayedFullStateRequest), 0.5f);
                    }
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] InitializeNetworking completed");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Initialization error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Sets up Steam P2P message handlers.
        /// </summary>
        private void RegisterBroadcastHandlers()
        {
            // Steam P2P polling is done in Update() via SteamP2PSync.PollMessages()
            AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Steam P2P sync system ready");
        }

        /// <summary>
        /// Delayed request for full state from server.
        /// </summary>
        private void DelayedFullStateRequest()
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Executing delayed full state request...");
            RequestFullStateFromServer();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Gets whether this instance is the server/host.
        /// </summary>
        public bool IsServer => _networkManager != null && _networkManager.IsServer;

        /// <summary>
        /// Gets whether this instance is a client.
        /// </summary>
        public bool IsClient => _networkManager != null && _networkManager.IsClient;

        /// <summary>
        /// Sends a message from client to server via Steam P2P.
        /// </summary>
        public void SendToServer(NetworkMessage message)
        {
            if (!_isInitialized || _networkManager == null)
            {
                MelonLogger.Warning("[ModSync] Cannot send - not initialized!");
                return;
            }

            if (!IsClient)
            {
                MelonLogger.Warning("[ModSync] SendToServer called but not a client!");
                return;
            }

            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Client sending {message.Type} to server via Steam P2P");
                
                // If we're the host, process locally
                if (IsServer)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Host processing client message locally");
                    string json = SerializeMessage(message);
                    HandleServerReceive(json);
                    return;
                }

                // Send via Steam P2P
                if (SteamP2PSync.SendToHost(message))
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] ✓ Sent {message.Type} to host via Steam P2P");
                }
                else
                {
                    MelonLogger.Warning($"[ModSync] Failed to send {message.Type} via Steam P2P");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Failed to send message to server: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Gets a unique client identifier.
        /// </summary>
        private string GetClientId()
        {
            // Use a hash of the local player name or connection ID
            try
            {
                if (_networkManager?.ClientManager?.Connection != null)
                {
                    return _networkManager.ClientManager.Connection.ClientId.ToString();
                }
            }
            catch { }
            
            return Environment.MachineName.GetHashCode().ToString("X8");
        }

        /// <summary>
        /// Broadcasts a message from server to all clients via Steam P2P.
        /// </summary>
        public void BroadcastToClients(NetworkMessage message)
        {
            if (!_isInitialized || _networkManager == null)
            {
                MelonLogger.Warning("[ModSync] Cannot broadcast - not initialized!");
                return;
            }

            if (!IsServer)
            {
                MelonLogger.Warning("[ModSync] BroadcastToClients called but not a server!");
                return;
            }

            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Server broadcasting {message.Type} to all clients via Steam P2P");
                
                // Send via Steam P2P to all connected clients
                if (SteamP2PSync.BroadcastToClients(message))
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] ✓ Broadcast {message.Type} to clients via Steam P2P");
                }
                else
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Broadcast {message.Type} completed (may have no clients)");
                }
                
                // If we're also a client (host), handle locally too
                if (IsClient)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Host also processing broadcast locally");
                    string json = SerializeMessage(message);
                    HandleClientReceive(json);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Failed to broadcast to clients: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Client requests full state from server.
        /// Safe to call from client only.
        /// </summary>
        public void RequestFullStateFromServer()
        {
            if (!_isInitialized || _networkManager == null)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] RequestFullState skipped - not initialized");
                return;
            }

            if (!IsClient || IsServer)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] RequestFullState skipped (host or not client)");
                return;
            }

            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[ModSync] Client requesting full state from server...");
                
                var message = new NetworkMessage(MessageType.RequestFullState)
                {
                    SenderId = "client"
                };
                
                SendToServer(message);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Failed to request full state: {ex.Message}");
            }
        }

        #endregion

        #region Message Handling

        /// <summary>
        /// Handles a message received by the server from a client.
        /// </summary>
        private void HandleServerReceive(string messageJson)
        {
            try
            {
                var message = DeserializeMessage(messageJson);
                if (message != null)
                {
                    HostSyncService.HandleClientMessage(message);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Server receive error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a message received by a client from the server.
        /// </summary>
        private void HandleClientReceive(string messageJson)
        {
            try
            {
                var message = DeserializeMessage(messageJson);
                if (message != null)
                {
                    ClientSyncService.HandleHostMessage(message);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Client receive error: {ex.Message}");
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes a NetworkMessage to JSON.
        /// </summary>
        private string SerializeMessage(NetworkMessage message)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    IncludeFields = true,
                    WriteIndented = false
                };

                string json = JsonSerializer.Serialize(message, message.GetType(), options);
                AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Serialized {message.Type}: {json.Length} bytes");
                return json;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Serialization error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Deserializes a NetworkMessage from JSON.
        /// Determines the correct type based on the MessageType property.
        /// </summary>
        private NetworkMessage? DeserializeMessage(string json)
        {
            try
            {
                // First, parse to determine the message type
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("Type", out var typeElement))
                {
                    MelonLogger.Error("[ModSync] Message missing Type property");
                    return null;
                }

                var messageType = (MessageType)typeElement.GetInt32();
                AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Deserializing message type: {messageType}");

                var options = new JsonSerializerOptions
                {
                    IncludeFields = true
                };

                // Deserialize to the correct concrete type
                NetworkMessage? message = messageType switch
                {
                    MessageType.FullStateSync => JsonSerializer.Deserialize<FullStateSyncMessage>(json, options),
                    MessageType.HistoryUpdate => JsonSerializer.Deserialize<HistoryUpdateMessage>(json, options),
                    MessageType.RecurringOrderUpdate => JsonSerializer.Deserialize<RecurringOrderUpdateMessage>(json, options),
                    MessageType.FavoriteUpdate => JsonSerializer.Deserialize<FavoriteUpdateMessage>(json, options),
                    MessageType.TimeMultiplierSync => JsonSerializer.Deserialize<TimeMultiplierSyncMessage>(json, options),
                    MessageType.ExecuteRecurringOrder => JsonSerializer.Deserialize<ExecuteRecurringOrderMessage>(json, options),
                    MessageType.RecurringOrderResult => JsonSerializer.Deserialize<RecurringOrderResultMessage>(json, options),
                    MessageType.RequestFullState => JsonSerializer.Deserialize<NetworkMessage>(json, options),
                    MessageType.Ack => JsonSerializer.Deserialize<AckMessage>(json, options),
                    _ => JsonSerializer.Deserialize<NetworkMessage>(json, options)
                };

                if (message != null)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[ModSync] Successfully deserialized {messageType}");
                }

                return message;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ModSync] Deserialization error: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        #endregion
    }
}