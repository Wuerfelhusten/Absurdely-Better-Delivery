// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Utils;
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
        private static int _lastCheckedHour = -1;
        private static int _lastCheckedMinute = -1;
        private static int _lastCheckedDay = -1;
        private static float _checkInterval = 1f; // Check every second
        private static float _lastCheckTime = 0f;
        private static bool _initialized = false;

        // Cooldown per record to prevent duplicate orders
        private static Dictionary<string, DateTime> _orderCooldowns = new Dictionary<string, DateTime>();
        private static Dictionary<string, DateTime> _failureCooldowns = new Dictionary<string, DateTime>();
        private static HashSet<string> _failureCooldownLogged = new HashSet<string>();
        private static HashSet<string> _activeDeliveryBlockLogged = new HashSet<string>();
        private static Dictionary<string, int> _asapRoundRobinNextByLocation = new Dictionary<string, int>();
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FailureCooldownDuration = TimeSpan.FromSeconds(10);
        private static int _lastAsapCandidateCount = -1;

        // Current save identifier for persistence
        private static string _currentSaveIdentifier = "Default";

        #endregion

        #region Properties

        /// <summary>
        /// Path to the recurring orders JSON file for the current save.
        /// </summary>
        private static string RecurringOrdersPath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, $"RecurringOrders_{_currentSaveIdentifier}.json");

        /// <summary>
        /// Path to the per-session backup of the recurring orders file.
        /// Created at session start and used to roll back if the game is not saved,
        /// keeping recurring order record IDs in sync with the history rollback.
        /// </summary>
        private static string RecurringOrdersSessionBackupPath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, $"RecurringOrders_{_currentSaveIdentifier}.session.bak");

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
            _lastCheckedHour = -1;
            _lastCheckedMinute = -1;
            _lastCheckedDay = -1;
            _initialized = true;
            _orderCooldowns.Clear();
            _failureCooldowns.Clear();
            _failureCooldownLogged.Clear();
            _activeDeliveryBlockLogged.Clear();
            _asapRoundRobinNextByLocation.Clear();
            _lastAsapCandidateCount = -1;
            
            LoadRecurringOrders();
            CreateSessionBackup();

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
                        _lastCheckedHour = -1;
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

            // Capture the previous tick position before advancing. Used by ProcessRecurringOrders
            // to detect minutes that were skipped at high game speed.
            var prevTime = (_lastCheckedHour, _lastCheckedMinute);
            bool dayChanged = gameTime.Value.day != _lastCheckedDay;
            bool timeAdvanced = gameTime.Value.hour != _lastCheckedHour
                || gameTime.Value.minute != _lastCheckedMinute
                || dayChanged;

            _lastCheckedHour = gameTime.Value.hour;
            _lastCheckedMinute = gameTime.Value.minute;
            _lastCheckedDay = gameTime.Value.day;

            // Process recurring orders if time has advanced.
            // prevTime is forwarded so IsTimeToOrder can use a range-based missed-minute check.
            if (timeAdvanced)
            {
                ProcessRecurringOrders(gameTime.Value, prevTime);
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
            _lastCheckedHour = -1;
            _lastCheckedMinute = -1;
            _lastCheckedDay = -1;
            _orderCooldowns.Clear();
            _failureCooldowns.Clear();
            _failureCooldownLogged.Clear();
            _activeDeliveryBlockLogged.Clear();
            _asapRoundRobinNextByLocation.Clear();
            _lastAsapCandidateCount = -1;
            AbsurdelyBetterDeliveryMod.DebugLog("[RecurringOrders] Service reset.");
        }

        /// <summary>
        /// Called when the game fires its own save event.
        /// Persists any in-memory changes (e.g. <see cref="RecurringSettings.LastExecutedGameDay"/>)
        /// and refreshes the session backup so a subsequent history rollback would restore to
        /// this post-save state rather than the original session-start state.
        /// </summary>
        public static void OnGameSaved()
        {
            SaveRecurringOrders();
            CreateSessionBackup();
            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] Game saved — session checkpoint updated for save '{_currentSaveIdentifier}'.");
        }

        /// <summary>
        /// Rolls back the persistent recurring orders file to the session-start snapshot.
        /// Called from <see cref="Managers.DeliveryHistoryManager.CommitSession"/> when the
        /// game was not saved, to keep record IDs in RecurringOrders_*.json in sync with
        /// the restored history baseline.
        /// </summary>
        public static void RollbackSessionOrders()
        {
            try
            {
                if (File.Exists(RecurringOrdersSessionBackupPath))
                {
                    File.Copy(RecurringOrdersSessionBackupPath, RecurringOrdersPath, overwrite: true);
                    File.Delete(RecurringOrdersSessionBackupPath);
                    MelonLogger.Msg($"[RecurringOrders] Rolled back to session-start snapshot for save '{_currentSaveIdentifier}'.");
                }
                else if (File.Exists(RecurringOrdersPath))
                {
                    // No backup means there were no recurring orders at session start.
                    // Delete the file created during the unsaved session.
                    File.Delete(RecurringOrdersPath);
                    MelonLogger.Msg($"[RecurringOrders] No session backup — deleted unsaved recurring orders for save '{_currentSaveIdentifier}'.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RecurringOrders] Failed to rollback session orders: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the session backup after a graceful, saved exit.
        /// </summary>
        public static void CommitSessionOrders()
        {
            try
            {
                if (File.Exists(RecurringOrdersSessionBackupPath))
                    File.Delete(RecurringOrdersSessionBackupPath);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RecurringOrders] Failed to commit session orders: {ex.Message}");
            }
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
                            DayOfWeek = record.RecurringSettings.DayOfWeek,
                            LastExecuted = record.RecurringSettings.LastExecuted,
                            LastExecutedGameDay = record.RecurringSettings.LastExecutedGameDay
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
                            DayOfWeek = orderData.DayOfWeek ?? DayOfWeek.Monday,
                            LastExecuted = orderData.LastExecuted,
                            LastExecutedGameDay = orderData.LastExecutedGameDay
                        };
                        restoredCount++;
                    }
                    else
                    {
                        MelonLogger.Warning(
                            $"[RecurringOrders] Could not restore recurring order — record ID '{orderData.RecordID}' not found in history. " +
                            $"The recurring settings for this order are lost until it is reconfigured.");
                    }
                }
                
                AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] Loaded and restored {restoredCount}/{data.RecurringOrders.Count} recurring orders.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RecurringOrders] Failed to load: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies the current recurring orders file to the session backup path.
        /// Called at <see cref="Initialize"/> and whenever the game saves, so that
        /// <see cref="RollbackSessionOrders"/> can restore exactly to that baseline.
        /// If no recurring orders file exists yet, any stale backup is removed.
        /// </summary>
        private static void CreateSessionBackup()
        {
            try
            {
                if (File.Exists(RecurringOrdersPath))
                    File.Copy(RecurringOrdersPath, RecurringOrdersSessionBackupPath, overwrite: true);
                else if (File.Exists(RecurringOrdersSessionBackupPath))
                    File.Delete(RecurringOrdersSessionBackupPath);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RecurringOrders] Failed to create session backup: {ex.Message}");
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

                // Convert the game's EDay index to System.DayOfWeek.
                // EDay (ScheduleOne.GameTime): Monday=0, Tuesday=1, ..., Saturday=5, Sunday=6
                // DayOfWeek (System):          Sunday=0, Monday=1, ..., Saturday=6
                // Mapping: eDayIndex → (eDayIndex + 1) % 7
                // Verified against: Mono decompile ScheduleOne.GameTime.EDay
                DayOfWeek dayOfWeek = (DayOfWeek)((elapsedDays % 7 + 1) % 7);

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
        /// <param name="gameTime">Current game time.</param>
        /// <param name="prevTime">Game time at the previous tick. Used for range-based missed-minute detection.</param>
        private static void ProcessRecurringOrders(
            (int hour, int minute, int day, DayOfWeek dayOfWeek) gameTime,
            (int hour, int minute) prevTime)
        {
            var recurringRecords = DeliveryHistoryManager.History
                .Where(r => r.IsRecurring && r.RecurringSettings != null)
                .Where(r => r.RecurringSettings!.Type == RecurringType.OnceADay || 
                           r.RecurringSettings!.Type == RecurringType.OnceAWeek)
                .ToList();

            foreach (var record in recurringRecords)
            {
                var settings = record.RecurringSettings!;

                // Range-based time check: catches minutes skipped at high game speed.
                if (!IsTimeToOrder(settings, gameTime, prevTime))
                    continue;

                // Check if already ordered today/this week
                if (HasOrderedRecently(record, settings))
                    continue;

                // Execute the order
                ExecuteRecurringOrder(record, allowWaitingQueue: true);
            }
        }

        /// <summary>
        /// Checks if the scheduled order time was reached between the previous and current tick.
        /// Uses a range-based check so that minutes skipped at high game speed are not missed.
        /// Falls back to exact-match on the very first tick of a session (prevTime.hour == -1)
        /// so we don't retroactively fire orders for times that passed before session start.
        /// </summary>
        private static bool IsTimeToOrder(
            RecurringSettings settings,
            (int hour, int minute, int day, DayOfWeek dayOfWeek) gameTime,
            (int hour, int minute) prevTime)
        {
            bool timeMatches;
            if (prevTime.hour == -1)
            {
                // First tick of the session: exact match only.
                timeMatches = gameTime.hour == settings.Hour && gameTime.minute == settings.Minute;
            }
            else
            {
                // Range check: true if the scheduled minute falls in the half-open interval
                // (prevTime, currentTime], handling midnight rollover.
                timeMatches = IsMinuteBetween(
                    settings.Hour, settings.Minute,
                    prevTime,
                    (gameTime.hour, gameTime.minute));
            }

            if (!timeMatches)
                return false;

            // For weekly orders, also check day of week.
            if (settings.Type == RecurringType.OnceAWeek)
            {
                if (gameTime.dayOfWeek != settings.DayOfWeek)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if the scheduled (hour, minute) falls within the half-open interval
        /// (from, to], handling midnight rollover correctly.
        /// Used by <see cref="IsTimeToOrder"/> to detect game-time ticks skipped at high speed.
        /// </summary>
        private static bool IsMinuteBetween(
            int scheduledHour, int scheduledMinute,
            (int hour, int minute) from,
            (int hour, int minute) to)
        {
            int scheduled = scheduledHour * 60 + scheduledMinute;
            int fromTotal = from.hour * 60 + from.minute;
            int toTotal   = to.hour   * 60 + to.minute;

            if (fromTotal < toTotal)
            {
                // Normal forward progression within the same day-segment.
                return scheduled > fromTotal && scheduled <= toTotal;
            }
            else if (fromTotal > toTotal)
            {
                // Day rollover (e.g. 23:59 → 0:01): range wraps around midnight.
                return scheduled > fromTotal || scheduled <= toTotal;
            }
            else
            {
                // from == to: clock hasn't moved (degenerate guard).
                return scheduled == toTotal;
            }
        }

        /// <summary>
        /// Checks if the order was already placed recently (prevents duplicates).
        /// Prefers a game-day based check so the cooldown is unaffected by real-world
        /// time or game-speed multiplier. Falls back to the legacy wall-clock check for
        /// records saved before <see cref="RecurringSettings.LastExecutedGameDay"/> was added.
        /// </summary>
        private static bool HasOrderedRecently(DeliveryRecord record, RecurringSettings settings)
        {
            var gameTime = GetCurrentGameTime();

            if (gameTime.HasValue && settings.LastExecutedGameDay.HasValue)
            {
                // Game-day based cooldown: independent of real time and speed multiplier.
                int daysSince = gameTime.Value.day - settings.LastExecutedGameDay.Value;
                if (settings.Type == RecurringType.OnceADay && daysSince < 1)
                    return true;
                if (settings.Type == RecurringType.OnceAWeek && daysSince < 7)
                    return true;
            }
            else if (settings.LastExecuted.HasValue)
            {
                // Legacy fallback for records persisted before LastExecutedGameDay was introduced.
                var timeSince = DateTime.Now - settings.LastExecuted.Value;
                if (settings.Type == RecurringType.OnceADay && timeSince.TotalHours < 20)
                    return true;
                if (settings.Type == RecurringType.OnceAWeek && timeSince.TotalDays < 6)
                    return true;
            }

            // In-session guard: prevents rapid duplicate triggers within the same session.
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
                .OrderBy(r => r.Timestamp)
                .ThenBy(r => r.ID, StringComparer.Ordinal)
                .ToList();

            if (asapRecords.Count == 0) return;

            if (asapRecords.Count != _lastAsapCandidateCount)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] ASAP scan found {asapRecords.Count} candidate(s).");
                _lastAsapCandidateCount = asapRecords.Count;
            }

            var app = GetDeliveryApp();
            if (app == null) return;

            // Process one record per destination+dock key in deterministic round-robin order.
            var locationGroups = asapRecords
                .GroupBy(GetAsapLocationKey)
                .ToList();

            foreach (var locationGroup in locationGroups)
            {
                string locationKey = locationGroup.Key;
                var orderedGroup = locationGroup
                    .OrderBy(r => r.Timestamp)
                    .ThenBy(r => r.ID, StringComparer.Ordinal)
                    .ToList();

                if (orderedGroup.Count == 0)
                {
                    continue;
                }

                int startIndex = GetRoundRobinStartIndex(locationKey, orderedGroup.Count);
                bool executedForLocation = false;

                for (int offset = 0; offset < orderedGroup.Count; offset++)
                {
                    int candidateIndex = (startIndex + offset) % orderedGroup.Count;
                    var record = orderedGroup[candidateIndex];

                    // Check failure cooldown
                    if (_failureCooldowns.TryGetValue(record.ID, out var lastFailure))
                    {
                        if (DateTime.Now - lastFailure < FailureCooldownDuration)
                        {
                            if (!_failureCooldownLogged.Contains(record.ID))
                            {
                                AbsurdelyBetterDeliveryMod.DebugLog(
                                    $"[RecurringOrders] ASAP skip '{record.StoreName}' (ID={record.ID}) due to failure cooldown ({FailureCooldownDuration.TotalSeconds:F0}s).");
                                _failureCooldownLogged.Add(record.ID);
                            }
                            continue;
                        }

                        if (_failureCooldownLogged.Contains(record.ID))
                        {
                            AbsurdelyBetterDeliveryMod.DebugLog(
                                $"[RecurringOrders] Failure cooldown ended for '{record.StoreName}' (ID={record.ID}), retrying.");
                            _failureCooldownLogged.Remove(record.ID);
                        }
                    }

                    // For ASAP, check if there's already an active delivery for this store/destination
                    if (HasActiveDelivery(app, record))
                    {
                        if (!_activeDeliveryBlockLogged.Contains(record.ID))
                        {
                            AbsurdelyBetterDeliveryMod.DebugLog(
                                $"[RecurringOrders] ASAP skip '{record.StoreName}' (ID={record.ID}) because active delivery is blocking destination={record.Destination}, dock={record.LoadingDockIndex + 1}.");
                            _activeDeliveryBlockLogged.Add(record.ID);
                        }
                        continue;
                    }

                    if (_activeDeliveryBlockLogged.Contains(record.ID))
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog(
                            $"[RecurringOrders] Active delivery block ended for '{record.StoreName}' (ID={record.ID}).");
                        _activeDeliveryBlockLogged.Remove(record.ID);
                    }

                    // Check if we can place an order
                    if (CanPlaceOrder(record))
                    {
                        ExecuteRecurringOrder(record, allowWaitingQueue: false);

                        // Advance pointer after a processed attempt to keep strict alternation.
                        int nextIndex = (candidateIndex + 1) % orderedGroup.Count;
                        _asapRoundRobinNextByLocation[locationKey] = nextIndex;
                        executedForLocation = true;
                        break;
                    }

                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[RecurringOrders] ASAP skip '{record.StoreName}' (ID={record.ID}) because CanPlaceOrder returned false.");
                }

                if (!executedForLocation)
                {
                    _asapRoundRobinNextByLocation[locationKey] = startIndex;
                }
            }
        }

        /// <summary>
        /// Builds a stable grouping key for ASAP records based on destination and loading dock.
        /// </summary>
        /// <param name="record">Recurring record.</param>
        /// <returns>Normalized key combining destination and dock index.</returns>
        private static string GetAsapLocationKey(DeliveryRecord record)
        {
            string destination = NormalizeForMatch(record.Destination ?? string.Empty);
            return $"{destination}|{record.LoadingDockIndex}";
        }

        /// <summary>
        /// Gets the round-robin start index for a location group.
        /// </summary>
        /// <param name="locationKey">Destination and dock grouping key.</param>
        /// <param name="groupCount">Number of records in the group.</param>
        /// <returns>Valid start index in range [0..groupCount-1].</returns>
        private static int GetRoundRobinStartIndex(string locationKey, int groupCount)
        {
            if (groupCount <= 0)
            {
                return 0;
            }

            if (_asapRoundRobinNextByLocation.TryGetValue(locationKey, out int storedIndex))
            {
                if (storedIndex >= 0 && storedIndex < groupCount)
                {
                    return storedIndex;
                }
            }

            return 0;
        }

        /// <summary>
        /// Checks if there's an active delivery for the given record's store and destination.
        /// </summary>
        private static bool HasActiveDelivery(DeliveryApp app, DeliveryRecord record)
        {
            try
            {
                // Check if there's an active delivery using the same destination/loading dock pair.
                if (app.statusDisplays != null && app.statusDisplays.Count > 0)
                {
                    string recordDestination = record.Destination ?? string.Empty;
                    string recordDestinationNormalized = NormalizeForMatch(recordDestination);
                    int recordDockIndex = record.LoadingDockIndex;
                    
                    foreach (var display in app.statusDisplays)
                    {
                        try
                        {
                            if (display == null || display.DeliveryInstance == null)
                            {
                                continue;
                            }

                            var delivery = display.DeliveryInstance;
                            string activeDestination = delivery.DestinationCode ?? string.Empty;
                            string activeDestinationNormalized = NormalizeForMatch(activeDestination);
                            int activeDockIndex = delivery.LoadingDockIndex;

                            bool sameDestination =
                                !string.IsNullOrEmpty(recordDestinationNormalized) &&
                                activeDestinationNormalized.Equals(recordDestinationNormalized, StringComparison.OrdinalIgnoreCase);

                            bool sameDock = activeDockIndex == recordDockIndex;

                            if (sameDestination && sameDock)
                            {
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders]   Error inspecting delivery: {ex.Message}");
                        }
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
                AbsurdelyBetterDeliveryMod.DebugLog(
                    $"[RecurringOrders] CanPlaceOrder: DeliveryApp unavailable for '{record.StoreName}' (ID={record.ID}).");
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
                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[RecurringOrders] CanPlaceOrder: found matching shop '{shopName}' for '{record.StoreName}' (ID={record.ID}).");
                    return true;
                }

                AbsurdelyBetterDeliveryMod.DebugLog(
                    $"[RecurringOrders] CanPlaceOrder: no matching shop found for '{record.StoreName}' (ID={record.ID}).");
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
        private static void ExecuteRecurringOrder(DeliveryRecord record, bool allowWaitingQueue)
        {
            var app = GetDeliveryApp();
            if (app == null)
            {
                MelonLogger.Warning("[RecurringOrders] DeliveryApp not found, cannot execute order.");
                return;
            }

            try
            {
                AbsurdelyBetterDeliveryMod.DebugLog(
                    $"[RecurringOrders] Executing record ID={record.ID}, store={record.StoreName}, destination={record.Destination}, dock={record.LoadingDockIndex + 1}, items={record.Items.Count}");

                // Use the existing repurchase service - it returns true if order was placed
                bool success = RepurchaseService.RepurchaseRecord(record, app, allowWaitingQueue);

                if (success)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringOrders] ✓ Order placed successfully for {record.StoreName}");

                    if (record.RecurringSettings != null)
                    {
                        // Track both wall-clock time (legacy compat) and in-game day.
                        // The game-day value is preferred by HasOrderedRecently() so the cooldown
                        // remains correct regardless of game-speed multiplier.
                        record.RecurringSettings.LastExecuted = DateTime.Now;
                        record.RecurringSettings.LastExecutedGameDay = GetCurrentGameTime()?.day;

                        // Persist immediately so the cooldown survives a game restart.
                        SaveRecurringOrders();
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
                        MelonLogger.Warning($"[RecurringOrders] ✗ Order for {record.StoreName} (ID={record.ID}, destination={record.Destination}, dock={record.LoadingDockIndex + 1}) failed. Retrying in {FailureCooldownDuration.TotalSeconds}s.");
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

        // Delegates to the shared utility — single implementation lives in NameFormatter.
        private static string NormalizeForMatch(string value) => NameFormatter.NormalizeForMatch(value);

        #endregion
    }
}