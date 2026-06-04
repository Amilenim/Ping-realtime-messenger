using System;

namespace Ping.Helpers
{
    public static class AvatarHelper
    {
        public static string GetColorByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#766ac8";

            int hash = 0;
            foreach (char c in name)
            {
                hash = c + ((hash << 5) - hash);
            }

            int hue = Math.Abs(hash % 360);
            return $"hsl({hue}, 60%, 50%)";
        }
    }
}