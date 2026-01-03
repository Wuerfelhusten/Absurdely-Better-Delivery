// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

namespace AbsurdelyBetterDelivery.Models
{
    /// <summary>
    /// Represents a single item in a delivery order.
    /// </summary>
    public class DeliveryItem
    {
        /// <summary>
        /// The internal name of the item.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The quantity ordered.
        /// </summary>
        public int Quantity { get; set; }
    }
}