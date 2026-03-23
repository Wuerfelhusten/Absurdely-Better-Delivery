// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using MelonLoader;
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
        /// Explicit ScrollRect to forward scroll events to.
        /// Set by DeliveryHistoryUI after the custom scroll container is initialized.
        /// Takes priority over GetComponentInParent at scroll time.
        /// </summary>
        private static ScrollRect? _scrollForwardTarget;

        /// <summary>
        /// Registers the ScrollRect that tooltip-equipped buttons should forward scroll events to.
        /// Call from DeliveryHistoryUI whenever the delivery scroll rect is (re)created or torn down.
        /// </summary>
        public static void SetScrollForwardTarget(ScrollRect? rect)
        {
            _scrollForwardTarget = rect;
        }

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

            // Scroll forward — EventTrigger implements IScrollHandler unconditionally, which
            // consumes scroll events and prevents them from reaching the parent ScrollRect.
            // We forward the event explicitly:
            //   1. Prefer the registered _scrollForwardTarget (set by DeliveryHistoryUI after init).
            //   2. Fall back to GetComponentInParent for generic use outside the delivery UI.
            // In IL2CPP a direct C# cast throws InvalidCastException; TryCast<T>() is the correct pattern.
            var scrollEntry = new EventTrigger.Entry();
            scrollEntry.eventID = EventTriggerType.Scroll;
            scrollEntry.callback.AddListener((UnityAction<BaseEventData>)((eventData) => {
                var scrollRect = _scrollForwardTarget ?? target.GetComponentInParent<ScrollRect>();
                var pointerData = eventData.TryCast<PointerEventData>();
                // DIAGNOSTIC — always log so we can see if the callback fires and what state it has.
                MelonLogger.Msg($"[TooltipUI] Scroll cb: rect={scrollRect?.name ?? "null"}, delta={pointerData?.scrollDelta.ToString() ?? "null"}, target={target?.name ?? "null"}");
                if (scrollRect != null && pointerData != null)
                {
                    scrollRect.OnScroll(pointerData);
                }
            }));
            trigger.triggers.Add(scrollEntry);
        }
    }
}