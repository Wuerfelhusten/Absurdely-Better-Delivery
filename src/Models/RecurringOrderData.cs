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
    /// Persistent data for a recurring order configuration.
    /// </summary>
    [Serializable]
    public class RecurringOrderData
    {
        /// <summary>
        /// Unique identifier for the delivery record.
        /// </summary>
        public string RecordID { get; set; } = string.Empty;

        /// <summary>
        /// The recurring type (Off, Once, Daily, Weekly, etc.).
        /// </summary>
        public RecurringType RecurringType { get; set; } = RecurringType.None;

        /// <summary>
        /// Hour of day to execute (0-23), if applicable.
        /// </summary>
        public int? Hour { get; set; }

        /// <summary>
        /// Minute of hour to execute (0-59), if applicable.
        /// </summary>
        public int? Minute { get; set; }

        /// <summary>
        /// Day of week to execute, if applicable (for Weekly).
        /// </summary>
        public DayOfWeek? DayOfWeek { get; set; }
    }

    /// <summary>
    /// Container for all recurring order configurations.
    /// </summary>
    [Serializable]
    public class RecurringOrdersData
    {
        /// <summary>
        /// List of all recurring order configurations.
        /// </summary>
        public List<RecurringOrderData> RecurringOrders { get; set; } = new List<RecurringOrderData>();
    }
}