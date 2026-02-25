// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System;

namespace AbsurdelyBetterDelivery.Models
{
    /// <summary>
    /// Settings for a recurring delivery order.
    /// Defines when and how often the order should be automatically placed.
    /// </summary>
    public class RecurringSettings
    {
        /// <summary>
        /// The type of recurring schedule.
        /// </summary>
        public RecurringType Type { get; set; } = RecurringType.None;

        /// <summary>
        /// The hour of day to place the order (0-23).
        /// </summary>
        public int Hour { get; set; } = 8;

        /// <summary>
        /// The minute of the hour to place the order (0-59).
        /// </summary>
        public int Minute { get; set; }

        /// <summary>
        /// For weekly orders, which day of the week.
        /// </summary>
        public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

        /// <summary>
        /// When the recurring order was last executed.
        /// Used to prevent duplicate orders.
        /// </summary>
        public DateTime? LastExecuted { get; set; }
    }
}