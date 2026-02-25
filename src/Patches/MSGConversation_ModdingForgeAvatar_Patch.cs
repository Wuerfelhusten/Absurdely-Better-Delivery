// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using AbsurdelyBetterDelivery.Services;
using HarmonyLib;
using Il2CppScheduleOne.Messaging;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Keeps custom avatar visuals for Modding Forge message contact after UI refreshes.
    /// </summary>
    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.SetIsKnown))]
    public static class MSGConversation_SetIsKnown_Avatar_Patch
    {
        /// <summary>
        /// Re-applies custom avatar after known/unknown UI refresh.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(MSGConversation __instance)
        {
            WelcomeMessageService.ApplyModdingForgeAvatar(__instance);
        }
    }

    /// <summary>
    /// Ensures the dialogue header icon uses the custom Modding Forge avatar when opening the conversation.
    /// </summary>
    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.SetOpen))]
    public static class MSGConversation_SetOpen_Avatar_Patch
    {
        /// <summary>
        /// Re-applies custom avatar after open-state changes.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(MSGConversation __instance)
        {
            WelcomeMessageService.ApplyModdingForgeAvatar(__instance);
        }
    }
}