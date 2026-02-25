// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace AbsurdelyBetterDelivery.Utils
{
    /// <summary>
    /// Formats internal game names (destinations, items) into human-readable display names.
    /// Uses a lookup table for known names and falls back to title-casing for unknown names.
    /// </summary>
    public static class NameFormatter
    {
        #region Name Lookup Table

        /// <summary>
        /// Lookup table for known internal names to display names.
        /// Case-insensitive matching.
        /// </summary>
        private static readonly Dictionary<string, string> NameReplacements = new(StringComparer.OrdinalIgnoreCase)
        {
            { "dockswarehouse", "Docks Warehouse" },
            { "energydrink", "Energy Drink" },
            { "flumedicine", "Flu Medicine" },
            { "mouthwash", "Mouth Wash" },
            { "trashbag", "Trash Bag" },
            { "motoroil", "Motor Oil" },
            { "horsesemen", "Horse Semen" },
            { "megabean", "Mega Bean" },
            { "storageunit", "Storage Unit" },
            { "longlifesoil", "Long-Life Soil" },
            { "pgr", "PGR" },
            { "speedgrow", "Speed Grow" },
            { "extralonglifesoil", "Extra Long-Life Soil" },
            { "ogkushseed", "OG Kush Seed" },
            { "granddaddypurpleseed", "Granddaddy Purple Seed" },
            { "greencrackseed", "Green Crack Seed" },
            { "sourdieselseed", "Sour Diesel Seed" }
        };

        #endregion

        #region Public Methods

        /// <summary>
        /// Formats an internal name to a display name.
        /// </summary>
        /// <param name="internalName">The internal game name.</param>
        /// <returns>The formatted display name.</returns>
        public static string FormatName(string? internalName)
        {
            if (string.IsNullOrEmpty(internalName))
            {
                return internalName ?? string.Empty;
            }

            string key = internalName.Trim().ToLowerInvariant();

            // Check lookup table first
            if (NameReplacements.TryGetValue(key, out string? replacement))
            {
                return replacement;
            }

            // Fallback: Replace underscores and title-case
            string formatted = internalName.Replace("_", " ");
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(formatted.ToLower());
        }

        /// <summary>
        /// Formats a destination name.
        /// </summary>
        /// <param name="destination">The internal destination name.</param>
        /// <returns>The formatted destination name.</returns>
        public static string FormatDestination(string? destination)
        {
            return FormatName(destination);
        }

        /// <summary>
        /// Formats an item name.
        /// </summary>
        /// <param name="itemName">The internal item name.</param>
        /// <returns>The formatted item name.</returns>
        public static string FormatItemName(string? itemName)
        {
            return FormatName(itemName);
        }

        #endregion
    }
}