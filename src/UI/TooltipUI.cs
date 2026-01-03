// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.UI
{
    /// <summary>
    /// Helper class to add tooltips to UI elements.
    /// Creates a local tooltip that appears above the element on hover.
    /// </summary>
    public static class TooltipUI
    {
        private static Font? _cachedFont;

        /// <summary>
        /// Sets the font to use for tooltips.
        /// </summary>
        public static void SetFont(Font? font)
        {
            _cachedFont = font;
        }

        /// <summary>
        /// Adds a static tooltip to a UI element.
        /// </summary>
        public static void AddTooltip(GameObject target, string text)
        {
            CreateLocalTooltip(target, () => text);
        }

        /// <summary>
        /// Adds a dynamic tooltip to a UI element.
        /// </summary>
        public static void AddDynamicTooltip(GameObject target, Func<string> textFunc)
        {
            CreateLocalTooltip(target, textFunc);
        }

        /// <summary>
        /// Placeholder for Initialize - not needed with local tooltips.
        /// </summary>
        public static void Initialize(Canvas canvas, Font? font)
        {
            _cachedFont = font;
        }

        private static void CreateLocalTooltip(GameObject target, Func<string> textFunc)
        {
            // Create tooltip as child of the target
            var tooltipObj = new GameObject("Tooltip");
            tooltipObj.transform.SetParent(target.transform, false);

            // Position above the button, aligned to the right edge (so it extends left, not right)
            var tooltipRect = tooltipObj.AddComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(1f, 1f);
            tooltipRect.anchorMax = new Vector2(1f, 1f);
            tooltipRect.pivot = new Vector2(1f, 0f);
            tooltipRect.anchoredPosition = new Vector2(0, 5f);

            // Background
            var bg = tooltipObj.AddComponent<Image>();
            bg.color = new Color32(20, 20, 20, 240);

            // Content size fitter
            var csf = tooltipObj.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Layout for padding
            var hlg = tooltipObj.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleCenter;

            // Text child
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(tooltipObj.transform, false);

            var text = textObj.AddComponent<Text>();
            text.font = _cachedFont;
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;

            // Start hidden
            tooltipObj.SetActive(false);

            // Add event triggers to parent
            var trigger = target.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = target.AddComponent<EventTrigger>();
            }

            // Pointer Enter - show tooltip
            var enterEntry = new EventTrigger.Entry();
            enterEntry.eventID = EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((UnityAction<BaseEventData>)((_) => {
                text.text = textFunc();
                tooltipObj.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
            }));
            trigger.triggers.Add(enterEntry);

            // Pointer Exit - hide tooltip
            var exitEntry = new EventTrigger.Entry();
            exitEntry.eventID = EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((UnityAction<BaseEventData>)((_) => {
                tooltipObj.SetActive(false);
            }));
            trigger.triggers.Add(exitEntry);
        }
    }
}