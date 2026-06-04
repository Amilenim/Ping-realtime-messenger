using System;
using System.Collections.Generic;

namespace Ping.Models;

public partial class Message
{
    public int Id { get; set; }

    public int ChatId { get; set; }

    public DateTime CreatedAt { get; set; }

    public int SenderId { get; set; }

    public string Content { get; set; } = null!;

    public bool IsRead { get; set; }

    public bool IsEdited { get; set; }

    public virtual Chat Chat { get; set; } = null!;

    public virtual ICollection<MessageReaction> MessageReactions { get; set; } = new List<MessageReaction>();

    public virtual User Sender { get; set; } = null!;
}
