// =============================================================================
// Copyright (c) 2026 Modding Forge
// This file is part of Absurdely Better Delivery
// by Wuerfelhusten and falls under the license GPLv3.
// =============================================================================

using System;
using System.Collections.Generic;
using AbsurdelyBetterDelivery.Models;

namespace AbsurdelyBetterDelivery.Multiplayer
{
    /// <summary>
    /// Types of network messages that can be sent.
    /// </summary>
    public enum MessageType
    {
        // State sync messages
        FullStateSync,
        HistoryUpdate,
        RecurringOrderUpdate,
        FavoriteUpdate,
        TimeMultiplierSync,
        ClearData,
        
        // Request messages
        RequestFullState,
        
        // Recurring order execution
        ExecuteRecurringOrder,
        RecurringOrderResult,
        
        // Acknowledgments
        Ack
    }

    /// <summary>
    /// Base class for all network messages.
    /// </summary>
    [Serializable]
    public class NetworkMessage
    {
        public MessageType Type { get; set; }
        public string SenderId { get; set; } = "";
        public long Timestamp { get; set; }

        public NetworkMessage(MessageType type)
        {
            Type = type;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Message containing the complete state (history, recurring orders, favorites).
    /// Sent from host to client on join or on request.
    /// </summary>
    [Serializable]
    public class FullStateSyncMessage : NetworkMessage
    {
        public List<DeliveryRecord> History { get; set; } = new List<DeliveryRecord>();
        public List<RecurringOrderData> RecurringOrders { get; set; } = new List<RecurringOrderData>();
        public float TimeMultiplier { get; set; } = 1.0f;
        
        public FullStateSyncMessage() : base(MessageType.FullStateSync) { }
    }

    /// <summary>
    /// Message for updating a single history record.
    /// Sent when a client marks something as favorite or when a delivery completes.
    /// </summary>
    [Serializable]
    public class HistoryUpdateMessage : NetworkMessage
    {
        public DeliveryRecord Record { get; set; } = null!;
        public HistoryUpdateType UpdateType { get; set; }
        
        public HistoryUpdateMessage() : base(MessageType.HistoryUpdate) { }
    }

    public enum HistoryUpdateType
    {
        Add,
        Update,
        Remove
    }

    /// <summary>
    /// Message for updating recurring order settings.
    /// Sent when a client toggles recurring on/off or changes settings.
    /// </summary>
    [Serializable]
    public class RecurringOrderUpdateMessage : NetworkMessage
    {
        public string RecordId { get; set; } = "";
        public bool IsRecurring { get; set; }
        public RecurringSettings? Settings { get; set; }
        
        public RecurringOrderUpdateMessage() : base(MessageType.RecurringOrderUpdate) { }
    }

    /// <summary>
    /// Message for updating favorite status.
    /// Sent when a client toggles favorite on/off.
    /// </summary>
    [Serializable]
    public class FavoriteUpdateMessage : NetworkMessage
    {
        public string RecordId { get; set; } = "";
        public bool IsFavorite { get; set; }
        
        public FavoriteUpdateMessage() : base(MessageType.FavoriteUpdate) { }
    }

    /// <summary>
    /// Message requesting the host to execute a recurring order.
    /// Sent from client when they want to trigger an order manually.
    /// </summary>
    [Serializable]
    public class ExecuteRecurringOrderMessage : NetworkMessage
    {
        public string RecordId { get; set; } = "";
        
        public ExecuteRecurringOrderMessage() : base(MessageType.ExecuteRecurringOrder) { }
    }

    /// <summary>
    /// Message containing the result of a recurring order execution.
    /// Sent from host to client after attempting to execute an order.
    /// </summary>
    [Serializable]
    public class RecurringOrderResultMessage : NetworkMessage
    {
        public string RecordId { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        
        public RecurringOrderResultMessage() : base(MessageType.RecurringOrderResult) { }
    }

    /// <summary>
    /// Time multiplier synchronization message (host to clients).
    /// </summary>
    [Serializable]
    public class TimeMultiplierSyncMessage : NetworkMessage
    {
        public float Multiplier { get; set; }
        
        public TimeMultiplierSyncMessage() : base(MessageType.TimeMultiplierSync) { }
    }

    /// <summary>
    /// Clear all data message (host to clients).
    /// </summary>
    [Serializable]
    public class ClearDataMessage : NetworkMessage
    {
        public ClearDataMessage() : base(MessageType.ClearData) { }
    }

    /// <summary>
    /// Simple acknowledgment message.
    /// </summary>
    [Serializable]
    public class AckMessage : NetworkMessage
    {
        public MessageType AcknowledgedMessageType { get; set; }
        
        public AckMessage() : base(MessageType.Ack) { }
    }
}