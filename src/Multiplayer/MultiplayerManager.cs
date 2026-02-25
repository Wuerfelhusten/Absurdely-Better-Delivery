// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Multiplayer
{
    /// <summary>
    /// Central manager for multiplayer functionality.
    /// Handles host/client detection and coordinates sync services.
    /// </summary>
    public static class MultiplayerManager
    {
        #region Properties

        /// <summary>
        /// Whether multiplayer mode is currently active.
        /// </summary>
        public static bool IsMultiplayer { get; private set; }

        /// <summary>
        /// Whether the local player is the host.
        /// </summary>
        public static bool IsHost { get; private set; }

        /// <summary>
        /// Whether the local player is a client.
        /// </summary>
        public static bool IsClient => IsMultiplayer && !IsHost;

        /// <summary>
        /// Number of connected players (including host).
        /// </summary>
        public static int PlayerCount { get; private set; } = 1;

        #endregion

        #region Private Fields

#pragma warning disable CS0414 // Field is assigned but never read (false positive - used for state tracking)
        private static bool _initialized = false;
#pragma warning restore CS0414
        private static bool _hasNetworkManager = false;
        private static bool _componentRegistered = false;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the multiplayer manager.
        /// Should be called when entering a game session.
        /// </summary>
        public static void Initialize()
        {
            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Main scene loaded - scheduling multiplayer detection with delays...");
            
            // Start detection after scene is fully loaded
            MelonLoader.MelonCoroutines.Start(DelayedDetectionWithRetries());
        }
        
        /// <summary>
        /// Performs multiplayer detection with multiple retry attempts
        /// First waits for scene to load, then tries multiple times with increasing delays
        /// </summary>
        private static System.Collections.IEnumerator DelayedDetectionWithRetries()
        {
            // Wait for scene to fully load (initial delay)
            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Waiting 2 seconds for scene initialization...");
            
            // Use time-based waiting to avoid WaitForSeconds Il2Cpp issues
            float startTime = UnityEngine.Time.realtimeSinceStartup;
            while (UnityEngine.Time.realtimeSinceStartup - startTime < 2f)
            {
                yield return null;
            }
            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Initial wait complete, starting detection...");
            
            // Try detection multiple times with delays
            int[] retryDelays = { 0, 3, 6 }; // Try at 2s, 5s (2+3), 11s (2+3+6)
            
            for (int i = 0; i < retryDelays.Length; i++)
            {
                if (retryDelays[i] > 0)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] Retry {i}: Waiting {retryDelays[i]} more seconds...");
                    float waitStart = UnityEngine.Time.realtimeSinceStartup;
                    while (UnityEngine.Time.realtimeSinceStartup - waitStart < retryDelays[i])
                    {
                        yield return null;
                    }
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] === Detection attempt {i + 1}/{retryDelays.Length} ===");
                DetectMultiplayerState();
                
                if (IsMultiplayer)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] ✓ Multiplayer detected on attempt {i + 1} - Mode: {(IsHost ? "HOST" : "CLIENT")}, Players: {PlayerCount}");
                    
                    CreateNetworkComponent();
                    
                    if (IsHost)
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Initializing HOST sync service...");
                        HostSyncService.Initialize();
                    }
                    else
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Initializing CLIENT sync service...");
                        ClientSyncService.Initialize();
                    }
                    
                    yield break; // Success - stop retrying
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] Attempt {i + 1}: Not in multiplayer yet{(_hasNetworkManager ? " (NetworkManager found but not connected)" : " (No NetworkManager)")}");
            }
            
            // All retries exhausted
            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] All detection attempts completed - assuming single-player mode");
        }
        
        
        /// <summary>
        /// Shuts down multiplayer services.
        /// </summary>
        public static void Shutdown()
        {
            if (IsMultiplayer)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] Shutting down ({(IsHost ? "Host" : "Client")} mode)...");
                
                // Destroy network component
                if (ModSyncBehaviour.Instance != null)
                {
                    UnityEngine.Object.Destroy(ModSyncBehaviour.Instance.gameObject);
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Network component destroyed");
                }
                
                if (IsHost)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Shutting down host sync service...");
                    HostSyncService.Shutdown();
                }
                else
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Shutting down client sync service...");
                    ClientSyncService.Shutdown();
                }
            }
            
            IsMultiplayer = false;
            IsHost = false;
            PlayerCount = 1;
            _initialized = false;
            
            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Shutdown complete");
        }

        #endregion

        #region Detection

        /// <summary>
        /// Detects whether the game is in multiplayer mode and whether we're host or client.
        /// </summary>
        private static void DetectMultiplayerState()
        {
            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Starting multiplayer detection...");
                
                // Try to find FishNet NetworkManager
                Il2CppFishNet.Managing.NetworkManager? networkManager = null;
                
                try
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Searching for NetworkManager...");
                    networkManager = UnityEngine.Object.FindObjectOfType<Il2CppFishNet.Managing.NetworkManager>();
                    AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] NetworkManager search complete: {(networkManager != null ? "FOUND" : "NOT FOUND")}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Multiplayer] Error finding NetworkManager: {ex.Message}");
                    networkManager = null;
                }
                
                if (networkManager == null)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] No NetworkManager found - single player mode");
                    IsMultiplayer = false;
                    IsHost = true;
                    PlayerCount = 1;
                    _hasNetworkManager = false;
                    return;
                }
                
                _hasNetworkManager = true;

                // Check if we're in an active multiplayer session
                bool isServer = false;
                bool isClient = false;
                
                try
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Checking NetworkManager status...");
                    isServer = networkManager.IsServer;
                    isClient = networkManager.IsClient;
                    AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] Status: IsServer={isServer}, IsClient={isClient}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Multiplayer] Error checking NetworkManager status: {ex.Message}");
                }
                
                if (!isServer && !isClient)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] NetworkManager exists but not connected yet - assuming singleplayer for now");
                    IsMultiplayer = false;
                    IsHost = true;
                    PlayerCount = 1;
                    return;
                }

                // Determine role
                IsMultiplayer = true;
                IsHost = isServer;
                
                // Count players (with safety checks)
                if (isServer)
                {
                    try
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Counting connected clients...");
                        PlayerCount = networkManager.ServerManager?.Clients?.Count ?? 1;
                        AbsurdelyBetterDeliveryMod.DebugLog($"[Multiplayer] Server mode detected, {PlayerCount} clients connected");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Multiplayer] Error counting clients: {ex.Message}");
                        PlayerCount = 1;
                    }
                }
                else
                {
                    PlayerCount = 2; // At minimum: host + this client
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Client mode detected");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Multiplayer] Error detecting multiplayer state: {ex.Message}");
                IsMultiplayer = false;
                IsHost = true;
                PlayerCount = 1;
            }
        }

        #endregion

        #region Network Component

        /// <summary>
        /// Creates and spawns the ModSyncBehaviour NetworkBehaviour component.
        /// </summary>
        private static void CreateNetworkComponent()
        {
            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] CreateNetworkComponent called");
                
                // Check if already exists
                if (ModSyncBehaviour.Instance != null)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Network component already exists, skipping creation");
                    return;
                }

                // Register the type with Il2Cpp ClassInjector first time
                if (!_componentRegistered)
                {
                    try
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Registering ModSyncBehaviour with Il2Cpp ClassInjector...");
                        ClassInjector.RegisterTypeInIl2Cpp<ModSyncBehaviour>();
                        _componentRegistered = true;
                        AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] ModSyncBehaviour registered successfully");
                    }
                    catch (Exception regEx)
                    {
                        // Already registered is fine
                        if (!regEx.Message.Contains("already"))
                        {
                            throw;
                        }
                        AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] ModSyncBehaviour already registered");
                        _componentRegistered = true;
                    }
                }

                AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Creating GameObject...");
                var go = new UnityEngine.GameObject("AbsurdelyBetterDelivery_NetworkSync");
                
                AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Adding ModSyncBehaviour component...");
                var modSync = go.AddComponent<ModSyncBehaviour>();
                
                AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Setting DontDestroyOnLoad...");
                UnityEngine.Object.DontDestroyOnLoad(go);
                
                AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Network component created successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Multiplayer] Failed to create network component: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Forces a full state sync from host to all clients.
        /// Only callable by host.
        /// </summary>
        public static void ForceFullSync()
        {
            if (!IsHost)
            {
                MelonLogger.Warning("[Multiplayer] ForceFullSync can only be called by host!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Forcing full state sync to all clients...");
            HostSyncService.BroadcastFullState();
        }

        /// <summary>
        /// Requests the current state from the host.
        /// Only callable by clients.
        /// </summary>
        public static void RequestStateFromHost()
        {
            if (!IsClient)
            {
                MelonLogger.Warning("[Multiplayer] RequestStateFromHost can only be called by clients!");
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog("[Multiplayer] Requesting state from host...");
            ClientSyncService.RequestFullState();
        }

        #endregion
    }
}