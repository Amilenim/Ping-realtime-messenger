using System;
using System.Collections.Generic;

namespace Ping.Models;

public partial class Chat
{
    public int Id { get; set; }

    public int FirstUser { get; set; }

    public int SecondUser { get; set; }

    public virtual User FirstUserNavigation { get; set; } = null!;

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual User SecondUserNavigation { get; set; } = null!;
}
