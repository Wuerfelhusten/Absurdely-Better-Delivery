// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace AbsurdelyBetterDelivery.Services
{
    /// <summary>
    /// Service responsible for executing recurring delivery orders at scheduled times.
    /// Monitors game time and triggers orders based on RecurringSettings.
    /// </summary>
    public static class RecurringOrderService
    {
        #region Private Fields

        private static TimeManager? _timeManager;
        private static int _lastCheckedMinute = -1;
        private static int _lastCheckedDay = -1;
        private static float _checkInterval = 1f; // Check every second
        private static float _lastCheckTime = 0f;
        private static bool _initialized = false;

        // Cooldown per record to prevent duplicate orders
        private static Dictionary<string, DateTime> _orderCooldowns = new Dictionary<string, DateTime>();
        private static Dictionary<string, DateTime> _failureCooldowns = new Dictionary<string, DateTime>();
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FailureCooldownDuration = TimeSpan.FromSeconds(10);

        // Current save identifier for persistence
        private static string _currentSaveIdentifier = "Default";

        #endregion

        #region Properties

        /// <summary>
        /// Path to the recurring orders JSON file for the current save.
        /// </summary>
        private static string RecurringOrdersPath => 
            Path.Combine(MelonEnvironment.UserDataDirectory, $"RecurringOrders_{_currentSaveIdentifier}.json");

        #endregion

        #region Public API

        /// <summary>
        /// Initializes the recurring order service.
        /// Called when entering the main game scene.
        /// </summary>
        /// <param name="saveIdentifier">The save identifier for this session.</param>
        public static void Initialize(string saveIdentifier = "Default")
        {
            _currentSaveIdentifier = saveIdentifier;
            _timeManager = null;
            _lastCheckedMinute = -1;
            _lastCheckedDay = -1;
            _initialized = true;
            _orderCooldowns.Clear();
            _failureCooldowns.Clear();
            
            LoadRecurringOrders();
            
            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] Service initialized for save: {saveIdentifier}");
        }

        /// <summary>
        /// Updates the recurring order checks.
        /// Should be called from OnUpdate in the main mod class.
        /// </summary>
        public static void Update()
        {
            // Only host should process recurring orders
            if (MultiplayerManager.IsClient)
            {
                // Client skips recurring order processing (host handles this)
                return;
            }
            
            if (!_initialized)
            {
                // Try to auto-initialize if we're in-game but not initialized
                if (_timeManager == null)
                {
                    _timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
                    if (_timeManager != null)
                    {
                        MelonLogger.Warning("[RecurringOrders] Auto-initializing service (was not initialized properly)");
                        _initialized = true;
                        _lastCheckedMinute = -1;
                        _lastCheckedDay = -1;
                    }
                }
                
                if (!_initialized) return;
            }

            // Throttle checks
            if (Time.time - _lastCheckTime < _checkInterval) return;
            _lastCheckTime = Time.time;

            // Ensure TimeManager is available
            if (_timeManager == null)
            {
                _timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
                if (_timeManager == null) return;
            }

            // Get current game time
            var gameTime = GetCurrentGameTime();
            if (gameTime == null) return;

            // Check if minute has changed
            bool minuteChanged = gameTime.Value.minute != _lastCheckedMinute;
            bool dayChanged = gameTime.Value.day != _lastCheckedDay;

            _lastCheckedMinute = gameTime.Value.minute;
            _lastCheckedDay = gameTime.Value.day;

            // Process recurring orders
            if (minuteChanged || dayChanged)
            {
                ProcessRecurringOrders(gameTime.Value);
            }

            // Always check "As Soon As Possible" orders
            ProcessAsSoonAsPossibleOrders();
        }

        /// <summary>
        /// Resets the service state (e.g., when loading a new save).
        /// </summary>
        public static void Reset()
        {
            _timeManager = null;
            _lastCheckedMinute = -1;
            _lastCheckedDay = -1;
            _orderCooldowns.Clear();
            _failureCooldowns.Clear();
            AbsurdelyBetterDeliveryMod.DebugLog("[RecurringOrders] Service reset.");
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Saves the current recurring order settings to disk.
        /// </summary>
        public static void SaveRecurringOrders()
        {
            try
            {
                var data = new RecurringOrdersData();
                
                // Collect all recurring order settings from history
                foreach (var record in DeliveryHistoryManager.History)
                {
                    if (record.RecurringSettings != null && record.RecurringSettings.Type != RecurringType.None)
                    {
                        data.RecurringOrders.Add(new RecurringOrderData
                        {
                            RecordID = record.ID,
                            RecurringType = record.RecurringSettings.Type,
                            Hour = record.RecurringSettings.Hour,
                            Minute = record.RecurringSettings.Minute,
                            DayOfWeek = record.RecurringSettings.DayOfWeek
                        });
                    }
                }
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(RecurringOrdersPath, json);
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] Saved {data.RecurringOrders.Count} recurring orders to: {RecurringOrdersPath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RecurringOrders] Failed to save: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads recurring order settings from disk.
        /// </summary>
        private static void LoadRecurringOrders()
        {
            if (!File.Exists(RecurringOrdersPath))
            {
                AbsurdelyBetterDeliveryMod.DebugLog("[RecurringOrders] No saved recurring orders found.");
                return;
            }

            try
            {
                string json = File.ReadAllText(RecurringOrdersPath);
                var data = JsonSerializer.Deserialize<RecurringOrdersData>(json);
                
                if (data == null || data.RecurringOrders == null)
                {
                    MelonLogger.Warning("[RecurringOrders] Failed to deserialize recurring orders.");
                    return;
                }
                
                // Restore recurring settings to matching history records
                int restoredCount = 0;
                foreach (var orderData in data.RecurringOrders)
                {
                    var record = DeliveryHistoryManager.History.FirstOrDefault(r => r.ID == orderData.RecordID);
                    if (record != null)
                    {
                        record.RecurringSettings = new RecurringSettings
                        {
                            Type = orderData.RecurringType,
                            Hour = orderData.Hour ?? 8,
                            Minute = orderData.Minute ?? 0,
                            DayOfWeek = orderData.DayOfWeek ?? DayOfWeek.Monday
                        };
                        restoredCount++;
                    }
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] Loaded and restored {restoredCount}/{data.RecurringOrders.Count} recurring orders.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RecurringOrders] Failed to load: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Gets the current game time from TimeManager.
        /// </summary>
        private static (int hour, int minute, int day, DayOfWeek dayOfWeek)? GetCurrentGameTime()
        {
            if (_timeManager == null) return null;

            try
            {
                // TimeManager has GetTotalMinSum() which returns total minutes since game start
                int totalMinutes = _timeManager.GetTotalMinSum();
                int elapsedDays = _timeManager.ElapsedDays;
                
                // Calculate current hour and minute
                // Assuming 24 in-game hours per day, 60 minutes per hour
                int minuteOfDay = totalMinutes % (24 * 60);
                int hour = minuteOfDay / 60;
                int minute = minuteOfDay % 60;

                // Get day of week (0 = Sunday in C#)
                // Game starts on a specific day, we need to calculate current day
                DayOfWeek dayOfWeek = (DayOfWeek)(elapsedDays % 7);

                return (hour, minute, elapsedDays, dayOfWeek);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RecurringOrders] Error getting game time: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Processes all scheduled recurring orders (OnceADay, OnceAWeek).
        /// </summary>
        private static void ProcessRecurringOrders((int hour, int minute, int day, DayOfWeek dayOfWeek) gameTime)
        {
            var recurringRecords = DeliveryHistoryManager.History
                .Where(r => r.IsRecurring && r.RecurringSettings != null)
                .Where(r => r.RecurringSettings!.Type == RecurringType.OnceADay || 
                           r.RecurringSettings!.Type == RecurringType.OnceAWeek)
                .ToList();

            foreach (var record in recurringRecords)
            {
                var settings = record.RecurringSettings!;

                // Check if it's the right time
                if (!IsTimeToOrder(settings, gameTime))
                    continue;

                // Check if already ordered today/this week
                if (HasOrderedRecently(record, settings))
                    continue;

                // Execute the order
                ExecuteRecurringOrder(record);
            }
        }

        /// <summary>
        /// Checks if the current game time matches the scheduled order time.
        /// </summary>
        private static bool IsTimeToOrder(RecurringSettings settings, (int hour, int minute, int day, DayOfWeek dayOfWeek) gameTime)
        {
            // Check hour and minute (within a 1-minute window)
            if (gameTime.hour != settings.Hour || gameTime.minute != settings.Minute)
                return false;

            // For weekly orders, also check day of week
            if (settings.Type == RecurringType.OnceAWeek)
            {
                if (gameTime.dayOfWeek != settings.DayOfWeek)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the order was already placed recently (prevents duplicates).
        /// </summary>
        private static bool HasOrderedRecently(DeliveryRecord record, RecurringSettings settings)
        {
            // Check LastExecuted in settings
            if (settings.LastExecuted.HasValue)
            {
                var timeSince = DateTime.Now - settings.LastExecuted.Value;
                
                if (settings.Type == RecurringType.OnceADay && timeSince.TotalHours < 20)
                    return true;
                    
                if (settings.Type == RecurringType.OnceAWeek && timeSince.TotalDays < 6)
                    return true;
            }

            // Also check cooldown dictionary
            if (_orderCooldowns.TryGetValue(record.ID, out var lastOrder))
            {
                if (DateTime.Now - lastOrder < CooldownDuration)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Processes "As Soon As Possible" orders when loading dock becomes available.
        /// </summary>
        private static void ProcessAsSoonAsPossibleOrders()
        {
            var asapRecords = DeliveryHistoryManager.History
                .Where(r => r.IsRecurring && r.RecurringSettings?.Type == RecurringType.AsSoonAsPossible)
                .ToList();

            if (asapRecords.Count == 0) return;

            var app = GetDeliveryApp();
            if (app == null) return;

            // Check if any loading dock is available for each destination
            foreach (var record in asapRecords)
            {
                // Check failure cooldown
                if (_failureCooldowns.TryGetValue(record.ID, out var lastFailure))
                {
                    if (DateTime.Now - lastFailure < FailureCooldownDuration)
                        continue;
                }

                // For ASAP, check if there's already an active delivery for this store/destination
                if (HasActiveDelivery(app, record))
                {
                    continue; // Skip if already delivering
                }

                // Check if we can place an order
                if (CanPlaceOrder(record))
                {
                    ExecuteRecurringOrder(record);
                }
            }
        }

        /// <summary>
        /// Checks if there's an active delivery for the given record's store and destination.
        /// </summary>
        private static bool HasActiveDelivery(DeliveryApp app, DeliveryRecord record)
        {
            try
            {
                // Check if there's an active delivery using the SAME loading dock
                if (app.statusDisplays != null && app.statusDisplays.Count > 0)
                {
                    int blockingCount = 0;
                    
                    foreach (var display in app.statusDisplays)
                    {
                        try
                        {
                            // Get the destination/loading dock info from the delivery
                            var type = display.GetType();
                            
                            // Try to get loading dock index or destination property
                            int? displayDockIndex = null;
                            string? displayDestination = null;
                            
                            // Check for loadingDockIndex field/property
                            var dockIndexField = type.GetField("loadingDockIndex");
                            if (dockIndexField != null)
                            {
                                var value = dockIndexField.GetValue(display);
                                if (value != null)
                                {
                                    displayDockIndex = Convert.ToInt32(value);
                                }
                            }
                            
                            // Check for destination property
                            var destProp = type.GetProperty("Destination");
                            if (destProp != null)
                            {
                                displayDestination = destProp.GetValue(display)?.ToString();
                            }
                            
                            // Compare with our record
                            bool sameDock = displayDockIndex.HasValue && displayDockIndex.Value == record.LoadingDockIndex;
                            bool sameDest = !string.IsNullOrEmpty(displayDestination) && 
                                          !string.IsNullOrEmpty(record.Destination) &&
                                          displayDestination.Replace(" ", "").Equals(record.Destination.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
                            
                            if (sameDock || sameDest)
                            {
                                blockingCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders]   Error inspecting delivery: {ex.Message}");
                        }
                    }
                    
                    if (blockingCount > 0)
                    {
                        return true;
                    }
                }
                
                // ASAP check passed - dock is free
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RecurringOrders] Error checking active deliveries: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Checks if an order can be placed (shop available, loading dock not occupied).
        /// </summary>
        private static bool CanPlaceOrder(DeliveryRecord record)
        {
            var app = GetDeliveryApp();
            if (app == null)
            {
                return false;
            }

            try
            {
                // Find the shop
                foreach (var shop in app.deliveryShops)
                {
                    var interfaceNameProp = shop.GetType().GetProperty("MatchingShopInterfaceName");
                    string shopName = interfaceNameProp?.GetValue(shop)?.ToString() ?? shop.name;

                    if (!shopName.Trim().Equals(record.StoreName.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    // For ASAP, we just need to be able to order - don't check CanOrder as that requires items in cart
                    // Just return true and let RepurchaseService handle it
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RecurringOrders] Error checking order availability: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Executes a recurring order.
        /// </summary>
        private static void ExecuteRecurringOrder(DeliveryRecord record)
        {
            var app = GetDeliveryApp();
            if (app == null)
            {
                MelonLogger.Warning("[RecurringOrders] DeliveryApp not found, cannot execute order.");
                return;
            }

            try
            {
                // Use the existing repurchase service - it returns true if order was placed
                bool success = RepurchaseService.RepurchaseRecord(record, app);

                if (success)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] ✓ Order placed successfully for {record.StoreName}");

                    // Update last executed time
                    if (record.RecurringSettings != null)
                    {
                        record.RecurringSettings.LastExecuted = DateTime.Now;
                        DeliveryHistoryManager.SaveHistory();
                    }

                    // Set cooldown
                    _orderCooldowns[record.ID] = DateTime.Now;
                    _failureCooldowns.Remove(record.ID); // Clear failure cooldown on success
                }
                else
                {
                    // Set failure cooldown to prevent spamming
                    _failureCooldowns[record.ID] = DateTime.Now;
                    
                    // Only log warning if debug mode is on, otherwise it spams the console
                    if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                    {
                        MelonLogger.Warning($"[RecurringOrders] ✗ Order for {record.StoreName} failed. Retrying in {FailureCooldownDuration.TotalSeconds}s.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RecurringOrders] Error executing order: {ex.Message}");
                _failureCooldowns[record.ID] = DateTime.Now; // Also cooldown on exception
            }
        }

        /// <summary>
        /// Gets the active delivery count from DeliveryApp.
        /// </summary>
        private static int GetActiveDeliveryCount()
        {
            var app = GetDeliveryApp();
            if (app == null || app.statusDisplays == null)
            {
                return 0;
            }
            return app.statusDisplays.Count;
        }

        /// <summary>
        /// Gets the DeliveryApp instance.
        /// </summary>
        private static DeliveryApp? GetDeliveryApp()
        {
            var app = AbsurdelyBetterDeliveryMod.DeliveryAppInstance;
            if (app != null) return app;

            app = UnityEngine.Object.FindObjectOfType<DeliveryApp>();
            if (app != null)
            {
                AbsurdelyBetterDeliveryMod.DeliveryAppInstance = app;
            }

            return app;
        }

        #endregion
    }
}