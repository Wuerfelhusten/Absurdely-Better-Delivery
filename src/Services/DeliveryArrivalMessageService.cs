using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.Messages;
using MelonLoader;
using UnityEngine;

namespace AbsurdelyBetterDelivery.Services
{
    /// <summary>
    /// Creates phone message notifications when a delivery arrives.
    /// </summary>
    public static class DeliveryArrivalMessageService
    {
        private const string GasStoreKey = "gas";
        private const string HardwareStoreKey = "hardware";

        private static readonly Dictionary<string, MSGConversation> CustomConversations = new();
        private static readonly HashSet<string> ReservedNpcIds = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> NotifiedArrivalDeliveryIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Sends an arrival message for the provided delivery instance.
        /// </summary>
        /// <param name="instance">Delivery instance that just arrived/completed.</param>
        public static void NotifyDeliveryArrived(DeliveryInstance instance)
        {
            try
            {
                if (AbsurdelyBetterDeliveryMod.EnableDeliveryArrivalMessages != null &&
                    !AbsurdelyBetterDeliveryMod.EnableDeliveryArrivalMessages.Value)
                {
                    return;
                }

                if (instance == null)
                {
                    return;
                }

                if (IsArrivalAlreadyNotified(instance))
                {
                    if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                    {
                        AbsurdelyBetterDeliveryMod.DebugLog($"[ArrivalMessage] Skipping duplicate arrival notification for delivery '{instance.DeliveryID}'.");
                    }
                    return;
                }

                string storeName = instance.StoreName ?? "Delivery";
                MSGConversation? conversation = ResolveConversation(instance, storeName);
                if (conversation == null)
                {
                    MelonLogger.Warning($"[ArrivalMessage] No conversation available for store '{storeName}'.");
                    return;
                }

                string destinationName = ResolveDestinationName(instance);
                int dockNumber = instance.LoadingDockIndex + 1;
                string normalizedStore = NormalizeForMatch(storeName);
                bool includeStoreName = IsGasStore(normalizedStore) || IsHardwareStore(normalizedStore);
                string messageText = includeStoreName
                    ? $"Your {storeName} delivery has arrived at {destinationName} (Dock {dockNumber})."
                    : $"Your delivery has arrived at {destinationName} (Dock {dockNumber}).";

                Message message = new(messageText, Message.ESenderType.Other, _endOfGroup: true);

                // Keep this local to avoid multiplayer duplication from server/client RPC boundaries.
                conversation.SendMessage(message, notify: true, network: false);
                conversation.MoveToTop();
                MarkArrivalNotified(instance);

                if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[ArrivalMessage] Sent message from '{conversation.contactName}' for store '{storeName}'.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ArrivalMessage] Failed to send arrival message: {ex.Message}");
            }
        }

        private static MSGConversation? ResolveConversation(DeliveryInstance instance, string storeName)
        {
            string normalizedStore = NormalizeForMatch(storeName);

            if (IsGasStore(normalizedStore))
            {
                NPC? gasNpc = ResolveGasMarketNpcByShift(normalizedStore) ?? ResolveNpcByShopInterface(storeName) ?? ResolveNpcForStore(storeName);
                if (gasNpc != null)
                {
                    if (gasNpc.MSGConversation == null)
                    {
                        gasNpc.CreateMessageConversation();
                    }

                    if (gasNpc.MSGConversation != null)
                    {
                        gasNpc.MSGConversation.SetIsKnown(true);
                        return gasNpc.MSGConversation;
                    }
                }
            }

            NPC? knownStoreNpc = ResolveKnownStoreNpc(normalizedStore);
            if (knownStoreNpc != null)
            {
                if (knownStoreNpc.MSGConversation == null)
                {
                    knownStoreNpc.CreateMessageConversation();
                }

                if (knownStoreNpc.MSGConversation != null)
                {
                    knownStoreNpc.MSGConversation.SetIsKnown(true);
                    return knownStoreNpc.MSGConversation;
                }
            }

            MSGConversation? existingConversation = FindBestConversationForStore(normalizedStore);
            if (existingConversation != null)
            {
                existingConversation.SetIsKnown(true);
                return existingConversation;
            }

            if (IsHardwareStore(normalizedStore))
            {
                return GetOrCreateCustomConversation(HardwareStoreKey, "Hardware Store Dispatch", instance);
            }

            NPC? npc = ResolveNpcByShopInterface(storeName) ?? ResolveNpcForStore(storeName);
            if (npc != null)
            {
                if (npc.MSGConversation == null)
                {
                    npc.CreateMessageConversation();
                }

                if (npc.MSGConversation != null)
                {
                    npc.MSGConversation.SetIsKnown(true);
                    return npc.MSGConversation;
                }
            }

            return GetOrCreateCustomConversation($"store:{normalizedStore}", $"{storeName} Dispatch", instance);
        }

        private static MSGConversation? GetOrCreateCustomConversation(string key, string contactName, DeliveryInstance instance)
        {
            if (CustomConversations.TryGetValue(key, out MSGConversation? existing) && existing != null)
            {
                return existing;
            }

            MSGConversation? knownConversation = FindConversationByContactName(contactName);
            if (knownConversation != null)
            {
                CustomConversations[key] = knownConversation;
                return knownConversation;
            }

            NPC? backingNpc = ResolveUnusedNpcForCustomContact(instance.StoreName);
            if (backingNpc == null)
            {
                MelonLogger.Warning($"[ArrivalMessage] Could not allocate backing NPC for contact '{contactName}'.");
                return null;
            }

            MSGConversation created = new(backingNpc, contactName);
            created.SetIsKnown(true);

            string npcId = backingNpc.ID ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(npcId))
            {
                ReservedNpcIds.Add(npcId);
            }

            CustomConversations[key] = created;
            return created;
        }

        private static MSGConversation? FindConversationByContactName(string contactName)
        {
            var conversations = MessagesApp.Conversations;
            if (conversations == null)
            {
                return null;
            }

            for (int i = 0; i < conversations.Count; i++)
            {
                MSGConversation conversation = conversations[i];
                if (conversation == null)
                {
                    continue;
                }

                if (string.Equals(conversation.contactName, contactName, StringComparison.OrdinalIgnoreCase))
                {
                    return conversation;
                }
            }

            return null;
        }

        private static NPC? ResolveNpcForStore(string storeName)
        {
            string normalizedStore = NormalizeForMatch(storeName);
            var registry = NPCManager.NPCRegistry;
            if (registry == null)
            {
                return null;
            }

            for (int i = 0; i < registry.Count; i++)
            {
                NPC npc = registry[i];
                if (npc == null)
                {
                    continue;
                }

                string npcId = NormalizeForMatch(npc.ID);
                string npcName = NormalizeForMatch(npc.fullName);

                if (!string.IsNullOrEmpty(npcId) && normalizedStore == npcId)
                {
                    return npc;
                }

                if (!string.IsNullOrEmpty(npcName) && normalizedStore == npcName)
                {
                    return npc;
                }
            }

            return null;
        }

        private static NPC? ResolveNpcByShopInterface(string storeName)
        {
            string normalizedStore = NormalizeForMatch(storeName);
            if (string.IsNullOrWhiteSpace(normalizedStore))
            {
                return null;
            }

            var registry = NPCManager.NPCRegistry;
            if (registry == null)
            {
                return null;
            }

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
                string shopName = NormalizeForMatch(shopNameValue?.ToString());
                if (string.IsNullOrWhiteSpace(shopName))
                {
                    continue;
                }

                if (shopName.Equals(normalizedStore, StringComparison.Ordinal))
                {
                    return npc;
                }
            }

            return null;
        }

        private static NPC? ResolveKnownStoreNpc(string normalizedStore)
        {
            if (normalizedStore.Contains("oscar", StringComparison.Ordinal))
            {
                return FindNpcByKeyword("oscar");
            }

            if (normalizedStore.Contains("dan", StringComparison.Ordinal))
            {
                return FindNpcByKeyword("dan");
            }

            if (normalizedStore.Contains("hank", StringComparison.Ordinal) || normalizedStore.Contains("handy", StringComparison.Ordinal))
            {
                return FindNpcByKeyword("hank");
            }

            return null;
        }

        private static NPC? ResolveGasMarketNpcByShift(string normalizedStore)
        {
            int? hour = GetCurrentGameHour();
            if (!hour.HasValue)
            {
                return null;
            }

            if (normalizedStore.Contains("west", StringComparison.Ordinal))
            {
                if (hour.Value >= 6 && hour.Value < 18)
                {
                    return FindNpcByPreferredNames("chloebowers", "chloe");
                }

                if (hour.Value >= 18 || hour.Value < 5)
                {
                    return FindNpcByPreferredNames("charlesrowland", "charles");
                }

                return FindNpcByPreferredNames("chloebowers", "chloe");
            }

            if (normalizedStore.Contains("central", StringComparison.Ordinal))
            {
                if (hour.Value >= 7 && hour.Value < 18)
                {
                    return FindNpcByPreferredNames("megcooley", "meg");
                }

                return FindNpcByPreferredNames("javierperez", "javier");
            }

            return null;
        }

        private static int? GetCurrentGameHour()
        {
            try
            {
                TimeManager? timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
                if (timeManager == null)
                {
                    return null;
                }

                int totalMinutes = timeManager.GetTotalMinSum();
                int minuteOfDay = totalMinutes % (24 * 60);
                return minuteOfDay / 60;
            }
            catch
            {
                return null;
            }
        }

        private static NPC? FindNpcByPreferredNames(params string[] normalizedAliases)
        {
            var registry = NPCManager.NPCRegistry;
            if (registry == null || normalizedAliases == null || normalizedAliases.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < registry.Count; i++)
            {
                NPC npc = registry[i];
                if (npc == null)
                {
                    continue;
                }

                string npcId = NormalizeForMatch(npc.ID);
                string npcName = NormalizeForMatch(npc.fullName);
                string npcTypeName = NormalizeForMatch(npc.GetType().Name);

                for (int aliasIndex = 0; aliasIndex < normalizedAliases.Length; aliasIndex++)
                {
                    string alias = NormalizeForMatch(normalizedAliases[aliasIndex]);
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    if ((!string.IsNullOrWhiteSpace(npcName) && npcName.Contains(alias, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(npcId) && npcId.Contains(alias, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(npcTypeName) && npcTypeName.Contains(alias, StringComparison.Ordinal)))
                    {
                        return npc;
                    }
                }
            }

            return null;
        }

        private static NPC? FindNpcByKeyword(string keyword)
        {
            var registry = NPCManager.NPCRegistry;
            if (registry == null)
            {
                return null;
            }

            string normalizedKeyword = NormalizeForMatch(keyword);
            for (int i = 0; i < registry.Count; i++)
            {
                NPC npc = registry[i];
                if (npc == null)
                {
                    continue;
                }

                string npcId = NormalizeForMatch(npc.ID);
                string npcName = NormalizeForMatch(npc.fullName);
                string npcTypeName = NormalizeForMatch(npc.GetType().Name);

                if ((!string.IsNullOrWhiteSpace(npcId) && npcId.Contains(normalizedKeyword, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(npcName) && npcName.Contains(normalizedKeyword, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(npcTypeName) && npcTypeName.Contains(normalizedKeyword, StringComparison.Ordinal)))
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
                // Ignored: best-effort member probing only.
            }

            return null;
        }

        private static MSGConversation? FindBestConversationForStore(string normalizedStore)
        {
            if (string.IsNullOrWhiteSpace(normalizedStore))
            {
                return null;
            }

            var conversations = MessagesApp.Conversations;
            if (conversations == null)
            {
                return null;
            }

            MSGConversation? bestConversation = null;
            int bestScore = 0;

            for (int i = 0; i < conversations.Count; i++)
            {
                MSGConversation conversation = conversations[i];
                if (conversation == null)
                {
                    continue;
                }

                int score = ScoreConversationMatch(conversation, normalizedStore);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestConversation = conversation;
                }
            }

            return bestScore >= 60 ? bestConversation : null;
        }

        private static int ScoreConversationMatch(MSGConversation conversation, string normalizedStore)
        {
            int bestScore = 0;

            bestScore = Math.Max(bestScore, ScoreNameMatch(normalizedStore, NormalizeForMatch(conversation.contactName)));

            if (conversation.sender != null)
            {
                bestScore = Math.Max(bestScore, ScoreNameMatch(normalizedStore, NormalizeForMatch(conversation.sender.ID)));
                bestScore = Math.Max(bestScore, ScoreNameMatch(normalizedStore, NormalizeForMatch(conversation.sender.fullName)));
            }

            return bestScore;
        }

        private static int ScoreNameMatch(string normalizedStore, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(normalizedStore))
            {
                return 0;
            }

            if (normalizedStore.Equals(candidate, StringComparison.Ordinal))
            {
                return 100;
            }

            // Require at least 4 chars for fuzzy containment to avoid accidental Hank-style matches.
            if (candidate.Length >= 4 && normalizedStore.Contains(candidate, StringComparison.Ordinal))
            {
                return 75;
            }

            if (normalizedStore.Length >= 4 && candidate.Contains(normalizedStore, StringComparison.Ordinal))
            {
                return 70;
            }

            return 0;
        }

        private static bool IsArrivalAlreadyNotified(DeliveryInstance instance)
        {
            string deliveryId = instance.DeliveryID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(deliveryId))
            {
                return false;
            }

            return NotifiedArrivalDeliveryIds.Contains(deliveryId);
        }

        private static void MarkArrivalNotified(DeliveryInstance instance)
        {
            string deliveryId = instance.DeliveryID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(deliveryId))
            {
                return;
            }

            NotifiedArrivalDeliveryIds.Add(deliveryId);
        }

        private static NPC? ResolveUnusedNpcForCustomContact(string preferredStoreName)
        {
            NPC? preferred = ResolveNpcForStore(preferredStoreName);
            if (IsValidUnusedNpc(preferred))
            {
                return preferred;
            }

            var registry = NPCManager.NPCRegistry;
            if (registry == null)
            {
                return null;
            }

            for (int i = 0; i < registry.Count; i++)
            {
                NPC npc = registry[i];
                if (IsValidUnusedNpc(npc))
                {
                    return npc;
                }
            }

            return null;
        }

        private static bool IsValidUnusedNpc(NPC? npc)
        {
            if (npc == null)
            {
                return false;
            }

            string npcId = npc.ID ?? string.Empty;
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return false;
            }

            if (ReservedNpcIds.Contains(npcId))
            {
                return false;
            }

            return npc.MSGConversation == null;
        }

        private static string ResolveDestinationName(DeliveryInstance instance)
        {
            try
            {
                var destination = instance.Destination;
                if (destination != null)
                {
                    var nameProperty = destination.GetType().GetProperty("PropertyName");
                    if (nameProperty != null)
                    {
                        object? value = nameProperty.GetValue(destination);
                        if (value != null)
                        {
                            string name = value.ToString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                return name;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall back to code below.
            }

            return string.IsNullOrWhiteSpace(instance.DestinationCode) ? "your destination" : instance.DestinationCode;
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

        private static string NormalizeForMatch(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = char.ToLowerInvariant(decomposed[i]);
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}