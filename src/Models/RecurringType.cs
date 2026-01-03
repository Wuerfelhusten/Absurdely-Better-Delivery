// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

namespace AbsurdelyBetterDelivery.Models
{
    /// <summary>
    /// Defines the frequency of recurring delivery orders.
    /// </summary>
    public enum RecurringType
    {
        /// <summary>No recurring schedule.</summary>
        None,

        /// <summary>Order once per in-game day.</summary>
        OnceADay,

        /// <summary>Order as soon as the previous delivery is complete.</summary>
        AsSoonAsPossible,

        /// <summary>Order once per in-game week.</summary>
        OnceAWeek
    }
}