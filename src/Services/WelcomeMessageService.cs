// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.IO;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.Messages;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.Services
{
    /// <summary>
    /// Sends a one-time welcome message per savegame from a custom Modding Forge contact.
    /// </summary>
    public static class WelcomeMessageService
    {
        private const string ContactName = "Modding Forge";

        private static string _saveIdentifier = "Default";
        private static bool _initialized;
        private static bool _alreadySentForSave;
        private static float _nextAttemptTime;
        private static bool _readinessLogged;

        /// <summary>
        /// Initializes welcome-message tracking for a specific save.
        /// </summary>
        /// <param name="saveIdentifier">Current save identifier.</param>
        public static void Initialize(string saveIdentifier)
        {
            _saveIdentifier = string.IsNullOrWhiteSpace(saveIdentifier) ? "Default" : saveIdentifier;
            _alreadySentForSave = File.Exists(GetSentFlagPath(_saveIdentifier));
            _nextAttemptTime = 0f;
            _readinessLogged = false;
            _initialized = true;
        }

        /// <summary>
        /// Per-frame update; attempts to send the welcome message once prerequisites are available.
        /// </summary>
        public static void Update()
        {
            if (!_initialized || _alreadySentForSave)
            {
                return;
            }

            if (Time.realtimeSinceStartup < _nextAttemptTime)
            {
                return;
            }

            _nextAttemptTime = Time.realtimeSinceStartup + 2f;
            TrySendWelcomeMessage();
        }

        /// <summary>
        /// Applies the Modding Forge avatar to the given conversation if it is our custom contact.
        /// </summary>
        /// <param name="conversation">Conversation to decorate.</param>
        public static void ApplyModdingForgeAvatar(MSGConversation conversation)
        {
            if (!IsModdingForgeConversation(conversation))
            {
                return;
            }

            Sprite? avatar = AbsurdelyBetterDeliveryMod.ModdingForgeIcon;
            if (avatar == null)
            {
                return;
            }

            try
            {
                if (conversation.entry != null)
                {
                    Transform iconTransform = conversation.entry.Find("IconMask/Icon");
                    if (iconTransform != null)
                    {
                        Image iconImage = iconTransform.GetComponent<Image>();
                        if (iconImage != null)
                        {
                            iconImage.sprite = avatar;
                        }
                    }
                }

                if (conversation.isOpen)
                {
                    MessagesApp messagesApp = UnityEngine.Object.FindObjectOfType<MessagesApp>();
                    if (messagesApp != null && messagesApp.iconImage != null)
                    {
                        messagesApp.iconImage.sprite = avatar;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[WelcomeMessage] Failed to apply avatar: {ex.Message}");
            }
        }

        private static void TrySendWelcomeMessage()
        {
            if (!IsMessagesAppReady())
            {
                if (!_readinessLogged && AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog("[WelcomeMessage] Waiting for MessagesApp readiness before sending welcome message.");
                    _readinessLogged = true;
                }

                return;
            }

            try
            {
                MSGConversation? conversation = FindConversationByContactName(ContactName);
                if (conversation == null)
                {
                    NPC? backingNpc = FindBackingNpc();
                    if (backingNpc == null)
                    {
                        return;
                    }

                    conversation = new MSGConversation(backingNpc, ContactName);
                    conversation.SetIsKnown(true);
                }

                ApplyModdingForgeAvatar(conversation);

                string text = "Thanks for installing Absurdely Better Delivery. If you like this mod consider leaving an <b>Endorsement</b> on the Nexus Mods page.";
                Message message = new(text, Message.ESenderType.Other, _endOfGroup: true);
                conversation.SendMessage(message, notify: true, network: false);
                conversation.MoveToTop();

                MarkAsSent(_saveIdentifier);
                _alreadySentForSave = true;
                _readinessLogged = false;

                if (AbsurdelyBetterDeliveryMod.EnableDebugMode.Value)
                {
                    AbsurdelyBetterDeliveryMod.DebugLog($"[WelcomeMessage] Welcome message sent for save '{_saveIdentifier}'.");
                }
            }
            catch (Exception ex)
            {
                _nextAttemptTime = Time.realtimeSinceStartup + 10f;
                MelonLogger.Warning($"[WelcomeMessage] Failed to send: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures the phone messages application and required UI containers are initialized.
        /// </summary>
        /// <returns><c>true</c> when welcome-message creation can safely proceed.</returns>
        private static bool IsMessagesAppReady()
        {
            if (!PlayerSingleton<MessagesApp>.InstanceExists)
            {
                return false;
            }

            MessagesApp messagesApp = PlayerSingleton<MessagesApp>.Instance;
            if (messagesApp == null)
            {
                return false;
            }

            return messagesApp.conversationEntryContainer != null &&
                   messagesApp.conversationContainer != null &&
                   messagesApp.conversationEntryPrefab != null &&
                   messagesApp.conversationContainerPrefab != null;
        }

        private static NPC? FindBackingNpc()
        {
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

                if (npc.MSGConversation != null)
                {
                    return npc;
                }
            }

            return registry.Count > 0 ? registry[0] : null;
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

        private static bool IsModdingForgeConversation(MSGConversation? conversation)
        {
            return conversation != null &&
                   string.Equals(conversation.contactName, ContactName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSentFlagPath(string saveIdentifier)
        {
            return Path.Combine(MelonEnvironment.UserDataDirectory, $"WelcomeMessageSent_{saveIdentifier}.flag");
        }

        private static void MarkAsSent(string saveIdentifier)
        {
            File.WriteAllText(GetSentFlagPath(saveIdentifier), DateTime.UtcNow.ToString("O"));
        }
    }
}