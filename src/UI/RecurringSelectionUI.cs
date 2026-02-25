// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;
using AbsurdelyBetterDelivery.Managers;
using AbsurdelyBetterDelivery.Models;
using AbsurdelyBetterDelivery.Multiplayer;
using AbsurdelyBetterDelivery.Services;
using Il2CppScheduleOne.UI.Phone.Delivery;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.UI
{
    /// <summary>
    /// Multi-page wizard for recurring order configuration.
    /// Page 1: Type selection
    /// Page 2: Type-specific configuration
    /// </summary>
    public static class RecurringSelectionUI
    {
        #region Enums

        private enum Page
        {
            TypeSelection,
            ConfigureAsSoonAsPossible,
            ConfigureOnceADay,
            ConfigureOnceAWeek
        }

        #endregion

        #region Private Fields

        private static DeliveryRecord? _currentRecord;
        private static Image? _currentButtonImage;
        private static DeliveryApp? _currentApp;
        private static bool _isActive;
        private static Page _currentPage = Page.TypeSelection;

        // Configuration values
        private static int _selectedHour = 9;
        private static int _selectedMinute = 0;
        private static DayOfWeek _selectedDay = DayOfWeek.Monday;

        // UI references for updating
        private static Text? _hourText;
        private static Text? _minuteText;
        private static Text? _amPmText;
        private static Text? _dayText;

        private static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        /// <summary>
        /// Formats hour and minute to 12-hour AM/PM format.
        /// </summary>
        public static string FormatTimeAmPm(int hour, int minute)
        {
            string period = hour >= 12 ? "PM" : "AM";
            int displayHour = hour % 12;
            if (displayHour == 0) displayHour = 12;
            return $"{displayHour}:{minute:D2} {period}";
        }

        #endregion

        #region Properties

        public static bool IsActive => _isActive;

        #endregion

        #region Public API

        public static void Show(DeliveryRecord record, Image buttonImg, DeliveryApp app)
        {
            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringSelection] Show for {record.StoreName}");
            
            _currentRecord = record;
            _currentButtonImage = buttonImg;
            _currentApp = app;
            _isActive = true;
            _currentPage = Page.TypeSelection;
            
            // Reset config values
            _selectedHour = 9;
            _selectedMinute = 0;
            _selectedDay = DayOfWeek.Monday;

            // Block shop interaction
            BlockShopInteraction(app, true);

            DeliveryHistoryUI.RefreshHistoryUI(app);
        }

        public static void Close()
        {
            _isActive = false;
            _currentRecord = null;
            _currentButtonImage = null;
            _currentPage = Page.TypeSelection;
            
            // Unblock shop interaction
            if (_currentApp != null)
            {
                BlockShopInteraction(_currentApp, false);
                DeliveryHistoryUI.RefreshHistoryUI(_currentApp);
            }
            
            _currentApp = null;
        }

        public static void BuildSelectionUI(Transform container, Font? font)
        {
            if (_currentRecord == null || _currentApp == null)
            {
                Close();
                return;
            }

            switch (_currentPage)
            {
                case Page.TypeSelection:
                    BuildTypeSelectionPage(container, font);
                    break;
                case Page.ConfigureAsSoonAsPossible:
                    BuildAsSoonAsPossiblePage(container, font);
                    break;
                case Page.ConfigureOnceADay:
                    BuildOnceADayPage(container, font);
                    break;
                case Page.ConfigureOnceAWeek:
                    BuildOnceAWeekPage(container, font);
                    break;
            }
        }

        #endregion

        #region Page Builders

        private static void BuildTypeSelectionPage(Transform container, Font? font)
        {
            CreateTitle(container, "Set Recurring Order", font);
            CreateRecordInfo(container, _currentRecord!, font);
            CreateSpacer(container, 20f);

            // Three clickable options
            CreateOptionButton(container, "As Soon As Possible", font, () => {
                _currentPage = Page.ConfigureAsSoonAsPossible;
                DeliveryHistoryUI.RefreshHistoryUI(_currentApp!);
            });

            CreateSpacer(container, 10f);

            CreateOptionButton(container, "Once a Day", font, () => {
                _currentPage = Page.ConfigureOnceADay;
                DeliveryHistoryUI.RefreshHistoryUI(_currentApp!);
            });

            CreateSpacer(container, 10f);

            CreateOptionButton(container, "Once a Week", font, () => {
                _currentPage = Page.ConfigureOnceAWeek;
                DeliveryHistoryUI.RefreshHistoryUI(_currentApp!);
            });

            CreateSpacer(container, 20f);
            CreateCancelButton(container, font);
        }

        private static void BuildAsSoonAsPossiblePage(Transform container, Font? font)
        {
            CreateTitle(container, "As Soon As Possible", font);
            CreateSpacer(container, 15f);

            // Explanation
            CreateExplanationText(container, 
                "This option will automatically place an order whenever the loading bay becomes available.\n\n" +
                "The order will be placed as soon as possible after the previous delivery has been completed.", 
                font);

            CreateSpacer(container, 30f);
            CreateApplyButton(container, font, RecurringType.AsSoonAsPossible);
            CreateSpacer(container, 10f);
            CreateCancelButton(container, font);
        }

        private static void BuildOnceADayPage(Transform container, Font? font)
        {
            CreateTitle(container, "Once a Day", font);
            CreateSpacer(container, 15f);

            // Explanation
            CreateExplanationText(container, 
                "This option will automatically place an order once per day at a fixed time.\n\n" +
                "Select the time below:", 
                font);

            CreateSpacer(container, 20f);

            // Time picker
            CreateTimePicker(container, font);

            CreateSpacer(container, 30f);
            CreateApplyButton(container, font, RecurringType.OnceADay);
            CreateSpacer(container, 10f);
            CreateCancelButton(container, font);
        }

        private static void BuildOnceAWeekPage(Transform container, Font? font)
        {
            CreateTitle(container, "Once a Week", font);
            CreateSpacer(container, 15f);

            // Explanation
            CreateExplanationText(container, 
                "This option will automatically place an order once per week on a specific day and time.\n\n" +
                "Select the day and time below:", 
                font);

            CreateSpacer(container, 20f);

            // Day picker
            CreateDayPicker(container, font);

            CreateSpacer(container, 10f);

            // Time picker
            CreateTimePicker(container, font);

            CreateSpacer(container, 30f);
            CreateApplyButton(container, font, RecurringType.OnceAWeek);
            CreateSpacer(container, 10f);
            CreateCancelButton(container, font);
        }

        #endregion

        #region UI Creation Helpers

        private static void CreateTitle(Transform parent, string text, Font? font)
        {
            var obj = new GameObject("Title");
            obj.transform.SetParent(parent, false);

            var txt = obj.AddComponent<Text>();
            txt.font = font;
            txt.text = text;
            txt.fontSize = 24;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.flexibleWidth = 1f;
        }

        private static void CreateRecordInfo(Transform parent, DeliveryRecord record, Font? font)
        {
            var obj = new GameObject("RecordInfo");
            obj.transform.SetParent(parent, false);

            var txt = obj.AddComponent<Text>();
            txt.font = font;
            txt.text = $"{record.StoreName}\n{Utils.NameFormatter.FormatDestination(record.Destination)}";
            txt.fontSize = 14;
            txt.color = new Color(0.7f, 0.7f, 0.7f);
            txt.alignment = TextAnchor.MiddleCenter;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.flexibleWidth = 1f;
        }

        private static void CreateExplanationText(Transform parent, string text, Font? font)
        {
            var obj = new GameObject("Explanation");
            obj.transform.SetParent(parent, false);

            var txt = obj.AddComponent<Text>();
            txt.font = font;
            txt.text = text;
            txt.fontSize = 14;
            txt.color = new Color(0.85f, 0.85f, 0.85f);
            txt.alignment = TextAnchor.MiddleCenter;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 100f;
            le.flexibleWidth = 1f;
        }

        private static void CreateSpacer(Transform parent, float height)
        {
            var obj = new GameObject("Spacer");
            obj.transform.SetParent(parent, false);

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
        }

        private static void CreateOptionButton(Transform parent, string label, Font? font, Action onClick)
        {
            var obj = new GameObject("Option_" + label.Replace(" ", ""));
            obj.transform.SetParent(parent, false);

            var img = obj.AddComponent<Image>();
            img.color = new Color32(60, 60, 60, 255);

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.flexibleWidth = 1f;

            // Text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var txt = textObj.AddComponent<Text>();
            txt.font = font;
            txt.text = label;
            txt.fontSize = 18;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color32(80, 80, 80, 255);
            colors.pressedColor = new Color32(100, 100, 100, 255);
            btn.colors = colors;

            btn.onClick.AddListener((UnityAction)(() => onClick()));
        }

        private static void CreateTimePicker(Transform parent, Font? font)
        {
            var row = new GameObject("TimePicker");
            row.transform.SetParent(parent, false);

            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 50f;
            rowLE.flexibleWidth = 1f;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 5f;
            hlg.padding = new RectOffset(30, 30, 0, 0);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Label
            CreatePickerLabel(row.transform, "Time:", 60f, font);

            // Hour -
            CreatePickerButton(row.transform, "-", font, () => {
                _selectedHour = (_selectedHour - 1 + 24) % 24;
                UpdateTimeDisplay();
            });

            // Hour display (12-hour format)
            int displayHour = _selectedHour % 12;
            if (displayHour == 0) displayHour = 12;
            _hourText = CreatePickerValue(row.transform, displayHour.ToString(), 30f, font);

            // Hour +
            CreatePickerButton(row.transform, "+", font, () => {
                _selectedHour = (_selectedHour + 1) % 24;
                UpdateTimeDisplay();
            });

            // Separator
            CreatePickerLabel(row.transform, ":", 10f, font);

            // Minute -
            CreatePickerButton(row.transform, "-", font, () => {
                _selectedMinute = (_selectedMinute - 15 + 60) % 60;
                UpdateTimeDisplay();
            });

            // Minute display
            _minuteText = CreatePickerValue(row.transform, _selectedMinute.ToString("D2"), 30f, font);

            // Minute +
            CreatePickerButton(row.transform, "+", font, () => {
                _selectedMinute = (_selectedMinute + 15) % 60;
                UpdateTimeDisplay();
            });

            // Spacer
            CreatePickerLabel(row.transform, "", 5f, font);

            // AM/PM toggle
            _amPmText = CreatePickerValue(row.transform, _selectedHour >= 12 ? "PM" : "AM", 35f, font);

            // AM/PM toggle button
            CreatePickerButton(row.transform, "↕", font, () => {
                _selectedHour = (_selectedHour + 12) % 24;
                UpdateTimeDisplay();
            });
        }

        private static void CreateDayPicker(Transform parent, Font? font)
        {
            var row = new GameObject("DayPicker");
            row.transform.SetParent(parent, false);

            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 50f;
            rowLE.flexibleWidth = 1f;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 5f;
            hlg.padding = new RectOffset(30, 30, 0, 0);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Label
            CreatePickerLabel(row.transform, "Day:", 50f, font);

            // Day -
            CreatePickerButton(row.transform, "<", font, () => {
                int day = ((int)_selectedDay - 1 + 7) % 7;
                _selectedDay = (DayOfWeek)day;
                UpdateDayDisplay();
            });

            // Day display
            _dayText = CreatePickerValue(row.transform, DayNames[(int)_selectedDay], 100f, font);

            // Day +
            CreatePickerButton(row.transform, ">", font, () => {
                int day = ((int)_selectedDay + 1) % 7;
                _selectedDay = (DayOfWeek)day;
                UpdateDayDisplay();
            });
        }

        private static void CreatePickerLabel(Transform parent, string text, float width, Font? font)
        {
            var obj = new GameObject("Label");
            obj.transform.SetParent(parent, false);

            var txt = obj.AddComponent<Text>();
            txt.font = font;
            txt.text = text;
            txt.fontSize = 16;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
        }

        private static Text CreatePickerValue(Transform parent, string text, float width, Font? font)
        {
            var obj = new GameObject("Value");
            obj.transform.SetParent(parent, false);

            var img = obj.AddComponent<Image>();
            img.color = new Color32(40, 40, 40, 255);

            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 35f;

            // Text needs to be on a child object
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var txt = textObj.AddComponent<Text>();
            txt.font = font;
            txt.text = text;
            txt.fontSize = 16;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            return txt;
        }

        private static void CreatePickerButton(Transform parent, string text, Font? font, Action onClick)
        {
            var obj = new GameObject("Btn_" + text);
            obj.transform.SetParent(parent, false);

            var img = obj.AddComponent<Image>();
            img.color = new Color32(70, 70, 70, 255);

            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = 35f;
            le.preferredHeight = 35f;

            // Text needs to be on a child object
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var txt = textObj.AddComponent<Text>();
            txt.font = font;
            txt.text = text;
            txt.fontSize = 18;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color32(90, 90, 90, 255);
            colors.pressedColor = new Color32(110, 110, 110, 255);
            btn.colors = colors;

            btn.onClick.AddListener((UnityAction)(() => onClick()));
        }

        private static void CreateApplyButton(Transform parent, Font? font, RecurringType type)
        {
            var obj = new GameObject("ApplyButton");
            obj.transform.SetParent(parent, false);

            var img = obj.AddComponent<Image>();
            img.color = new Color32(50, 120, 50, 255);

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.flexibleWidth = 1f;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var txt = textObj.AddComponent<Text>();
            txt.font = font;
            txt.text = "Apply";
            txt.fontSize = 18;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color32(60, 140, 60, 255);
            colors.pressedColor = new Color32(70, 160, 70, 255);
            btn.colors = colors;

            btn.onClick.AddListener((UnityAction)(() => ApplySelection(type)));
        }

        private static void CreateCancelButton(Transform parent, Font? font)
        {
            var obj = new GameObject("CancelButton");
            obj.transform.SetParent(parent, false);

            var img = obj.AddComponent<Image>();
            img.color = new Color32(100, 50, 50, 255);

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.flexibleWidth = 1f;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var txt = textObj.AddComponent<Text>();
            txt.font = font;
            txt.text = "Cancel";
            txt.fontSize = 18;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color32(120, 60, 60, 255);
            colors.pressedColor = new Color32(140, 70, 70, 255);
            btn.colors = colors;

            btn.onClick.AddListener((UnityAction)Close);
        }

        #endregion

        #region Display Updates

        private static void UpdateTimeDisplay()
        {
            int displayHour = _selectedHour % 12;
            if (displayHour == 0) displayHour = 12;
            
            if (_hourText != null) _hourText.text = displayHour.ToString();
            if (_minuteText != null) _minuteText.text = _selectedMinute.ToString("D2");
            if (_amPmText != null) _amPmText.text = _selectedHour >= 12 ? "PM" : "AM";
        }

        private static void UpdateDayDisplay()
        {
            if (_dayText != null) _dayText.text = DayNames[(int)_selectedDay];
        }

        #endregion

        #region Selection Handling

        private static void ApplySelection(RecurringType type)
        {
            if (_currentRecord == null)
            {
                Close();
                return;
            }

            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringSelection] Applying {type} for {_currentRecord.StoreName}");
            AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringSelection] Hour={_selectedHour}, Minute={_selectedMinute}, Day={_selectedDay}");

            var historyRecord = DeliveryHistoryManager.History.Find(r => r.ID == _currentRecord.ID);
            if (historyRecord != null)
            {
                historyRecord.RecurringSettings = new RecurringSettings
                {
                    Type = type,
                    Hour = _selectedHour,
                    Minute = _selectedMinute,
                    DayOfWeek = _selectedDay
                };

                historyRecord.IsFavorite = false;

                AbsurdelyBetterDeliveryMod.DebugLog($"[RecurringSelection] Updated: IsFavorite={historyRecord.IsFavorite}, IsRecurring={historyRecord.IsRecurring}");
            }
            else
            {
                MelonLogger.Warning("[RecurringSelection] Record not found in History!");
            }

            if (_currentButtonImage != null)
            {
                _currentButtonImage.sprite = AbsurdelyBetterDeliveryMod.RepeatOnIcon;
            }

            DeliveryHistoryManager.SaveHistory();
            RecurringOrderService.SaveRecurringOrders();
            
            // Sync to host/clients
            if (historyRecord != null)
            {
                if (MultiplayerManager.IsHost)
                {
                    HostSyncService.BroadcastRecurringOrderUpdate(historyRecord.ID, historyRecord.IsRecurring, historyRecord.RecurringSettings);
                }
                else if (MultiplayerManager.IsClient)
                {
                    ClientSyncService.SendRecurringOrderUpdate(historyRecord.ID, historyRecord.IsRecurring, historyRecord.RecurringSettings);
                }
            }
            
            Close();
        }

        /// <summary>
        /// Blocks or unblocks shop interaction using CanvasGroup.
        /// </summary>
        private static void BlockShopInteraction(DeliveryApp app, bool block)
        {
            if (app == null || app.deliveryShops == null) return;

            foreach (var shop in app.deliveryShops)
            {
                if (shop == null) continue;

                // Get or add CanvasGroup to the shop
                var canvasGroup = shop.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = shop.gameObject.AddComponent<CanvasGroup>();
                }

                canvasGroup.interactable = !block;
                canvasGroup.alpha = block ? 0.5f : 1f;
            }
        }

        #endregion
    }
}