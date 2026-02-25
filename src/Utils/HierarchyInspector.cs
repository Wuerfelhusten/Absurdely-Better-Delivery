// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.Utils
{
    /// <summary>
    /// Debug utility for dumping Unity GameObject hierarchies to the log.
    /// Useful for understanding UI structure during mod development.
    /// </summary>
    public static class HierarchyInspector
    {
        #region Public Methods

        /// <summary>
        /// Dumps the full hierarchy of a GameObject to the log.
        /// </summary>
        /// <param name="root">The root GameObject to start from.</param>
        public static void DumpHierarchy(GameObject root)
        {
            MelonLogger.Msg($"Hierarchy of {root.name}:");
            DumpRecursive(root.transform, "");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Recursively dumps a transform and its children.
        /// </summary>
        /// <param name="t">The current transform.</param>
        /// <param name="indent">The current indentation string.</param>
        private static void DumpRecursive(Transform t, string indent)
        {
            MelonLogger.Msg($"{indent}- {t.name} (Active: {t.gameObject.activeSelf})");

            // Log all components
            foreach (Component comp in t.GetComponents<Component>())
            {
                MelonLogger.Msg($"{indent}  [{comp.GetType().Name}]");

                // Special handling for Text components
                if (comp is Text text)
                {
                    MelonLogger.Msg($"{indent}  Text: '{text.text}'");
                }
            }

            // Recurse into children
            for (int i = 0; i < t.childCount; i++)
            {
                DumpRecursive(t.GetChild(i), indent + "  ");
            }
        }

        #endregion
    }
}