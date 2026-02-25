// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;
using System.Collections.Generic;

namespace AbsurdelyBetterDelivery.Models
{
    /// <summary>
    /// Represents a saved delivery record in the history.
    /// Contains all information needed to repurchase the delivery.
    /// </summary>
    public class DeliveryRecord
    {
        /// <summary>
        /// Unique identifier for the delivery.
        /// </summary>
        public string ID { get; set; } = string.Empty;

        /// <summary>
        /// Name of the store the delivery was ordered from.
        /// </summary>
        public string StoreName { get; set; } = string.Empty;

        /// <summary>
        /// Destination code (property name).
        /// </summary>
        public string Destination { get; set; } = string.Empty;

        /// <summary>
        /// Loading dock index at the destination.
        /// </summary>
        public int LoadingDockIndex { get; set; }

        /// <summary>
        /// Total price of the order including delivery fee.
        /// </summary>
        public float TotalPrice { get; set; }

        /// <summary>
        /// List of items in the delivery.
        /// </summary>
        public List<DeliveryItem> Items { get; set; } = new List<DeliveryItem>();

        /// <summary>
        /// When the delivery was completed.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Whether this delivery is marked as a favorite.
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Recurring order settings, or null if not recurring.
        /// </summary>
        public RecurringSettings? RecurringSettings { get; set; }

        /// <summary>
        /// Whether this delivery has active recurring settings.
        /// </summary>
        public bool IsRecurring => RecurringSettings != null && RecurringSettings.Type != RecurringType.None;
    }
}