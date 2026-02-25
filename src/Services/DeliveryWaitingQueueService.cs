// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.UI;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.UI.Phone.Messages;
using MelonLoader;
using UnityEngine;

namespace AbsurdelyBetterDelivery.Services
{
    /// <summary>
    /// Manages queued delivery orders that must wait for an occupied destination/dock or busy shop.
    /// </summary>
    public static class DeliveryWaitingQueueService
    {
        private sealed class QueuedOrder
        {
            public string QueueId { get; set; } = string.Empty;
            public DeliveryRecord Record { get; set; } = new DeliveryRecord();
            public DateTime EnqueuedAtUtc { get; set; }
            public DateTime NextAttemptUtc { get; set; }
            public int StartAttempts { get; set; }
        }

        private static readonly List<QueuedOrder> PendingOrders = new();
        private const int MaxStartAttempts = 2;
        private static readonly TimeSpan RetryAfterFailedStart = TimeSpan.FromSeconds(30);
        private static float _lastProcessTime;
        private static int _internalPlacementDepth;

        /// <summary>
        /// Gets whether a queued-order internal placement is currently running.
        /// </summary>
        public static bool IsInternalPlacementActive => _internalPlacementDepth > 0;

        /// <summary>
        /// Returns a snapshot of queued orders for UI rendering.
        /// </summary>
        public static List<DeliveryRecord> GetQueuedOrdersSnapshot()
        {
            var result = new List<DeliveryRecord>(PendingOrders.Count);
            for (int i = 0; i < PendingOrders.Count; i++)
            {
                result.Add(PendingOrders[i].Record);
            }

            return result;
        }

        /// <summary>
        /// Checks whether the specified record should wait and queues it if required.
        /// </summary>
        /// <param name="record">Order record candidate.</param>
        /// <param name="app">Delivery app instance.</param>
        /// <param name="sendShopMessage">Whether to send a shop message when queued.</param>
        /// <returns><c>true</c> when the order was queued and should not be placed immediately.</returns>
        public static bool TryEnqueueIfBlocked(DeliveryRecord record, DeliveryApp app, bool sendShopMessage)
        {
            if (record == null || app == null)
            {
                return false;
            }

            if (!IsBlockedByActiveOrQueued(record, app))
            {
                return false;
            }

            EnqueueRecord(record, sendShopMessage);
            return true;
        }

        /// <summary>
        /// Tries to queue a manual shop order before the game creates the delivery.
        /// </summary>
        /// <param name="shop">Shop where the order is currently submitted.</param>
        /// <returns><c>true</c> if vanilla order placement should be skipped because queued.</returns>
        public static bool TryQueueFromShopSelection(DeliveryShop shop)
        {
            if (shop == null)
            {
                return false;
            }

            DeliveryApp? app = AbsurdelyBetterDeliveryMod.DeliveryAppInstance;
            if (app == null)
            {
                app = UnityEngine.Object.FindObjectOfType<DeliveryApp>();
                if (app != null)
                {
                    AbsurdelyBetterDeliveryMod.DeliveryAppInstance = app;
                }
            }

            if (app == null)
            {
                return false;
            }

            DeliveryRecord? record = BuildRecordFromShopSelection(shop);
            if (record == null)
            {
                return false;
            }

            if (!IsBlockedByActiveOrQueued(record, app))
            {
                return false;
            }

            EnqueueRecord(record, sendShopMessage: true);
            return true;
        }

        /// <summary>
        /// Processes queued orders and starts one as soon as blocking deliveries are gone.
        /// </summary>
        public static void Update()
        {
            if (PendingOrders.Count == 0)
            {
                return;
            }

            if (Time.time - _lastProcessTime < 1f)
            {
                return;
            }

            _lastProcessTime = Time.time;

            DeliveryApp? app = AbsurdelyBetterDeliveryMod.DeliveryAppInstance;
            if (app == null)
            {
                app = UnityEngine.Object.FindObjectOfType<DeliveryApp>();
                if (app != null)
                {
                    AbsurdelyBetterDeliveryMod.DeliveryAppInstance = app;
                }
            }

            if (app == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;

            for (int i = 0; i < PendingOrders.Count; i++)
            {
                QueuedOrder queued = PendingOrders[i];
                if (now < queued.NextAttemptUtc)
                {
                    continue;
                }

                if (IsBlockedByActiveDeliveries(queued.Record, app) || HasEarlierBlockingQueue(i, queued.Record))
                {
                    continue;
                }

                if (!CanResolveQueuedItems(queued.Record, app))
                {
                    MelonLogger.Warning(
                        $"[WaitingQueue] Removing queued order for '{queued.Record.StoreName}' because queued items are no longer resolvable in the shop list.");
                    PendingOrders.RemoveAt(i);
                    DeliveryHistoryUI.RefreshHistoryUI(app);
                    break;
                }

                EnterInternalPlacement();
                bool started;
                try
                {
                    started = RepurchaseService.RepurchaseRecord(queued.Record, app, allowWaitingQueue: false);
                }
                finally
                {
                    ExitInternalPlacement();
                }

                if (started)
                {
                    PendingOrders.RemoveAt(i);
                    DeliveryHistoryUI.RefreshHistoryUI(app);
                }
                else
                {
                    queued.StartAttempts++;
                    if (queued.StartAttempts >= MaxStartAttempts)
                    {
                        MelonLogger.Warning(
                            $"[WaitingQueue] Removing queued order for '{queued.Record.StoreName}' after {queued.StartAttempts} failed start attempt(s).");
                        PendingOrders.RemoveAt(i);
                        DeliveryHistoryUI.RefreshHistoryUI(app);
                    }
                    else
                    {
                        queued.NextAttemptUtc = now.Add(RetryAfterFailedStart);
                    }
                }

                // Process max one queued order per tick to avoid burst spam.
                break;
            }
        }

        private static void EnqueueRecord(DeliveryRecord source, bool sendShopMessage)
        {
            DeliveryRecord queuedRecord = CloneRecord(source);
            queuedRecord.ID = $"queued_{Guid.NewGuid():N}";
            queuedRecord.Timestamp = DateTime.Now;

            PendingOrders.Add(new QueuedOrder
            {
                QueueId = queuedRecord.ID,
                Record = queuedRecord,
                EnqueuedAtUtc = DateTime.UtcNow,
                NextAttemptUtc = DateTime.UtcNow
            });

            AbsurdelyBetterDeliveryMod.DebugLog(
                $"[WaitingQueue] Queued order for {queuedRecord.StoreName} at {queuedRecord.Destination} [Dock {queuedRecord.LoadingDockIndex + 1}]. Queue size={PendingOrders.Count}");

            if (sendShopMessage)
            {
                SendQueuedMessage(queuedRecord);
            }

            if (AbsurdelyBetterDeliveryMod.DeliveryAppInstance != null)
            {
                DeliveryHistoryUI.RefreshHistoryUI(AbsurdelyBetterDeliveryMod.DeliveryAppInstance);
            }
        }

        private static DeliveryRecord CloneRecord(DeliveryRecord source)
        {
            var clone = new DeliveryRecord
            {
                ID = source.ID,
                StoreName = source.StoreName,
                Destination = source.Destination,
                LoadingDockIndex = source.LoadingDockIndex,
                TotalPrice = source.TotalPrice,
                Timestamp = source.Timestamp,
                IsFavorite = source.IsFavorite,
                RecurringSettings = source.RecurringSettings
            };

            for (int i = 0; i < source.Items.Count; i++)
            {
                DeliveryItem item = source.Items[i];
                clone.Items.Add(new DeliveryItem { Name = item.Name, Quantity = item.Quantity });
            }

            return clone;
        }

        private static DeliveryRecord? BuildRecordFromShopSelection(DeliveryShop shop)
        {
            if (shop.listingEntries == null)
            {
                return null;
            }

            var items = new List<DeliveryItem>();
            float itemsTotal = 0f;

            foreach (var entry in shop.listingEntries)
            {
                if (entry == null || entry.SelectedQuantity <= 0 || entry.MatchingListing == null)
                {
                    continue;
                }

                string itemName = entry.ItemNameLabel != null && !string.IsNullOrWhiteSpace(entry.ItemNameLabel.text)
                    ? entry.ItemNameLabel.text
                    : entry.MatchingListing.name;

                itemName = SimplifyItemName(itemName);
                int quantity = entry.SelectedQuantity;
                float subtotal = entry.MatchingListing.Price * quantity;

                if (string.IsNullOrWhiteSpace(itemName) || quantity <= 0)
                {
                    continue;
                }

                items.Add(new DeliveryItem { Name = itemName, Quantity = quantity });
                itemsTotal += subtotal;
            }

            if (items.Count == 0)
            {
                return null;
            }

            string storeName = shop.MatchingShopInterfaceName;
            string destinationCode = string.Empty;
            int dockIndex = shop.loadingDockIndex - 1;

            try
            {
                if (shop.destinationProperty != null)
                {
                    destinationCode = shop.destinationProperty.PropertyCode ?? string.Empty;
                }
            }
            catch
            {
                destinationCode = string.Empty;
            }

            if (dockIndex < 0)
            {
                dockIndex = 0;
            }

            return new DeliveryRecord
            {
                ID = string.Empty,
                StoreName = storeName,
                Destination = destinationCode,
                LoadingDockIndex = dockIndex,
                TotalPrice = itemsTotal + shop.DeliveryFee,
                Timestamp = DateTime.Now,
                Items = items
            };
        }

        private static bool CanResolveQueuedItems(DeliveryRecord record, DeliveryApp app)
        {
            DeliveryShop? targetShop = FindShop(record.StoreName, app);
            if (targetShop == null || targetShop.listingEntries == null)
            {
                return false;
            }

            for (int itemIndex = 0; itemIndex < record.Items.Count; itemIndex++)
            {
                DeliveryItem queuedItem = record.Items[itemIndex];
                string queuedName = NormalizeItemNameForMatch(queuedItem.Name);
                bool found = false;

                foreach (var entry in targetShop.listingEntries)
                {
                    if (entry == null || entry.MatchingListing == null)
                    {
                        continue;
                    }

                    string displayName = entry.ItemNameLabel != null && !string.IsNullOrWhiteSpace(entry.ItemNameLabel.text)
                        ? entry.ItemNameLabel.text
                        : entry.MatchingListing.name;

                    string liveName = NormalizeItemNameForMatch(displayName);
                    if (!string.IsNullOrWhiteSpace(liveName) && liveName.Equals(queuedName, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static DeliveryShop? FindShop(string storeName, DeliveryApp app)
        {
            if (app.deliveryShops == null)
            {
                return null;
            }

            string target = NormalizeForMatch(storeName);
            foreach (var shop in app.deliveryShops)
            {
                if (shop == null)
                {
                    continue;
                }

                string candidateName = NormalizeForMatch(shop.MatchingShopInterfaceName);
                if (string.IsNullOrWhiteSpace(candidateName))
                {
                    candidateName = NormalizeForMatch(shop.name);
                }

                if (candidateName.Equals(target, StringComparison.Ordinal))
                {
                    return shop;
                }
            }

            return null;
        }

        private static bool IsBlockedByActiveOrQueued(DeliveryRecord candidate, DeliveryApp app)
        {
            return IsBlockedByActiveDeliveries(candidate, app) || PendingOrders.Any(existing => Conflicts(existing.Record, candidate));
        }

        private static bool IsBlockedByActiveDeliveries(DeliveryRecord candidate, DeliveryApp app)
        {
            if (app.statusDisplays == null)
            {
                return false;
            }

            foreach (var display in app.statusDisplays)
            {
                if (display == null || display.DeliveryInstance == null)
                {
                    continue;
                }

                DeliveryInstance active = display.DeliveryInstance;
                if (Conflicts(active, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEarlierBlockingQueue(int pendingIndex, DeliveryRecord candidate)
        {
            for (int i = 0; i < pendingIndex; i++)
            {
                if (Conflicts(PendingOrders[i].Record, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Conflicts(DeliveryInstance active, DeliveryRecord candidate)
        {
            bool sameStore = NormalizeForMatch(active.StoreName).Equals(NormalizeForMatch(candidate.StoreName), StringComparison.Ordinal);
            bool sameLocation =
                NormalizeForMatch(active.DestinationCode).Equals(NormalizeForMatch(candidate.Destination), StringComparison.Ordinal) &&
                active.LoadingDockIndex == candidate.LoadingDockIndex;

            return sameStore || sameLocation;
        }

        private static bool Conflicts(DeliveryRecord existing, DeliveryRecord candidate)
        {
            bool sameStore = NormalizeForMatch(existing.StoreName).Equals(NormalizeForMatch(candidate.StoreName), StringComparison.Ordinal);
            bool sameLocation =
                NormalizeForMatch(existing.Destination).Equals(NormalizeForMatch(candidate.Destination), StringComparison.Ordinal) &&
                existing.LoadingDockIndex == candidate.LoadingDockIndex;

            return sameStore || sameLocation;
        }

        private static void SendQueuedMessage(DeliveryRecord queuedRecord)
        {
            try
            {
                if (AbsurdelyBetterDeliveryMod.EnableDeliveryQueueMessages != null &&
                    !AbsurdelyBetterDeliveryMod.EnableDeliveryQueueMessages.Value)
                {
                    return;
                }

                MSGConversation? conversation = ResolveConversationForStore(queuedRecord.StoreName);

                if (conversation == null)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog(
                        $"[WaitingQueue] Could not resolve conversation for queued message ({queuedRecord.StoreName}).");
                    return;
                }

                string destination = string.IsNullOrWhiteSpace(queuedRecord.Destination)
                    ? "your location"
                    : queuedRecord.Destination;

                string normalizedStore = NormalizeForMatch(queuedRecord.StoreName);
                bool includeStoreName = IsGasStore(normalizedStore) || IsHardwareStore(normalizedStore);
                string text = includeStoreName
                    ? $"Your {queuedRecord.StoreName} delivery is queued for {destination} (Dock {queuedRecord.LoadingDockIndex + 1}) and will start once the current delivery is cleared."
                    : $"Your delivery is queued for {destination} (Dock {queuedRecord.LoadingDockIndex + 1}) and will start once the current delivery is cleared.";
                Message message = new(text, Message.ESenderType.Other, _endOfGroup: true);
                conversation.SendMessage(message, notify: true, network: false);
                conversation.MoveToTop();
            }
            catch (Exception ex)
            {
                AbsurdelyBetterDeliveryMod.DebugLog($"[WaitingQueue] Failed to send queued message: {ex.Message}");
            }
        }

        private static MSGConversation? ResolveConversationForStore(string storeName)
        {
            string normalizedStore = NormalizeForMatch(storeName);
            if (string.IsNullOrWhiteSpace(normalizedStore))
            {
                return null;
            }

            var conversations = MessagesApp.Conversations;
            if (conversations != null)
            {
                for (int i = 0; i < conversations.Count; i++)
                {
                    MSGConversation candidate = conversations[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    int score = ScoreConversation(candidate, normalizedStore);
                    if (score >= 60)
                    {
                        return candidate;
                    }
                }
            }

            NPC? npc = ResolveNpcByShopInterface(storeName) ?? ResolveNpcByNameOrId(storeName);
            if (npc == null)
            {
                return null;
            }

            if (npc.MSGConversation == null)
            {
                npc.CreateMessageConversation();
            }

            if (npc.MSGConversation != null)
            {
                npc.MSGConversation.SetIsKnown(true);
            }

            return npc.MSGConversation;
        }

        private static int ScoreConversation(MSGConversation conversation, string normalizedStore)
        {
            int best = ScoreNamePair(normalizedStore, NormalizeForMatch(conversation.contactName));
            if (conversation.sender != null)
            {
                best = Math.Max(best, ScoreNamePair(normalizedStore, NormalizeForMatch(conversation.sender.fullName)));
                best = Math.Max(best, ScoreNamePair(normalizedStore, NormalizeForMatch(conversation.sender.ID)));
            }

            return best;
        }

        private static int ScoreNamePair(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            if (left.Equals(right, StringComparison.Ordinal))
            {
                return 100;
            }

            if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
            {
                return 70;
            }

            return 0;
        }

        private static NPC? ResolveNpcByShopInterface(string storeName)
        {
            var registry = NPCManager.NPCRegistry;
            if (registry == null)
            {
                return null;
            }

            string normalizedStore = NormalizeForMatch(storeName);
            for (int i = 0; i < registry.Count; i++)
            {
                NPC npc = registry[i];
                if (npc == null)
                {
                    continue;
                }

                object? shopInterface = GetMemberValue(npc, "ShopInterface");
                if (shopInterface == null)
                {
                    continue;
                }

                object? shopNameValue = GetMemberValue(shopInterface, "ShopName");
                string shopName = NormalizeForMatch(shopNameValue?.ToString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(shopName) &&
                    (shopName.Equals(normalizedStore, StringComparison.Ordinal) ||
                     shopName.Contains(normalizedStore, StringComparison.Ordinal) ||
                     normalizedStore.Contains(shopName, StringComparison.Ordinal)))
                {
                    return npc;
                }
            }

            return null;
        }

        private static NPC? ResolveNpcByNameOrId(string storeName)
        {
            var registry = NPCManager.NPCRegistry;
            if (registry == null)
            {
                return null;
            }

            string normalizedStore = NormalizeForMatch(storeName);
            for (int i = 0; i < registry.Count; i++)
            {
                NPC npc = registry[i];
                if (npc == null)
                {
                    continue;
                }

                string npcName = NormalizeForMatch(npc.fullName);
                string npcId = NormalizeForMatch(npc.ID);
                if ((!string.IsNullOrWhiteSpace(npcName) &&
                     (npcName.Equals(normalizedStore, StringComparison.Ordinal) ||
                      npcName.Contains(normalizedStore, StringComparison.Ordinal) ||
                      normalizedStore.Contains(npcName, StringComparison.Ordinal))) ||
                    (!string.IsNullOrWhiteSpace(npcId) &&
                     (npcId.Equals(normalizedStore, StringComparison.Ordinal) ||
                      npcId.Contains(normalizedStore, StringComparison.Ordinal) ||
                      normalizedStore.Contains(npcId, StringComparison.Ordinal))))
                {
                    return npc;
                }
            }

            return null;
        }

        private static object? GetMemberValue(object instance, string memberName)
        {
            try
            {
                var type = instance.GetType();
                var property = type.GetProperty(memberName);
                if (property != null)
                {
                    return property.GetValue(instance);
                }

                var field = type.GetField(memberName);
                if (field != null)
                {
                    return field.GetValue(instance);
                }
            }
            catch
            {
                // Best effort reflection access.
            }

            return null;
        }

        private static string NormalizeForMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    buffer.Append(char.ToLowerInvariant(c));
                }
            }

            return buffer.ToString();
        }

        private static bool IsGasStore(string normalizedStore)
        {
            return normalizedStore.Contains("gas", StringComparison.Ordinal) ||
                   normalizedStore.Contains("fuel", StringComparison.Ordinal);
        }

        private static bool IsHardwareStore(string normalizedStore)
        {
            return normalizedStore.Contains("hardware", StringComparison.Ordinal) ||
                   normalizedStore.Contains("tool", StringComparison.Ordinal);
        }

        private static string NormalizeItemNameForMatch(string value)
        {
            return NormalizeForMatch(SimplifyItemName(value));
        }

        private static void EnterInternalPlacement()
        {
            _internalPlacementDepth++;
        }

        private static void ExitInternalPlacement()
        {
            if (_internalPlacementDepth > 0)
            {
                _internalPlacementDepth--;
            }
        }

        /// <summary>
        /// Simplifies rich listing labels (price/category/rank metadata) to a stable base item name.
        /// </summary>
        private static string SimplifyItemName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int cutIndex = value.IndexOf('(');
            if (cutIndex < 0)
            {
                cutIndex = value.IndexOf('[');
            }

            string trimmed = cutIndex > 0 ? value.Substring(0, cutIndex) : value;
            return trimmed.Trim();
        }
    }
}
