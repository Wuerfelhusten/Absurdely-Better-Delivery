// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and is licensed under Modding Forge All Rights Reserved.
// =============================================================================

using System.Collections.Generic;

namespace AbsurdelyBetterDelivery.Patches
{
    /// <summary>
    /// Tracks pending delivery prices between order placement and delivery completion.
    /// Prices are stored when an order is placed (OrderPressed) and retrieved when
    /// the delivery is completed (DeliveryCompleted).
    /// </summary>
    public static class DeliveryPriceTracker
    {
        /// <summary>
        /// Dictionary mapping store names to their pending order prices.
        /// Entries are removed when the delivery is recorded to history.
        /// </summary>
        public static Dictionary<string, float> PendingPrices { get; } = new Dictionary<string, float>();
    }
}