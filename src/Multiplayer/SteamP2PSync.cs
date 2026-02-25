// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Multiplayer
{
    /// <summary>
    /// Steam P2P networking handler for mod synchronization.
    /// Uses Steam's built-in NAT traversal for cross-network communication.
    /// </summary>
    public static class SteamP2PSync
    {
        #region Constants
        
        /// <summary>
        /// Steam P2P channel for mod sync messages.
        /// Using a high number to avoid conflicts with game's channels.
        /// </summary>
        private const int MOD_SYNC_CHANNEL = 42;
        
        /// <summary>
        /// Maximum packet size for Steam P2P (1200 bytes recommended for reliability).
        /// </summary>
        private const int MAX_PACKET_SIZE = 1200;
        
        /// <summary>
        /// Message prefix to identify our mod's packets.
        /// </summary>
        private const string MESSAGE_PREFIX = "ABD:"; // Absurdely Better Delivery
        
        #endregion
        
        #region Fields
        
        private static bool _initialized = false;
        private static CSteamID _hostSteamId;
        private static List<CSteamID> _connectedClients = new();
        private static bool _isHost = false;
        private static Callback<P2PSessionRequest_t>? _p2pSessionRequestCallback;
        
        #endregion
        
        #region Properties
        
        public static bool IsInitialized => _initialized;
        public static bool IsHost => _isHost;
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initializes Steam P2P sync as host or client.
        /// </summary>
        public static void Initialize(bool asHost)
        {
            if (_initialized)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[SteamP2P] Already initialized");
                return;
            }
            
            try
            {
                _isHost = asHost;
                
                // Register callback for incoming P2P session requests
                RegisterP2PCallback();
                
                // Get our own Steam ID
                var localSteamId = SteamUser.GetSteamID();
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Local Steam ID: {localSteamId.m_SteamID}");
                
                if (asHost)
                {
                    _hostSteamId = localSteamId;
                    _connectedClients.Clear();
                    
                    // Register all current lobby members as clients
                    RegisterLobbyMembersAsClients();
                    
                    AbsurdelyBetterDeliveryMod.DebugLog("[SteamP2P] Initialized as HOST");
                }
                else
                {
                    // Client needs to find the host - we'll get this from lobby data
                    FindHostSteamId();
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Initialized as CLIENT, Host Steam ID: {_hostSteamId.m_SteamID}");
                }
                
                _initialized = true;
                MelonLogger.Msg($"[SteamP2P] Steam P2P sync initialized ({(asHost ? "HOST" : "CLIENT")})");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] Initialization error: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Shuts down Steam P2P sync.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            
            try
            {
                _connectedClients.Clear();
                _initialized = false;
                AbsurdelyBetterDeliveryMod.DebugLog("[SteamP2P] Steam P2P sync shutdown");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] Shutdown error: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Host Discovery
        
        /// <summary>
        /// Finds the host's Steam ID from the game's Lobby singleton.
        /// Uses ScheduleOne.Networking.Lobby which manages Steam lobbies.
        /// </summary>
        private static void FindHostSteamId()
        {
            try
            {
                // Use the game's Lobby singleton to get lobby information
                var gameLobby = Lobby.Instance;
                
                if (gameLobby == null)
                {
                    MelonLogger.Warning("[SteamP2P] Game Lobby singleton is null");
                    _hostSteamId = new CSteamID(0);
                    return;
                }
                
                // Check if we're in a lobby
                if (!gameLobby.IsInLobby)
                {
                    MelonLogger.Warning("[SteamP2P] Not currently in a lobby");
                    _hostSteamId = new CSteamID(0);
                    return;
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] In lobby. LobbyID: {gameLobby.LobbyID}, PlayerCount: {gameLobby.PlayerCount}");
                
                // Get the lobby's Steam ID
                var lobbySteamId = gameLobby.LobbySteamID;
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] LobbySteamID: {lobbySteamId.m_SteamID}");
                
                // The lobby owner is the host
                _hostSteamId = SteamMatchmaking.GetLobbyOwner(lobbySteamId);
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Found host via GetLobbyOwner: {_hostSteamId.m_SteamID}");
                
                // Log all players in the lobby for debugging
                var players = gameLobby.Players;
                if (players != null && players.Length > 0)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Players in lobby: {players.Length}");
                    for (int i = 0; i < players.Length; i++)
                    {
                        var player = players[i];
                        AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P]   Player {i}: {player.m_SteamID}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] Could not find host Steam ID: {ex.Message}\n{ex.StackTrace}");
                _hostSteamId = new CSteamID(0);
            }
        }
        
        /// <summary>
        /// Registers all current lobby members as connected clients (called by host).
        /// </summary>
        private static void RegisterLobbyMembersAsClients()
        {
            try
            {
                var gameLobby = Lobby.Instance;
                if (gameLobby == null || !gameLobby.IsInLobby)
                {
                    // Silent return if no lobby - this runs frequently
                    return;
                }
                
                var localSteamId = SteamUser.GetSteamID();
                var players = gameLobby.Players;
                bool newClientAdded = false;
                
                if (players != null && players.Length > 0)
                {
                    foreach (var player in players)
                    {
                        // Don't add ourselves (the host) as a client
                        if (player.m_SteamID != localSteamId.m_SteamID && player.m_SteamID != 0)
                        {
                            if (!_connectedClients.Exists(c => c.m_SteamID == player.m_SteamID))
                            {
                                _connectedClients.Add(player);
                                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Registered client from lobby: {player.m_SteamID}");
                                newClientAdded = true;
                            }
                        }
                    }
                }
                
                if (newClientAdded)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Total registered clients: {_connectedClients.Count}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SteamP2P] Error registering lobby members: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Updates the client list from the current lobby (should be called periodically).
        /// </summary>
        public static void RefreshClientList()
        {
            if (!_isHost || !_initialized) return;
            RegisterLobbyMembersAsClients();
        }
        
        /// <summary>
        /// Registers Steam P2P session request callback.
        /// This is called when another user tries to establish a P2P connection.
        /// </summary>
        private static void RegisterP2PCallback()
        {
            try
            {
                // Create callback for P2P session requests
                _p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(new System.Action<P2PSessionRequest_t>(OnP2PSessionRequest));
                AbsurdelyBetterDeliveryMod.DebugLog("[SteamP2P] P2P session callback registered");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SteamP2P] Failed to register P2P callback: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a P2P session request is received.
        /// </summary>
        private static void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            try
            {
                var remoteSteamId = request.m_steamIDRemote;
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] P2P session request from: {remoteSteamId.m_SteamID}");
                
                // Accept all P2P session requests from lobby members
                SteamNetworking.AcceptP2PSessionWithUser(remoteSteamId);
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Accepted P2P session from: {remoteSteamId.m_SteamID}");
                
                // If we're host and this is a new client, register them
                if (_isHost && !_connectedClients.Exists(c => c.m_SteamID == remoteSteamId.m_SteamID))
                {
                    _connectedClients.Add(remoteSteamId);
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] New client connected via P2P request: {remoteSteamId.m_SteamID}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] OnP2PSessionRequest error: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Message Sending
        
        /// <summary>
        /// Sends a message to the host (client → server).
        /// </summary>
        public static bool SendToHost(NetworkMessage message)
        {
            if (!_initialized || _isHost)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[SteamP2P] SendToHost failed: not initialized or is host");
                return false;
            }
            
            if (_hostSteamId.m_SteamID == 0)
            {
                MelonLogger.Warning("[SteamP2P] Host Steam ID unknown, cannot send message");
                return false;
            }
            
            return SendP2PMessage(_hostSteamId, message);
        }
        
        /// <summary>
        /// Broadcasts a message to all clients (server → clients).
        /// </summary>
        public static bool BroadcastToClients(NetworkMessage message)
        {
            if (!_initialized || !_isHost)
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[SteamP2P] BroadcastToClients failed: not initialized or not host");
                return false;
            }
            
            bool success = true;
            foreach (var clientId in _connectedClients)
            {
                if (!SendP2PMessage(clientId, message))
                {
                    success = false;
                }
            }
            
            AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Broadcast to {_connectedClients.Count} clients: {(success ? "success" : "partial failure")}");
            return success;
        }
        
        /// <summary>
        /// Sends a P2P message to a specific Steam user.
        /// </summary>
        private static bool SendP2PMessage(CSteamID targetId, NetworkMessage message)
        {
            try
            {
                string json = SerializeMessage(message);
                string prefixedJson = MESSAGE_PREFIX + json;
                byte[] data = Encoding.UTF8.GetBytes(prefixedJson);
                
                if (data.Length > MAX_PACKET_SIZE)
                {
                    MelonLogger.Warning($"[SteamP2P] Message too large ({data.Length} bytes), splitting not implemented");
                    // For now, just send it - Steam will handle fragmentation
                }
                
                // Create Il2Cpp array
                var il2cppData = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    il2cppData[i] = data[i];
                }
                
                bool sent = SteamNetworking.SendP2PPacket(
                    targetId, 
                    il2cppData, 
                    (uint)data.Length, 
                    EP2PSend.k_EP2PSendReliable, 
                    MOD_SYNC_CHANNEL
                );
                
                if (sent)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Sent {message.Type} to {targetId.m_SteamID} ({data.Length} bytes)");
                }
                else
                {
                    MelonLogger.Warning($"[SteamP2P] Failed to send packet to {targetId.m_SteamID}");
                }
                
                return sent;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] SendP2PMessage error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
        
        #endregion
        
        #region Message Receiving
        
        /// <summary>
        /// Polls for incoming P2P messages. Should be called every frame or regularly.
        /// </summary>
        public static void PollMessages()
        {
            if (!_initialized) return;
            
            try
            {
                uint messageSize;
                while (SteamNetworking.IsP2PPacketAvailable(out messageSize, MOD_SYNC_CHANNEL))
                {
                    ReceiveP2PMessage(messageSize);
                }
            }
            catch (Exception ex)
            {
                // Silently ignore polling errors to avoid log spam
                if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Poll error: {ex.Message}");
                }
            }
        }
        

        /// <summary>
        /// Receives and processes a P2P message.
        /// </summary>
        private static void ReceiveP2PMessage(uint messageSize)
        {
            try
            {
                var buffer = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>((int)messageSize);
                CSteamID senderId;
                uint bytesRead;
                
                if (SteamNetworking.ReadP2PPacket(buffer, messageSize, out bytesRead, out senderId, MOD_SYNC_CHANNEL))
                {
                    // Convert Il2Cpp array to managed array
                    byte[] data = new byte[bytesRead];
                    for (int i = 0; i < bytesRead; i++)
                    {
                        data[i] = buffer[i];
                    }
                    
                    string json = Encoding.UTF8.GetString(data);
                    
                    // Check for our message prefix
                    if (!json.StartsWith(MESSAGE_PREFIX))
                    {
                        // Not our message, ignore
                        return;
                    }
                    
                    // Remove prefix
                    json = json.Substring(MESSAGE_PREFIX.Length);
                    
                    AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Received message from {senderId.m_SteamID} ({bytesRead} bytes)");
                    
                    // Track client if we're host
                    if (_isHost && !_connectedClients.Contains(senderId))
                    {
                        _connectedClients.Add(senderId);
                        AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] New client connected: {senderId.m_SteamID}");
                    }
                    
                    // Process the message
                    ProcessReceivedMessage(json, senderId);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] ReceiveP2PMessage error: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Processes a received network message.
        /// </summary>
        private static void ProcessReceivedMessage(string json, CSteamID senderId)
        {
            try
            {
                var message = DeserializeMessage(json);
                if (message == null)
                {
                    MelonLogger.Warning("[SteamP2P] Failed to deserialize message");
                    return;
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Processing {message.Type} from {senderId.m_SteamID}");
                
                if (_isHost)
                {
                    // Server received message from client
                    HostSyncService.HandleClientMessage(message, senderId.m_SteamID.ToString());
                }
                else
                {
                    // Client received message from server
                    ClientSyncService.HandleServerMessage(message);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] ProcessReceivedMessage error: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        #endregion
        
        #region Serialization
        
        private static string SerializeMessage(NetworkMessage message)
        {
            var options = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = false
            };
            return JsonSerializer.Serialize(message, message.GetType(), options);
        }
        
        private static NetworkMessage? DeserializeMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("Type", out var typeElement))
                {
                    return null;
                }

                var messageType = (MessageType)typeElement.GetInt32();
                
                var options = new JsonSerializerOptions
                {
                    IncludeFields = true
                };

                return messageType switch
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
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SteamP2P] Deserialization error: {ex.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region Client Management
        
        /// <summary>
        /// Registers a client (called when they send their first message).
        /// </summary>
        public static void RegisterClient(CSteamID clientId)
        {
            if (!_connectedClients.Contains(clientId))
            {
                _connectedClients.Add(clientId);
                AbsurdelyBetterDeliveryMod.DebugLog($"[SteamP2P] Registered client: {clientId.m_SteamID}");
            }
        }
        
        /// <summary>
        /// Gets the count of connected clients.
        /// </summary>
        public static int GetClientCount() => _connectedClients.Count;
        
        #endregion
    }
}