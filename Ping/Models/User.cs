using System;
using System.Collections.Generic;

namespace Ping.Models;

public partial class User
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public string Username { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string? AvatarPath { get; set; }

    public virtual ICollection<Chat> ChatFirstUserNavigations { get; set; } = new List<Chat>();

    public virtual ICollection<Chat> ChatSecondUserNavigations { get; set; } = new List<Chat>();

    public virtual ICollection<MessageReaction> MessageReactions { get; set; } = new List<MessageReaction>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
