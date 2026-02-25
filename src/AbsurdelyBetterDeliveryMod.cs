// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Services;
using AbsurdelyBetterDelivery.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;
using UnityEngine;

namespace AbsurdelyBetterDelivery
{
    /// <summary>
    /// Main mod class for Absurdely Better Delivery.
    /// Provides enhanced delivery management with history, favorites, and recurring orders.
    /// </summary>
    public class AbsurdelyBetterDeliveryMod : MelonMod
    {
        #region Configuration Categories

        /// <summary>Main settings category.</summary>
        public static MelonPreferences_Category MainCategory = null!;

        #endregion

        #region Configuration Entries

        /// <summary>Maximum number of history items to keep.</summary>
        public static MelonPreferences_Entry<int> MaxHistoryItems = null!;

        /// <summary>Trigger to clear all history (set to true and save).</summary>
        public static MelonPreferences_Entry<bool> ClearHistoryTrigger = null!;

        /// <summary>Delivery time multiplier (0.01 = very fast, 2.0 = very slow).</summary>
        public static MelonPreferences_Entry<float> DeliveryTimeMultiplier = null!;

        /// <summary>Whether to enable debug logging.</summary>
        public static MelonPreferences_Entry<bool> EnableDebugMode = null!;

        /// <summary>Whether delivery arrival phone messages are enabled.</summary>
        public static MelonPreferences_Entry<bool> EnableDeliveryArrivalMessages = null!;

        /// <summary>Whether queued-delivery shop messages are enabled.</summary>
        public static MelonPreferences_Entry<bool> EnableDeliveryQueueMessages = null!;

        #endregion

        #region Static Properties

        /// <summary>Singleton instance of the mod.</summary>
        public static AbsurdelyBetterDeliveryMod Instance { get; private set; } = null!;

        /// <summary>Cached reference to the DeliveryApp.</summary>
        public static DeliveryApp? DeliveryAppInstance { get; set; }

        /// <summary>Tracks whether we're currently in a loaded save (Main scene).</summary>
        private static bool _isInSaveGame = false;

        /// <summary>The current save identifier.</summary>
        private static string _currentSaveIdentifier = "Default";

        /// <summary>Tracks whether the tutorial scene was just loaded (indicates fresh save).</summary>
        private static bool _tutorialSceneLoaded = false;

        /// <summary>
        /// Logs a debug message if debug mode is enabled.
        /// </summary>
        public static void DebugLog(string message)
        {
            if (EnableDebugMode?.Value == true && Instance != null)
            {
                Instance.LoggerInstance.Msg(message);
            }
        }

        #endregion

        #region Icon Sprites

        /// <summary>Unfilled favorite icon.</summary>
        public static Sprite? FavoriteIconFalse { get; private set; }

        /// <summary>Filled favorite icon.</summary>
        public static Sprite? FavoriteIconTrue { get; private set; }

        /// <summary>Repeat once icon.</summary>
        public static Sprite? RepeatOnceIcon { get; private set; }

        /// <summary>Recurring off icon.</summary>
        public static Sprite? RepeatOffIcon { get; private set; }

        /// <summary>Recurring on icon.</summary>
        public static Sprite? RepeatOnIcon { get; private set; }

        /// <summary>Modding Forge contact avatar icon.</summary>
        public static Sprite? ModdingForgeIcon { get; private set; }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc/>
        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("Initializing Absurdely Better Delivery...");

            LoadIcons();
            InitializeConfiguration();
            SubscribeToModManager();

            DeliveryHistoryManager.Initialize();
        }

        /// <inheritdoc/>
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            DebugLog($"Scene loaded: {sceneName} ({buildIndex})");

            if (sceneName == "Tutorial")
            {
                // Tutorial scene indicates a fresh save is being created
                _tutorialSceneLoaded = true;
                DebugLog("[SaveManager] Tutorial scene detected - marking as fresh save.");
            }
            else if (sceneName == "Main")
            {
                _isInSaveGame = true;
                InitializeForSave();
                UpdateClearDataButtonText();
            }
            else if (sceneName == "Menu")
            {
                // Graceful transition out of a save: commit session data and clear crash recovery backup.
                DeliveryHistoryManager.CommitSession();

                _isInSaveGame = false;
                _currentSaveIdentifier = "Default";
                _tutorialSceneLoaded = false; // Reset flag when returning to menu
                UpdateClearDataButtonText();
                
                // Shutdown multiplayer when leaving the game
                MultiplayerManager.Shutdown();
            }
        }

        /// <inheritdoc/>
        public override void OnUpdate()
        {
            // Update delivery time displays
            DeliveryHistoryUI.UpdateTimeDisplays();

            // Process queued deliveries that are waiting for occupied destination/store slots.
            DeliveryWaitingQueueService.Update();

            // Update recurring order checks
            RecurringOrderService.Update();

            // Send one-time welcome message per save when messaging systems are ready.
            WelcomeMessageService.Update();
        }

        /// <inheritdoc/>
        public override void OnApplicationQuit()
        {
            // Graceful shutdown path; keeps crash recovery only for unexpected exits.
            DeliveryHistoryManager.CommitSession();
            MultiplayerManager.Shutdown();
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Initializes all configuration entries.
        /// </summary>
        private void InitializeConfiguration()
        {
            // Main settings
            MainCategory = MelonPreferences.CreateCategory("AbsurdelyBetterDelivery_History", "Main Settings");
            MainCategory.SetFilePath("UserData/AbsurdelyBetterDelivery.cfg");

            MaxHistoryItems = MainCategory.CreateEntry(
                identifier: "MaxHistoryItems",
                default_value: 10,
                display_name: "Max History Items",
                description: "Maximum number of past deliveries to keep."
            );

            ClearHistoryTrigger = MainCategory.CreateEntry(
                identifier: "ClearData",
                default_value: false,
                display_name: "Remove Data",
                description: "Toggle ON and save to remove data. Behavior depends on context."
            );

            // Update display name and description dynamically
            ClearHistoryTrigger.OnEntryValueChanged.Subscribe((bool oldValue, bool newValue) =>
            {
                UpdateClearDataButtonText();
            });

            DeliveryTimeMultiplier = MainCategory.CreateEntry(
                identifier: "DeliveryTimeMultiplier",
                default_value: 1.0f,
                display_name: "Delivery Time Multiplier",
                description: "Adjust delivery speed. 0.5 = 2x faster, 2.0 = 2x slower. Range: 0.01 to 2.0"
            );

            EnableDebugMode = MainCategory.CreateEntry(
                identifier: "EnableDebugMode",
                default_value: false,
                display_name: "Enable Debug Mode",
                description: "Enable detailed debug logging to MelonLoader console."
            );

            EnableDeliveryArrivalMessages = MainCategory.CreateEntry(
                identifier: "EnableDeliveryArrivalMessages",
                default_value: true,
                display_name: "Enable Delivery Arrival Messages",
                description: "Shows \"your order has been delivered\" messages. Does not affect the one-time Modding Forge welcome message."
            );

            EnableDeliveryQueueMessages = MainCategory.CreateEntry(
                identifier: "EnableDeliveryQueueMessages",
                default_value: true,
                display_name: "Enable Delivery Queue Messages",
                description: "Shows \"your delivery is queued\" messages from shops when orders must wait."
            );

            // Validate multiplier range
            DeliveryTimeMultiplier.OnEntryValueChanged.Subscribe((float oldValue, float newValue) =>
            {
                float finalValue = newValue;
                bool wasClamped = false;
                
                if (newValue < 0.01f)
                {
                    finalValue = 0.01f;
                    DeliveryTimeMultiplier.Value = finalValue;
                    LoggerInstance.Warning("Delivery Time Multiplier must be at least 0.01. Value clamped.");
                    wasClamped = true;
                }
                else if (newValue > 2.0f)
                {
                    finalValue = 2.0f;
                    DeliveryTimeMultiplier.Value = finalValue;
                    LoggerInstance.Warning("Delivery Time Multiplier must be at most 2.0. Value clamped.");
                    wasClamped = true;
                }
                
                // In multiplayer, host broadcasts time multiplier to clients
                // Skip broadcast if value was clamped (will be broadcasted by the clamping's own event)
                if (Multiplayer.MultiplayerManager.IsHost && !wasClamped)
                {
                    DebugLog($"[Config] Host time multiplier changed: {oldValue:F3}x → {finalValue:F3}x, broadcasting to clients...");
                    Multiplayer.HostSyncService.BroadcastTimeMultiplier(finalValue);
                }
                else if (Multiplayer.MultiplayerManager.IsClient && !wasClamped)
                {
                    DebugLog($"[Config] Client time multiplier changed locally: {oldValue:F3}x → {finalValue:F3}x (will be overridden by host)");
                }
            });

            DebugLog("Configuration loaded.");
        }

        /// <summary>
        /// Subscribes to Mod Manager events for settings updates.
        /// </summary>
        private void SubscribeToModManager()
        {
            try
            {
                // Dynamic loading to avoid hard dependency on Mod Manager
                var modManagerType = Type.GetType("ModManagerPhoneApp.ModSettingsEvents, ModManager&PhoneApp");
                if (modManagerType == null) return;

                var phoneEvent = modManagerType.GetEvent("OnPhonePreferencesSaved");
                var menuEvent = modManagerType.GetEvent("OnMenuPreferencesSaved");

                Action handler = HandleSettingsUpdate;

                phoneEvent?.AddEventHandler(null, handler);
                menuEvent?.AddEventHandler(null, handler);

                DebugLog("Subscribed to Mod Manager events.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("Mod Manager not available: " + ex.Message);
            }
        }

        /// <summary>
        /// Handles settings updates from Mod Manager.
        /// </summary>
        private void HandleSettingsUpdate()
        {
            DebugLog($"HandleSettingsUpdate called. ClearHistoryTrigger.Value = {ClearHistoryTrigger.Value}");
            
            if (!ClearHistoryTrigger.Value) return;

            if (_isInSaveGame)
            {
                // In save game: Clear only current save's data
                DebugLog($"Clear Data triggered for save: {_currentSaveIdentifier}");
                ClearCurrentSaveData();
            }
            else
            {
                // In menu: Clear all data from all saves
                DebugLog("Remove All Data triggered! Clearing data from all saves...");
                ClearAllData();
            }

            ClearHistoryTrigger.Value = false;
            MelonPreferences.Save();

            // Request Mod Manager to refresh UI to show the reset toggle
            TriggerModManagerUIRefresh();

            if (DeliveryAppInstance != null)
            {
                DeliveryHistoryUI.RefreshHistoryUI(DeliveryAppInstance);
            }

            DebugLog("Data removal complete.");
        }

        /// <summary>
        /// Triggers Mod Manager UI refresh to update displayed values.
        /// </summary>
        private void TriggerModManagerUIRefresh()
        {
            try
            {
                var modManagerType = Type.GetType("ModManagerPhoneApp.ModSettingsEvents, ModManager&PhoneApp");
                if (modManagerType == null) return;

                var refreshMethod = modManagerType.GetMethod("TriggerUIRefresh", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                if (refreshMethod != null)
                {
                    refreshMethod.Invoke(null, null);
                    DebugLog("Mod Manager UI refresh triggered.");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to trigger UI refresh: {ex.Message}");
            }
        }

        #endregion

        #region Save Game Handling

        /// <summary>
        /// Initializes the history for the current save game.
        /// </summary>
        private void InitializeForSave()
        {
            var saveManager = UnityEngine.Object.FindObjectOfType<SaveManager>();
            if (saveManager == null) return;

            string saveName = saveManager.SaveName;
            string containerPath = saveManager.IndividualSavesContainerPath;
            
            // Try to find the actual save slot by checking which SaveGame_ folder has recent activity
            string actualSaveSlot = TryFindActiveSaveSlot(containerPath);
            
            DebugLog($"[SaveManager] SaveName: {saveName}");
            DebugLog($"[SaveManager] ContainerPath: {containerPath}");
            DebugLog($"[SaveManager] Detected SaveSlot: {actualSaveSlot}");

            // Use the save slot name if found, otherwise fall back to SaveName
            string identifier = !string.IsNullOrEmpty(actualSaveSlot) ? actualSaveSlot : saveName;
            _currentSaveIdentifier = identifier;
            
            // Check if this is a fresh save (tutorial was just played)
            bool isFreshSave = _tutorialSceneLoaded;
            DebugLog($"[SaveManager] Is Fresh Save: {isFreshSave}");
            
            if (isFreshSave)
            {
                DebugLog($"[SaveManager] Fresh save detected - clearing old data for slot: {identifier}");
                ClearCurrentSaveData();
                _tutorialSceneLoaded = false; // Reset flag after cleaning
            }
            
            DeliveryHistoryManager.Initialize(identifier);
            RecurringOrderService.Initialize(identifier);
            WelcomeMessageService.Initialize(identifier);
            
            // Initialize multiplayer system
            MultiplayerManager.Initialize();

            if (DeliveryAppInstance != null)
            {
                DeliveryHistoryUI.RefreshHistoryUI(DeliveryAppInstance);
            }
        }

        /// <summary>
        /// Attempts to find the active save slot by checking for recent file activity.
        /// </summary>
        private string TryFindActiveSaveSlot(string containerPath)
        {
            try
            {
                var directory = new System.IO.DirectoryInfo(containerPath);
                if (!directory.Exists) return "";

                // Find all SaveGame_ folders
                var saveGameFolders = directory.GetDirectories("SaveGame_*");
                
                if (saveGameFolders.Length == 0) return "";

                // Find the most recently modified SaveGame_ folder
                var mostRecent = saveGameFolders
                    .OrderByDescending(d => d.LastWriteTime)
                    .FirstOrDefault();

                if (mostRecent != null)
                {
                    DebugLog($"[SaveManager] Found recent save folder: {mostRecent.Name} (LastWriteTime: {mostRecent.LastWriteTime})");
                    return mostRecent.Name;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[SaveManager] Error finding active save slot: {ex.Message}");
            }

            return "";
        }

        #endregion

        #region Data Management

        /// <summary>
        /// Updates the Clear Data button text based on current context.
        /// </summary>
        private void UpdateClearDataButtonText()
        {
            if (ClearHistoryTrigger == null) return;

            if (_isInSaveGame)
            {
                ClearHistoryTrigger.DisplayName = "Remove Data from this Save";
                ClearHistoryTrigger.Description = $"Toggle ON and save to remove all delivery history and recurring orders from save: {_currentSaveIdentifier}";
            }
            else
            {
                ClearHistoryTrigger.DisplayName = "Remove All Data";
                ClearHistoryTrigger.Description = "Toggle ON and save to remove ALL delivery history and recurring orders from ALL saves.";
            }

            // Trigger UI refresh in Mod Manager
            TriggerModManagerUIRefresh();
        }

        /// <summary>
        /// Clears data for the current save only.
        /// </summary>
        private void ClearCurrentSaveData()
        {
            DebugLog($"[DataManagement] Clearing data for save: {_currentSaveIdentifier}");

            // Clear history
            DeliveryHistoryManager.ClearHistory();

            // Delete recurring orders file
            string recurringPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, $"RecurringOrders_{_currentSaveIdentifier}.json");
            if (File.Exists(recurringPath))
            {
                File.Delete(recurringPath);
                DebugLog($"[DataManagement] Deleted: {recurringPath}");
            }

            string welcomeFlagPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, $"WelcomeMessageSent_{_currentSaveIdentifier}.flag");
            if (File.Exists(welcomeFlagPath))
            {
                File.Delete(welcomeFlagPath);
                DebugLog($"[DataManagement] Deleted: {welcomeFlagPath}");
            }

            DebugLog($"[DataManagement] Data cleared for save: {_currentSaveIdentifier}");
            
            // Broadcast clear data to clients if we're the host
            if (Multiplayer.MultiplayerManager.IsHost)
            {
                Multiplayer.HostSyncService.BroadcastClearData();
            }
        }

        /// <summary>
        /// Clears all data from all saves.
        /// </summary>
        private void ClearAllData()
        {
            DebugLog("[DataManagement] Clearing ALL data from ALL saves...");

            try
            {
                string userDataPath = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;

                // Delete all DeliveryHistory_*.json files
                var historyFiles = Directory.GetFiles(userDataPath, "DeliveryHistory_*.json");
                foreach (var file in historyFiles)
                {
                    File.Delete(file);
                    LoggerInstance.Msg($"[DataManagement] Deleted: {Path.GetFileName(file)}");
                }

                // Delete all RecurringOrders_*.json files
                var recurringFiles = Directory.GetFiles(userDataPath, "RecurringOrders_*.json");
                foreach (var file in recurringFiles)
                {
                    File.Delete(file);
                    LoggerInstance.Msg($"[DataManagement] Deleted: {Path.GetFileName(file)}");
                }

                // Delete all WelcomeMessageSent_*.flag files
                var welcomeFlags = Directory.GetFiles(userDataPath, "WelcomeMessageSent_*.flag");
                foreach (var file in welcomeFlags)
                {
                    File.Delete(file);
                    LoggerInstance.Msg($"[DataManagement] Deleted: {Path.GetFileName(file)}");
                }

                // Clear current in-memory data
                DeliveryHistoryManager.History.Clear();

                LoggerInstance.Msg($"[DataManagement] Removed {historyFiles.Length} history files and {recurringFiles.Length} recurring order files.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[DataManagement] Error clearing all data: {ex.Message}");
            }
        }

        #endregion

        #region Icon Loading

        /// <summary>
        /// Loads all icon sprites from embedded resources.
        /// </summary>
        private void LoadIcons()
        {
            try
            {
                FavoriteIconFalse = LoadEmbeddedSprite("AbsurdelyBetterDelivery.assets.favorite_false.png");
                FavoriteIconTrue = LoadEmbeddedSprite("AbsurdelyBetterDelivery.assets.favorite_true.png");
                RepeatOnceIcon = LoadEmbeddedSprite("AbsurdelyBetterDelivery.assets.repeat_once.png");
                RepeatOffIcon = LoadEmbeddedSprite("AbsurdelyBetterDelivery.assets.repeat_off.png");
                RepeatOnIcon = LoadEmbeddedSprite("AbsurdelyBetterDelivery.assets.repeat_on.png");
                ModdingForgeIcon = LoadEmbeddedSprite("AbsurdelyBetterDelivery.assets.modding_forge.png");

                LogIcons();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("Failed to load icons: " + ex.Message);
            }
        }

        /// <summary>
        /// Logs the status of loaded icons.
        /// </summary>
        private void LogIcons()
        {
            LogIconStatus("favorite_false", FavoriteIconFalse);
            LogIconStatus("favorite_true", FavoriteIconTrue);
            LogIconStatus("repeat_once", RepeatOnceIcon);
            LogIconStatus("repeat_off", RepeatOffIcon);
            LogIconStatus("repeat_on", RepeatOnIcon);
            LogIconStatus("modding_forge", ModdingForgeIcon);
        }

        /// <summary>
        /// Logs the status of a single icon.
        /// </summary>
        private void LogIconStatus(string name, Sprite? sprite)
        {
            if (sprite != null)
                DebugLog($"Loaded embedded {name} icon");
            else
                LoggerInstance.Warning($"Failed to load embedded {name} icon");
        }

        /// <summary>
        /// Loads a sprite from an embedded resource.
        /// </summary>
        /// <param name="resourceName">The full resource name.</param>
        /// <returns>The loaded sprite, or null on failure.</returns>
        private Sprite? LoadEmbeddedSprite(string resourceName)
        {
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    LoggerInstance.Error("Embedded resource not found: " + resourceName);
                    LogAvailableResources();
                    return null;
                }

                byte[] data = new byte[stream.Length];
                _ = stream.Read(data, 0, data.Length);

                var tex = new Texture2D(2, 2)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                Il2CppStructArray<byte> il2cppData = data;

                if (tex.LoadImage(il2cppData))
                {
                    DebugLog($"Loaded texture {resourceName}: {tex.width}x{tex.height}");

                    var sprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    sprite.hideFlags = HideFlags.HideAndDontSave;

                    return sprite;
                }

                LoggerInstance.Error("Failed to load image data from: " + resourceName);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to load embedded sprite {resourceName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Logs all available embedded resources for debugging.
        /// </summary>
        private void LogAvailableResources()
        {
            string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            LoggerInstance.Msg("Available resources: " + string.Join(", ", names));
        }

        #endregion
    }
}
