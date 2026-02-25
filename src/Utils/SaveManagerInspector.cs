// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Linq;
using System.Reflection;
using Il2CppScheduleOne.Persistence;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Utils
{
    /// <summary>
    /// Utility for inspecting SaveManager properties and methods.
    /// </summary>
    public static class SaveManagerInspector
    {
        /// <summary>
        /// Inspects the SaveManager instance and logs all relevant save-related information.
        /// </summary>
        public static void InspectSaveManager()
        {
            var saveManager = UnityEngine.Object.FindObjectOfType<SaveManager>();
            if (saveManager == null)
            {
                MelonLogger.Warning("[SaveInspector] SaveManager not found!");
                return;
            }

            MelonLogger.Msg("[SaveInspector] ========== SaveManager Inspection ==========");

            // Get the type
            Type type = saveManager.GetType();
            MelonLogger.Msg($"[SaveInspector] Type: {type.FullName}");

            // Try specific save-related properties
            MelonLogger.Msg("[SaveInspector] ----- Key Save Properties -----");
            TryGetProperty(saveManager, "SaveName");
            TryGetProperty(saveManager, "IndividualSavesContainerPath");
            
            // List all methods
            MelonLogger.Msg("[SaveInspector] ----- All Public Methods -----");
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name.Contains("Save") || method.Name.Contains("Path") || method.Name.Contains("Name"))
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
                    MelonLogger.Msg($"[SaveInspector]   {method.Name}({parameters}): {method.ReturnType.Name}");
                }
            }

            MelonLogger.Msg("[SaveInspector] ========================================");
        }

        private static void TryGetProperty(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(obj);
                    MelonLogger.Msg($"[SaveInspector]   ✓ {propertyName}: {value}");
                }
                else
                {
                    MelonLogger.Msg($"[SaveInspector]   ✗ {propertyName}: Not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[SaveInspector]   ✗ {propertyName}: Error - {ex.Message}");
            }
        }

        private static void TryCallMethod(object obj, string methodName)
        {
            try
            {
                var method = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    var result = method.Invoke(obj, null);
                    MelonLogger.Msg($"[SaveInspector]   ✓ {methodName}(): {result}");
                }
                else
                {
                    MelonLogger.Msg($"[SaveInspector]   ✗ {methodName}(): Not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[SaveInspector]   ✗ {methodName}(): Error - {ex.Message}");
            }
        }
    }
}
