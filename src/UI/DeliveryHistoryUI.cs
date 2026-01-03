// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AbsurdelyBetterDelivery.Models;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.UI
{
    /// <summary>
    /// Main controller for the delivery history UI.
    /// Manages the scroll view, sections, and card creation.
    /// </summary>
    public static class DeliveryHistoryUI
    {
        #region Private Fields

        private static GameObject? _historyContainer;
        private static ScrollRect? _deliveriesScrollRect;
        private static bool _scrollRectInitialized;
        
        private static Dictionary<string, (Text? timeText, Text statusPillText, Image statusPillImg, DeliveryInstance delivery)> _activeDeliveryCards
            = new Dictionary<string, (Text?, Text, Image, DeliveryInstance)>();

        private static float _lastTimeUpdate;

        #endregion

        #region Public API

        /// <summary>
        /// Initializes the UI for the delivery app.
        /// </summary>
        /// <param name="app">The DeliveryApp instance.</param>
        public static void InitializeUI(DeliveryApp app)
        {
            RefreshHistoryUI(app);
        }

        /// <summary>
        /// Refreshes the entire history UI with current data.
        /// </summary>
        /// <param name="app">The DeliveryApp instance.</param>
        public static void RefreshHistoryUI(DeliveryApp app)
        {
            // Debug log removed to reduce spam - this is called very frequently
            _activeDeliveryCards.Clear();
            
            // Save scroll position before refresh from the correct ScrollRect
            float savedScrollPosition = 1f;
            if (_deliveriesScrollRect != null)
            {
                savedScrollPosition = _deliveriesScrollRect.verticalNormalizedPosition;
            }
            else if (app.MainScrollRect != null)
            {
                savedScrollPosition = app.MainScrollRect.verticalNormalizedPosition;
            }

            // Validate required components
            if (!ValidateAppComponents(app, out var statusContainer, out var containerParent))
            {
                return;
            }

            // Initialize scroll rect if needed
            if (!_scrollRectInitialized && app.MainScrollRect != null)
            {
                InitializeScrollRect(app, statusContainer, containerParent);
            }
            else if (!_scrollRectInitialized)
            {
                MelonLogger.Warning($"[HistoryUI] Cannot initialize ScrollRect: app.MainScrollRect={app.MainScrollRect != null}");
            }

            // Get font from prefab
            Font? font = GetFontFromApp(app);

            // Clean up old sections
            RemoveOldHistorySections(statusContainer.transform);

            // Create new history container
            _historyContainer = UIFactory.CreateVerticalContainer(statusContainer.transform, "HistorySection");

            // Check if we're in recurring selection mode
            if (RecurringSelectionUI.IsActive)
            {
                // Show inline selection UI instead of normal sections
                RecurringSelectionUI.BuildSelectionUI(_historyContainer.transform, font);
            }
            else
            {
                // Normal mode: show all sections
                var activeDeliveries = GetActiveDeliveries(app);
                HideVanillaStatusDisplays(app);

                BuildActiveDeliveriesSection(activeDeliveries, app, font);
                BuildFavoritesSection(app, font);
                BuildRecurringSection(app, font);
                BuildHistorySection(app, font);

                HideVanillaStatusDisplays(app);
            }

            // Initialize tooltip system
            InitializeTooltip(app, font);

            // Force layout rebuild
            RebuildLayout(statusContainer);
            
            // Restore scroll position after rebuild with delay to ensure layout is complete
            MelonCoroutines.Start(RestoreScrollPositionAfterFrame(savedScrollPosition, app));
        }

        /// <summary>
        /// Initializes the tooltip system if not already done.
        /// </summary>
        private static void InitializeTooltip(DeliveryApp app, Font? font)
        {
            var canvas = app.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                TooltipUI.Initialize(canvas, font);
            }
        }

        /// <summary>
        /// Updates time displays for active deliveries.
        /// Called periodically to update countdown timers.
        /// </summary>
        public static void UpdateTimeDisplays()
        {
            // Throttle updates to once per second
            if (Time.time - _lastTimeUpdate < 1f)
            {
                return;
            }

            _lastTimeUpdate = Time.time;
            var toRemove = new List<string>();

            foreach (var kvp in _activeDeliveryCards)
            {
                var (timeText, statusPillText, statusPillImg, delivery) = kvp.Value;

                // Check if UI elements are still valid
                if (statusPillText == null || statusPillText.gameObject == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Check if delivery has arrived
                if (delivery.TimeUntilArrival <= 0 || (int)delivery.Status == 2)
                {
                    statusPillText.text = "Arrived";
                    statusPillImg.color = new Color(0.2f, 0.6f, 0.3f, 1f);

                    if (timeText != null && timeText.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(timeText.gameObject);
                    }

                    toRemove.Add(kvp.Key);
                }
                else if (timeText != null && timeText.gameObject != null)
                {
                    timeText.text = UIFactory.FormatTime(delivery.TimeUntilArrival);
                }
            }

            // Clean up completed deliveries
            foreach (var key in toRemove)
            {
                _activeDeliveryCards.Remove(key);
            }
        }

        #endregion

        #region Section Builders

        /// <summary>
        /// Builds the Active Deliveries section.
        /// </summary>
        private static void BuildActiveDeliveriesSection(List<DeliveryInstance> deliveries, DeliveryApp app, Font? font)
        {
            if (_historyContainer == null) return;

            UIFactory.CreateSectionHeader(_historyContainer.transform, "Active Deliveries", font);

            if (deliveries.Count == 0)
            {
                UIFactory.CreateEmptyMessage(_historyContainer.transform, "No active deliveries", font);
            }
            else
            {
                foreach (var delivery in deliveries)
                {
                    DeliveryCardBuilder.CreateActiveDeliveryCard(delivery, _historyContainer.transform, app, font, _activeDeliveryCards);
                }
            }
        }

        /// <summary>
        /// Builds the Favorites section.
        /// </summary>
        private static void BuildFavoritesSection(DeliveryApp app, Font? font)
        {
            if (_historyContainer == null) return;

            // Favorites excludes recurring orders (recurring takes priority)
            var favorites = Managers.DeliveryHistoryManager.History.Where(r => r.IsFavorite && !r.IsRecurring).ToList();

            UIFactory.CreateSectionHeader(_historyContainer.transform, "Favorites", font);

            if (favorites.Count == 0)
            {
                UIFactory.CreateEmptyMessage(_historyContainer.transform, "Nothing saved as favorite", font);
            }
            else
            {
                foreach (var record in favorites)
                {
                    DeliveryCardBuilder.CreateHistoryCard(record, _historyContainer.transform, app, font);
                }
            }
        }

        /// <summary>
        /// Builds the Recurring Orders section.
        /// </summary>
        private static void BuildRecurringSection(DeliveryApp app, Font? font)
        {
            if (_historyContainer == null) return;

            var recurringRecords = Managers.DeliveryHistoryManager.History.Where(r => r.IsRecurring).ToList();

            UIFactory.CreateSectionHeader(_historyContainer.transform, "Recurring", font);

            if (recurringRecords.Count == 0)
            {
                UIFactory.CreateEmptyMessage(_historyContainer.transform, "No recurring orders", font);
            }
            else
            {
                foreach (var record in recurringRecords)
                {
                    DeliveryCardBuilder.CreateHistoryCard(record, _historyContainer.transform, app, font);
                }
            }
        }

        /// <summary>
        /// Builds the History section (non-favorite, non-recurring).
        /// </summary>
        private static void BuildHistorySection(DeliveryApp app, Font? font)
        {
            if (_historyContainer == null) return;

            var nonFavorites = Managers.DeliveryHistoryManager.History
                .Where(r => !r.IsFavorite && !r.IsRecurring)
                .ToList();

            UIFactory.CreateSectionHeader(_historyContainer.transform, "History", font);

            if (nonFavorites.Count == 0)
            {
                UIFactory.CreateEmptyMessage(_historyContainer.transform, "No deliveries yet.", font);
            }
            else
            {
                // Limit to 20 most recent
                foreach (var record in nonFavorites.Take(20))
                {
                    DeliveryCardBuilder.CreateHistoryCard(record, _historyContainer.transform, app, font);
                }
            }
        }

        #endregion

        #region Scroll Rect Setup

        /// <summary>
        /// Initializes the custom scroll rect for the deliveries panel.
        /// </summary>
        private static void InitializeScrollRect(DeliveryApp app, RectTransform statusContainer, Transform containerParent)
        {
            // Clone the main scroll rect
            var scrollObj = UnityEngine.Object.Instantiate(app.MainScrollRect.gameObject, containerParent, false);
            scrollObj.name = "DeliveriesScrollRect";
            _deliveriesScrollRect = scrollObj.GetComponent<ScrollRect>();
            var scrollRect = scrollObj.GetComponent<RectTransform>();

            // Configure scroll rect
            _deliveriesScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.anchorMin = statusContainer.anchorMin;
            scrollRect.anchorMax = statusContainer.anchorMax;

            // Adjust position
            var pos = statusContainer.anchoredPosition;
            pos.y += 20f;
            scrollRect.anchoredPosition = pos;

            // Adjust size
            var size = statusContainer.sizeDelta;
            size.y += 40f;
            scrollRect.sizeDelta = size;

            // Configure content
            var scrollContent = _deliveriesScrollRect.content;
            if (scrollContent != null)
            {
                // Clear existing children
                for (int i = scrollContent.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(scrollContent.GetChild(i).gameObject);
                }

                var scrollContentLayout = scrollContent.GetComponent<VerticalLayoutGroup>();
                if (scrollContentLayout != null)
                {
                    scrollContentLayout.padding = new RectOffset(0, 0, 0, 0);
                }

                // Reparent status container
                statusContainer.transform.SetParent(scrollContent, false);
                scrollContent.pivot = new Vector2(0f, 1f);
                scrollContent.anchorMin = new Vector2(0f, 1f);
                scrollContent.anchorMax = new Vector2(1f, 1f);
                scrollContent.anchoredPosition = Vector2.zero;
            }

            // Configure status container layout
            var statusLayout = statusContainer.GetComponent<VerticalLayoutGroup>();
            if (statusLayout == null)
            {
                statusLayout = statusContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                statusLayout.childForceExpandHeight = false;
                statusLayout.childForceExpandWidth = true;
                statusLayout.childControlHeight = false;
                statusLayout.childControlWidth = true;
            }
            statusLayout.padding = new RectOffset(0, 0, 0, 20);
            statusLayout.spacing = 10f;

            // Add content size fitter
            var statusFitter = statusContainer.GetComponent<ContentSizeFitter>();
            if (statusFitter == null)
            {
                statusFitter = statusContainer.gameObject.AddComponent<ContentSizeFitter>();
            }
            statusFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Force visibility of scrollbar
            if (_deliveriesScrollRect.verticalScrollbar != null)
            {
                _deliveriesScrollRect.verticalScrollbar.gameObject.SetActive(true);
            }
            else
            {
                MelonLogger.Warning("[HistoryUI] No vertical scrollbar found on ScrollRect!");
            }

            _scrollRectInitialized = true;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Validates that required app components exist.
        /// </summary>
        private static bool ValidateAppComponents(DeliveryApp app, out RectTransform statusContainer, out Transform containerParent)
        {
            statusContainer = null!;
            containerParent = null!;

            if (app.StatusDisplayContainer == null)
            {
                MelonLogger.Error("[HistoryUI] StatusDisplayContainer is null!");
                return false;
            }

            statusContainer = app.StatusDisplayContainer;
            containerParent = statusContainer.transform.parent;

            if (containerParent == null)
            {
                MelonLogger.Error("[HistoryUI] StatusDisplayContainer has no parent!");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the font from the app's status display prefab.
        /// </summary>
        private static Font? GetFontFromApp(DeliveryApp app)
        {
            if (app.StatusDisplayPrefab != null)
            {
                var prefabText = app.StatusDisplayPrefab.GetComponentInChildren<Text>();
                if (prefabText != null)
                {
                    return prefabText.font;
                }
            }

            return Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "Arial");
        }

        /// <summary>
        /// Removes old history section GameObjects.
        /// </summary>
        private static void RemoveOldHistorySections(Transform container)
        {
            var toDestroy = new List<GameObject>();

            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child.name == "HistorySection")
                {
                    toDestroy.Add(child.gameObject);
                }
            }

            foreach (var obj in toDestroy)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        /// <summary>
        /// Gets active delivery instances from the app.
        /// </summary>
        private static List<DeliveryInstance> GetActiveDeliveries(DeliveryApp app)
        {
            var deliveries = new List<DeliveryInstance>();

            if (app.statusDisplays != null)
            {
                foreach (var display in app.statusDisplays)
                {
                    if (display != null && display.DeliveryInstance != null)
                    {
                        deliveries.Add(display.DeliveryInstance);
                    }
                }
            }

            return deliveries;
        }

        /// <summary>
        /// Hides the vanilla status displays to show our custom UI instead.
        /// </summary>
        private static void HideVanillaStatusDisplays(DeliveryApp app)
        {
            // Hide status display prefabs
            if (app.statusDisplays != null)
            {
                foreach (var display in app.statusDisplays)
                {
                    if (display?.gameObject != null)
                    {
                        display.gameObject.SetActive(false);

                        var rt = display.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            rt.anchoredPosition = new Vector2(-9999f, -9999f);
                        }

                        var le = display.GetComponent<LayoutElement>();
                        if (le != null)
                        {
                            le.ignoreLayout = true;
                        }
                    }
                }
            }

            // Hide "no deliveries" indicator
            if (app.NoDeliveriesIndicator != null)
            {
                app.NoDeliveriesIndicator.gameObject.SetActive(false);
            }

            // Hide vanilla "Active Deliveries" header text
            foreach (var text in app.GetComponentsInChildren<Text>(true))
            {
                if (text != null && text.text != null)
                {
                    string txt = text.text.Trim();
                    if (txt.Equals("Active Deliveries", StringComparison.OrdinalIgnoreCase) ||
                        txt.Equals("Active Deliver", StringComparison.OrdinalIgnoreCase))
                    {
                        // Don't hide our own headers
                        if (text.transform.parent == null || text.transform.parent.name != "HistorySection")
                        {
                            text.gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Forces a layout rebuild.
        /// </summary>
        private static void RebuildLayout(RectTransform statusContainer)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(statusContainer);

            if (_deliveriesScrollRect?.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_deliveriesScrollRect.content);
            }
        }

        /// <summary>
        /// Coroutine to restore scroll position after layout is complete.
        /// </summary>
        private static System.Collections.IEnumerator RestoreScrollPositionAfterFrame(float position, DeliveryApp app)
        {
            // Wait one frame for layout to complete
            yield return null;
            
            // Restore position
            if (_deliveriesScrollRect != null)
            {
                _deliveriesScrollRect.verticalNormalizedPosition = position;
            }
            else if (app.MainScrollRect != null)
            {
                app.MainScrollRect.verticalNormalizedPosition = position;
            }
        }

        #endregion
    }
}