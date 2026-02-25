// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Services;
using AbsurdelyBetterDelivery.Utils;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone.Delivery;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.UI
{
    /// <summary>
    /// Builds and manages delivery card UI components.
    /// Handles both active delivery cards and history cards.
    /// </summary>
    public static class DeliveryCardBuilder
    {
        #region Active Delivery Cards

        /// <summary>
        /// Creates a card displaying an active delivery's status.
        /// </summary>
        /// <param name="delivery">The delivery instance.</param>
        /// <param name="parent">Parent transform for the card.</param>
        /// <param name="app">Reference to the DeliveryApp.</param>
        /// <param name="font">Font for text elements.</param>
        /// <param name="activeCards">Dictionary to track active cards for time updates.</param>
        public static void CreateActiveDeliveryCard(
            DeliveryInstance delivery,
            Transform parent,
            DeliveryApp app,
            Font? font,
            Dictionary<string, (Text? timeText, Text statusPillText, Image statusPillImg, DeliveryInstance delivery)> activeCards)
        {
            // Create card container
            var cardObj = CreateCardBase(parent, "ActiveDelivery_" + delivery.DeliveryID, app);

            // Header row: Destination + Status pill
            var headerRow = UIFactory.CreateHorizontalRow(cardObj.transform, "HeaderRow");
            CreateDestinationText(headerRow.transform, delivery.DestinationCode, delivery.LoadingDockIndex, font);
            var (pillText, pillImg) = CreateStatusPill(headerRow.transform, delivery, font, cardObj.GetComponent<Image>());

            // Store row: Store name + Time
            var storeRow = UIFactory.CreateHorizontalRow(cardObj.transform, "StoreRow");
            CreateStoreText(storeRow.transform, delivery.StoreName, font);

            // Time until arrival (only for in-transit deliveries)
            Text? timeText = null;
            if ((int)delivery.Status == 0 && delivery.TimeUntilArrival > 0)
            {
                timeText = CreateTimeText(storeRow.transform, delivery.TimeUntilArrival, font);
            }

            // Track for time updates
            if ((int)delivery.Status == 0)
            {
                activeCards[delivery.DeliveryID] = (timeText, pillText, pillImg, delivery);
            }

            // Items list
            CreateActiveItemsList(cardObj.transform, delivery.Items, font);
        }

        /// <summary>
        /// Creates a card for a queued order waiting on occupied destination/store constraints.
        /// </summary>
        /// <param name="record">Queued order record.</param>
        /// <param name="parent">Parent transform for the card.</param>
        /// <param name="app">Reference to the DeliveryApp.</param>
        /// <param name="font">Font for text elements.</param>
        public static void CreateQueuedWaitingCard(
            DeliveryRecord record,
            Transform parent,
            DeliveryApp app,
            Font? font)
        {
            var cardObj = CreateCardBase(parent, "QueuedWaiting_" + record.ID, app);

            var headerRow = UIFactory.CreateHorizontalRow(cardObj.transform, "HeaderRow");
            CreateDestinationText(headerRow.transform, record.Destination, record.LoadingDockIndex, font);
            CreateCustomStatusPill(headerRow.transform, "Waiting", new Color(0.75f, 0.2f, 0.2f, 1f), font, cardObj.GetComponent<Image>());

            var storeRow = UIFactory.CreateHorizontalRow(cardObj.transform, "StoreRow");
            CreateStoreText(storeRow.transform, record.StoreName, font);

            CreateHistoryItemsList(cardObj.transform, record.Items, font);
        }

        /// <summary>
        /// Creates the status pill showing delivery state.
        /// </summary>
        private static (Text pillText, Image pillImg) CreateStatusPill(
            Transform parent,
            DeliveryInstance delivery,
            Font? font,
            Image cardImg)
        {
            var pillObj = new GameObject("Pill");
            pillObj.transform.SetParent(parent, false);

            var pillImg = pillObj.AddComponent<Image>();
            if (cardImg.sprite != null)
            {
                pillImg.sprite = cardImg.sprite;
                pillImg.type = Image.Type.Sliced;
            }

            // Determine status text and color
            string statusText = GetStatusText((int)delivery.Status);
            Color pillColor = GetStatusColor((int)delivery.Status);
            pillImg.color = pillColor;

            // Layout
            var pillLE = pillObj.AddComponent<LayoutElement>();
            pillLE.minWidth = 80f;
            pillLE.minHeight = 24f;
            pillLE.preferredHeight = 24f;

            // Status text
            var pillTextObj = new GameObject("PillText");
            pillTextObj.transform.SetParent(pillObj.transform, false);

            var pillTextRect = pillTextObj.AddComponent<RectTransform>();
            pillTextRect.anchorMin = Vector2.zero;
            pillTextRect.anchorMax = Vector2.one;
            pillTextRect.offsetMin = Vector2.zero;
            pillTextRect.offsetMax = Vector2.zero;

            var pillText = pillTextObj.AddComponent<Text>();
            pillText.font = font;
            pillText.fontSize = 13;
            pillText.color = Color.white;
            pillText.text = statusText;
            pillText.alignment = TextAnchor.MiddleCenter;

            return (pillText, pillImg);
        }

        /// <summary>
        /// Creates a status pill with explicit text and color.
        /// </summary>
        private static (Text pillText, Image pillImg) CreateCustomStatusPill(
            Transform parent,
            string statusText,
            Color pillColor,
            Font? font,
            Image cardImg)
        {
            var pillObj = new GameObject("Pill");
            pillObj.transform.SetParent(parent, false);

            var pillImg = pillObj.AddComponent<Image>();
            if (cardImg.sprite != null)
            {
                pillImg.sprite = cardImg.sprite;
                pillImg.type = Image.Type.Sliced;
            }

            pillImg.color = pillColor;

            var pillLE = pillObj.AddComponent<LayoutElement>();
            pillLE.minWidth = 80f;
            pillLE.minHeight = 24f;
            pillLE.preferredHeight = 24f;

            var pillTextObj = new GameObject("PillText");
            pillTextObj.transform.SetParent(pillObj.transform, false);

            var pillTextRect = pillTextObj.AddComponent<RectTransform>();
            pillTextRect.anchorMin = Vector2.zero;
            pillTextRect.anchorMax = Vector2.one;
            pillTextRect.offsetMin = Vector2.zero;
            pillTextRect.offsetMax = Vector2.zero;

            var pillText = pillTextObj.AddComponent<Text>();
            pillText.font = font;
            pillText.fontSize = 13;
            pillText.color = Color.white;
            pillText.text = statusText;
            pillText.alignment = TextAnchor.MiddleCenter;

            return (pillText, pillImg);
        }

        /// <summary>
        /// Gets the display text for a delivery status.
        /// </summary>
        private static string GetStatusText(int status) => status switch
        {
            0 => "In Transit",
            1 => "Waiting",
            2 => "Arrived",
            _ => "Unknown"
        };

        /// <summary>
        /// Gets the color for a delivery status pill.
        /// </summary>
        private static Color GetStatusColor(int status) => status switch
        {
            0 => new Color(0.2f, 0.4f, 0.7f, 1f),  // Blue - In Transit
            1 => new Color(0.75f, 0.2f, 0.2f, 1f), // Red - Waiting
            2 => new Color(0.2f, 0.6f, 0.3f, 1f),  // Green - Arrived
            _ => new Color(0.35f, 0.35f, 0.35f, 1f)
        };

        /// <summary>
        /// Creates the time remaining text display.
        /// </summary>
        private static Text CreateTimeText(Transform parent, float timeUntilArrival, Font? font)
        {
            var timeObj = new GameObject("TimeUntilArrival");
            timeObj.transform.SetParent(parent, false);

            var timeText = timeObj.AddComponent<Text>();
            timeText.font = font;
            timeText.fontSize = 14;
            timeText.color = new Color(0.7f, 0.7f, 0.7f);
            timeText.text = UIFactory.FormatTime(timeUntilArrival);
            timeText.alignment = TextAnchor.MiddleRight;

            return timeText;
        }

        /// <summary>
        /// Creates the items list for an active delivery.
        /// </summary>
        private static void CreateActiveItemsList(
            Transform parent,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<StringIntPair>? items,
            Font? font)
        {
            if (items == null) return;

            var container = UIFactory.CreateItemsContainer(parent);

            var itemsList = new List<StringIntPair>();
            foreach (var item in items)
            {
                itemsList.Add(item);
            }

            // Display items in two-column rows
            for (int i = 0; i < itemsList.Count; i += 2)
            {
                var row = UIFactory.CreateHorizontalRow(container.transform, $"ItemRow_{i / 2}", UIFactory.ItemColumnSpacing, false);

                UIFactory.CreateItemText(row.transform, itemsList[i].Int, itemsList[i].String, font);

                if (i + 1 < itemsList.Count)
                {
                    UIFactory.CreateItemText(row.transform, itemsList[i + 1].Int, itemsList[i + 1].String, font);
                }
            }
        }

        #endregion

        #region History Cards

        /// <summary>
        /// Creates a card displaying a past delivery record.
        /// </summary>
        /// <param name="record">The delivery record.</param>
        /// <param name="parent">Parent transform for the card.</param>
        /// <param name="app">Reference to the DeliveryApp.</param>
        /// <param name="font">Font for text elements.</param>
        public static void CreateHistoryCard(
            DeliveryRecord record,
            Transform parent,
            DeliveryApp app,
            Font? font)
        {
            // Create card container
            var cardObj = CreateCardBase(parent, "HistoryItem_" + record.ID, app);

            // Header row: Destination + Action buttons
            var headerRow = UIFactory.CreateHorizontalRow(cardObj.transform, "HeaderRow");
            CreateDestinationText(headerRow.transform, record.Destination, record.LoadingDockIndex, font);

            // Recurring orders don't show Favorite or RepeatOnce buttons
            if (!record.IsRecurring)
            {
                CreateRepeatOnceButton(headerRow.transform, record, app);
            }

            CreateRecurringButton(headerRow.transform, record, app);

            // Favorite button at far right
            if (!record.IsRecurring)
            {
                CreateFavoriteButton(headerRow.transform, record, app);
            }

            // Store row: Store name + Price
            var storeRow = UIFactory.CreateHorizontalRow(cardObj.transform, "StoreRow");
            CreateStoreText(storeRow.transform, record.StoreName, font);

            if (record.TotalPrice > 0)
            {
                CreatePriceText(storeRow.transform, record.TotalPrice, font);
            }

            // Items list
            CreateHistoryItemsList(cardObj.transform, record.Items, font);
        }

        /// <summary>
        /// Creates the price display text.
        /// </summary>
        private static void CreatePriceText(Transform parent, float price, Font? font)
        {
            var priceObj = new GameObject("Price");
            priceObj.transform.SetParent(parent, false);

            var priceText = priceObj.AddComponent<Text>();
            priceText.font = font;
            priceText.fontSize = 14;
            priceText.color = new Color(0.4f, 0.8f, 0.4f);
            priceText.text = $"${price:F2}";
            priceText.alignment = TextAnchor.MiddleRight;
        }

        /// <summary>
        /// Creates the items list for a history record.
        /// </summary>
        private static void CreateHistoryItemsList(Transform parent, List<DeliveryItem> items, Font? font)
        {
            if (items == null || items.Count == 0) return;

            var container = UIFactory.CreateItemsContainer(parent);

            // Display items in two-column rows
            for (int i = 0; i < items.Count; i += 2)
            {
                var row = UIFactory.CreateHorizontalRow(container.transform, $"ItemRow_{i / 2}", UIFactory.ItemColumnSpacing, false);

                UIFactory.CreateItemText(row.transform, items[i].Quantity, items[i].Name, font);

                if (i + 1 < items.Count)
                {
                    UIFactory.CreateItemText(row.transform, items[i + 1].Quantity, items[i + 1].Name, font);
                }
            }
        }

        #endregion

        #region Shared Card Components

        /// <summary>
        /// Creates the base card GameObject with standard styling.
        /// </summary>
        private static GameObject CreateCardBase(Transform parent, string name, DeliveryApp app)
        {
            var cardObj = new GameObject(name);
            cardObj.transform.SetParent(parent, false);
            cardObj.transform.localScale = Vector3.one;

            // Background
            var cardImg = cardObj.AddComponent<Image>();
            SetupCardBackground(cardImg, app);

            // Layout
            var cardLayout = cardObj.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding = new RectOffset(12, 12, 10, 10);
            cardLayout.spacing = 4f;
            cardLayout.childControlHeight = true;
            cardLayout.childControlWidth = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.childForceExpandWidth = true;

            var cardLE = cardObj.AddComponent<LayoutElement>();
            cardLE.flexibleWidth = 1f;

            var cardFitter = cardObj.AddComponent<ContentSizeFitter>();
            cardFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return cardObj;
        }

        /// <summary>
        /// Sets up the card background using the game's styling.
        /// </summary>
        private static void SetupCardBackground(Image cardImg, DeliveryApp app)
        {
            if (app.StatusDisplayPrefab != null)
            {
                var prefabImg = app.StatusDisplayPrefab.GetComponent<Image>();
                if (prefabImg != null)
                {
                    cardImg.sprite = prefabImg.sprite;
                    cardImg.type = prefabImg.type;
                    cardImg.pixelsPerUnitMultiplier = prefabImg.pixelsPerUnitMultiplier;
                    cardImg.color = prefabImg.color;
                    return;
                }
            }

            // Fallback styling
            var bgSprite = Resources.FindObjectsOfTypeAll<Sprite>()
                .FirstOrDefault(s => s.name == "UISprite")
                ?? Resources.FindObjectsOfTypeAll<Sprite>()
                    .FirstOrDefault(s => s.name == "Background");

            if (bgSprite != null)
            {
                cardImg.sprite = bgSprite;
                cardImg.type = Image.Type.Sliced;
            }

            cardImg.color = new Color32(50, 50, 50, 255);
        }

        /// <summary>
        /// Creates the destination text with dock number.
        /// </summary>
        private static void CreateDestinationText(Transform parent, string destination, int dockIndex, Font? font)
        {
            var destObj = new GameObject("Destination");
            destObj.transform.SetParent(parent, false);

            var destText = destObj.AddComponent<Text>();
            destText.font = font;
            destText.fontSize = 18;
            destText.fontStyle = FontStyle.Bold;
            destText.color = Color.white;
            destText.text = $"{NameFormatter.FormatDestination(destination)} [{dockIndex + 1}]";

            var destLE = destObj.AddComponent<LayoutElement>();
            destLE.flexibleWidth = 1f;
            destLE.minWidth = 150f;
        }

        /// <summary>
        /// Creates the store name text.
        /// </summary>
        private static void CreateStoreText(Transform parent, string storeName, Font? font)
        {
            var storeObj = new GameObject("Store");
            storeObj.transform.SetParent(parent, false);

            var storeText = storeObj.AddComponent<Text>();
            storeText.font = font;
            storeText.fontSize = 14;
            storeText.color = new Color(0.7f, 0.7f, 0.7f);
            storeText.text = storeName;

            var storeLE = storeObj.AddComponent<LayoutElement>();
            storeLE.flexibleWidth = 1f;
        }

        #endregion

        #region Action Buttons

        /// <summary>
        /// Creates a 32x32 icon button.
        /// </summary>
        private static Button CreateIconButton(Transform parent, string name, Sprite? icon)
        {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            var btnImg = btnObj.AddComponent<Image>();
            btnImg.type = Image.Type.Simple;
            btnImg.sprite = icon;
            btnImg.preserveAspect = true;
            btnImg.color = Color.white;

            var btnLE = btnObj.AddComponent<LayoutElement>();
            btnLE.minWidth = 32f;
            btnLE.minHeight = 32f;

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnImg;

            return btn;
        }

        /// <summary>
        /// Creates the favorite toggle button.
        /// </summary>
        private static void CreateFavoriteButton(Transform parent, DeliveryRecord record, DeliveryApp app)
        {
            var icon = record.IsFavorite
                ? AbsurdelyBetterDeliveryMod.FavoriteIconTrue
                : AbsurdelyBetterDeliveryMod.FavoriteIconFalse;

            var btn = CreateIconButton(parent, "FavoriteButton", icon);
            var btnImg = btn.GetComponent<Image>();

            // Add tooltip
            TooltipUI.AddDynamicTooltip(btn.gameObject, () => record.IsFavorite ? "Remove from Favorites" : "Add to Favorites");

            btn.onClick.AddListener((UnityAction)(() =>
            {
                record.IsFavorite = !record.IsFavorite;
                btnImg.sprite = record.IsFavorite
                    ? AbsurdelyBetterDeliveryMod.FavoriteIconTrue
                    : AbsurdelyBetterDeliveryMod.FavoriteIconFalse;
                DeliveryHistoryManager.SaveHistory();
                DeliveryHistoryUI.RefreshHistoryUI(app);
                
                // Sync to host/clients
                if (MultiplayerManager.IsHost)
                {
                    HostSyncService.BroadcastFavoriteUpdate(record.ID, record.IsFavorite);
                }
                else if (MultiplayerManager.IsClient)
                {
                    ClientSyncService.SendFavoriteUpdate(record.ID, record.IsFavorite);
                }
            }));
        }

        /// <summary>
        /// Creates the repeat-once button for immediate reorder.
        /// </summary>
        private static void CreateRepeatOnceButton(Transform parent, DeliveryRecord record, DeliveryApp app)
        {
            var btn = CreateIconButton(parent, "RepeatOnceButton", AbsurdelyBetterDeliveryMod.RepeatOnceIcon);
            var displayedRecord = CreateRepurchaseSnapshot(record);

            // Add tooltip
            TooltipUI.AddTooltip(btn.gameObject, "Rebuy this Delivery once");

            btn.onClick.AddListener((UnityAction)(() =>
            {
                AbsurdelyBetterDeliveryMod.DebugLog(
                    $"[RepeatOnce] Repeating order from {displayedRecord.StoreName} (ID={displayedRecord.ID}, destination={displayedRecord.Destination}, dock={displayedRecord.LoadingDockIndex + 1})");
                DeliveryHistoryManager.RepurchaseRecord(displayedRecord, app);
                DeliveryHistoryUI.RefreshHistoryUI(app);
            }));
        }

        /// <summary>
        /// Creates an immutable snapshot of the rendered record for repeat-once actions.
        /// This prevents ordering a mutated in-memory record when the UI is stale.
        /// </summary>
        private static DeliveryRecord CreateRepurchaseSnapshot(DeliveryRecord source)
        {
            var snapshot = new DeliveryRecord
            {
                ID = source.ID,
                StoreName = source.StoreName,
                Destination = source.Destination,
                LoadingDockIndex = source.LoadingDockIndex,
                TotalPrice = source.TotalPrice,
                Timestamp = source.Timestamp,
                IsFavorite = source.IsFavorite,
                RecurringSettings = source.RecurringSettings
            };

            foreach (var item in source.Items)
            {
                snapshot.Items.Add(new DeliveryItem
                {
                    Name = item.Name,
                    Quantity = item.Quantity
                });
            }

            return snapshot;
        }

        /// <summary>
        /// Creates the recurring order toggle button.
        /// </summary>
        private static void CreateRecurringButton(Transform parent, DeliveryRecord record, DeliveryApp app)
        {
            var icon = record.IsRecurring
                ? AbsurdelyBetterDeliveryMod.RepeatOnIcon
                : AbsurdelyBetterDeliveryMod.RepeatOffIcon;

            var btn = CreateIconButton(parent, "RecurringButton", icon);
            var btnImg = btn.GetComponent<Image>();

            // Add tooltip with dynamic text for recurring details
            TooltipUI.AddDynamicTooltip(btn.gameObject, () => GetRecurringTooltip(record));

            Action clickAction = () =>
            {
                if (record.IsRecurring)
                {
                    // Turn off recurring - find and update the actual history record
                    var historyRecord = DeliveryHistoryManager.History.Find(r => r.ID == record.ID);
                    if (historyRecord != null)
                    {
                        historyRecord.RecurringSettings = null;
                        
                        // Sync to host/clients
                        if (MultiplayerManager.IsHost)
                        {
                            HostSyncService.BroadcastRecurringOrderUpdate(historyRecord.ID, false, null);
                        }
                        else if (MultiplayerManager.IsClient)
                        {
                            ClientSyncService.SendRecurringOrderUpdate(historyRecord.ID, false, null);
                        }
                    }
                    btnImg.sprite = AbsurdelyBetterDeliveryMod.RepeatOffIcon;
                    DeliveryHistoryManager.SaveHistory();
                    RecurringOrderService.SaveRecurringOrders();
                    DeliveryHistoryUI.RefreshHistoryUI(app);
                }
                else
                {
                    // Show inline selection UI
                    RecurringSelectionUI.Show(record, btnImg, app);
                }
            };
            btn.onClick.AddListener((UnityAction)clickAction);
        }

        /// <summary>
        /// Gets the tooltip text for the recurring button.
        /// </summary>
        private static string GetRecurringTooltip(DeliveryRecord record)
        {
            if (!record.IsRecurring || record.RecurringSettings == null)
            {
                return "Setup Recurring Order";
            }

            var settings = record.RecurringSettings;
            string timeStr = RecurringSelectionUI.FormatTimeAmPm(settings.Hour, settings.Minute);
            
            string details = settings.Type switch
            {
                RecurringType.AsSoonAsPossible => "As Soon As Possible",
                RecurringType.OnceADay => $"Once a Day at {timeStr}",
                RecurringType.OnceAWeek => $"Once a Week on {settings.DayOfWeek} at {timeStr}",
                _ => "Unknown"
            };

            return $"Remove Recurring Order:\n{details}";
        }

        #endregion
    }
}