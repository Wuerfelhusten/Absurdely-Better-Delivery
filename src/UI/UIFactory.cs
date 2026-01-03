// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace AbsurdelyBetterDelivery.UI
{
    /// <summary>
    /// Factory class for creating reusable UI components.
    /// Provides methods for creating rows, text elements, and layout containers.
    /// </summary>
    public static class UIFactory
    {
        #region Constants

        /// <summary>Maximum length for item text before truncation.</summary>
        public const int MaxItemTextLength = 22;

        /// <summary>Default spacing between row items.</summary>
        public const float DefaultRowSpacing = 10f;

        /// <summary>Spacing used for item columns.</summary>
        public const float ItemColumnSpacing = 80f;

        /// <summary>Fixed width for item text columns.</summary>
        public const float ItemColumnWidth = 180f;

        #endregion

        #region Layout Containers

        /// <summary>
        /// Creates a horizontal layout row.
        /// </summary>
        /// <param name="parent">Parent transform to attach the row to.</param>
        /// <param name="name">Name for the GameObject.</param>
        /// <param name="spacing">Spacing between child elements.</param>
        /// <param name="controlWidth">Whether to control child width automatically.</param>
        /// <returns>The created row GameObject.</returns>
        public static GameObject CreateHorizontalRow(Transform parent, string name, float spacing = DefaultRowSpacing, bool controlWidth = true)
        {
            var row = new GameObject(name);
            row.transform.SetParent(parent, false);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = controlWidth;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = spacing;

            return row;
        }

        /// <summary>
        /// Creates a vertical layout container with standard settings.
        /// </summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="name">Container name.</param>
        /// <param name="spacing">Vertical spacing between children.</param>
        /// <param name="padding">Padding around the container content.</param>
        /// <returns>The created container GameObject.</returns>
        public static GameObject CreateVerticalContainer(Transform parent, string name, float spacing = 10f, RectOffset? padding = null)
        {
            var container = new GameObject(name);
            container.transform.SetParent(parent, false);
            container.transform.localScale = Vector3.one;

            var rect = container.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var layout = container.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 20);

            var fitter = container.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return container;
        }

        /// <summary>
        /// Creates an items container for displaying delivery items in rows.
        /// </summary>
        /// <param name="parent">Parent transform.</param>
        /// <returns>The created container GameObject.</returns>
        public static GameObject CreateItemsContainer(Transform parent)
        {
            var container = new GameObject("ItemsContainer");
            container.transform.SetParent(parent, false);

            var layout = container.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 2f;

            return container;
        }

        #endregion

        #region Text Elements

        /// <summary>
        /// Creates a section header text element.
        /// </summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="title">Header text.</param>
        /// <param name="font">Font to use.</param>
        public static void CreateSectionHeader(Transform parent, string title, Font? font)
        {
            var headerObj = new GameObject(title.Replace(" ", "") + "Header");
            headerObj.transform.SetParent(parent, false);

            var headerText = headerObj.AddComponent<Text>();
            headerText.font = font;
            headerText.text = title;
            headerText.alignment = TextAnchor.MiddleLeft;
            headerText.color = Color.white;
            headerText.fontSize = 26;
            headerText.fontStyle = FontStyle.Bold;

            var layoutElement = headerObj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 36f;
            layoutElement.preferredHeight = 36f;
            layoutElement.flexibleWidth = 1f;
        }

        /// <summary>
        /// Creates an empty state message.
        /// </summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="message">Message to display.</param>
        /// <param name="font">Font to use.</param>
        public static void CreateEmptyMessage(Transform parent, string message, Font? font)
        {
            var msgObj = new GameObject("EmptyMessage");
            msgObj.transform.SetParent(parent, false);

            var msgText = msgObj.AddComponent<Text>();
            msgText.font = font;
            msgText.text = message;
            msgText.color = new Color(0.5f, 0.5f, 0.5f);
            msgText.fontSize = 16;
            msgText.alignment = TextAnchor.MiddleLeft;

            var layoutElement = msgObj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 30f;
            layoutElement.preferredHeight = 30f;
        }

        /// <summary>
        /// Creates an item text element with fixed-width layout.
        /// </summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="quantity">Item quantity.</param>
        /// <param name="name">Item name.</param>
        /// <param name="font">Font to use.</param>
        public static void CreateItemText(Transform parent, int quantity, string name, Font? font)
        {
            var textObj = new GameObject("Item");
            textObj.transform.SetParent(parent, false);

            var text = textObj.AddComponent<Text>();
            text.font = font;
            text.fontSize = 15;
            text.color = Color.white;
            text.text = FormatItemText(quantity, name);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var layoutElement = textObj.AddComponent<LayoutElement>();
            layoutElement.minWidth = ItemColumnWidth;
            layoutElement.preferredWidth = ItemColumnWidth;
        }

        /// <summary>
        /// Creates a simple text label.
        /// </summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="name">GameObject name.</param>
        /// <param name="content">Text content.</param>
        /// <param name="font">Font to use.</param>
        /// <param name="fontSize">Font size.</param>
        /// <param name="color">Text color.</param>
        /// <param name="style">Font style.</param>
        /// <returns>The created Text component.</returns>
        public static Text CreateText(
            Transform parent,
            string name,
            string content,
            Font? font,
            int fontSize = 14,
            Color? color = null,
            FontStyle style = FontStyle.Normal)
        {
            var textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            var text = textObj.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color ?? Color.white;
            text.text = content;

            return text;
        }

        #endregion

        #region Formatting Helpers

        /// <summary>
        /// Formats item text with quantity prefix and truncation.
        /// </summary>
        /// <param name="quantity">Item quantity.</param>
        /// <param name="itemName">Raw item name.</param>
        /// <returns>Formatted string like "3x Item Name".</returns>
        public static string FormatItemText(int quantity, string itemName)
        {
            string formatted = $"{quantity}x {Utils.NameFormatter.FormatItemName(itemName)}";

            if (formatted.Length > MaxItemTextLength)
            {
                return formatted.Substring(0, MaxItemTextLength - 3) + "...";
            }

            return formatted;
        }

        /// <summary>
        /// Formats time duration in minutes to a readable string.
        /// </summary>
        /// <param name="minutes">Duration in minutes.</param>
        /// <returns>Formatted string like "1h 30min" or "45 min".</returns>
        public static string FormatTime(float minutes)
        {
            if (minutes >= 60f)
            {
                int hours = (int)(minutes / 60f);
                int mins = (int)(minutes % 60f);
                return $"{hours}h {mins}min";
            }

            return $"{(int)minutes} min";
        }

        #endregion
    }
}