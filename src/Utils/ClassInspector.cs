// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Reflection;
using MelonLoader;

namespace AbsurdelyBetterDelivery.Utils
{
    /// <summary>
    /// Debug utility for inspecting object properties, fields, and methods at runtime.
    /// Useful for exploring IL2CPP objects during mod development.
    /// </summary>
    public static class ClassInspector
    {
        #region Public Methods

        /// <summary>
        /// Logs all public properties, fields, and methods of an object instance.
        /// </summary>
        /// <param name="obj">The object to inspect.</param>
        public static void InspectInstance(object? obj)
        {
            if (obj == null)
            {
                MelonLogger.Msg("[Inspector] Object is null");
                return;
            }

            Type type = obj.GetType();
            MelonLogger.Msg($"--- Inspecting Instance of {type.FullName} ---");

            InspectProperties(obj, type);
            InspectFields(obj, type);
            InspectMethods(type);

            MelonLogger.Msg("---------------------------------------------");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Logs all public instance properties.
        /// </summary>
        private static void InspectProperties(object obj, Type type)
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (PropertyInfo prop in properties)
            {
                try
                {
                    object? val = prop.GetValue(obj);
                    MelonLogger.Msg($"  Property: {prop.Name} = {val}");
                }
                catch
                {
                    // Ignore properties that throw exceptions when accessed
                }
            }
        }

        /// <summary>
        /// Logs all public instance fields.
        /// </summary>
        private static void InspectFields(object obj, Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                try
                {
                    object? val = field.GetValue(obj);
                    MelonLogger.Msg($"  Field: {field.Name} = {val}");
                }
                catch
                {
                    // Ignore fields that throw exceptions when accessed
                }
            }
        }

        /// <summary>
        /// Logs all declared public instance methods.
        /// </summary>
        private static void InspectMethods(Type type)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);

            foreach (MethodInfo method in methods)
            {
                MelonLogger.Msg($"  Method: {method.Name}");
            }
        }

        #endregion
    }
}