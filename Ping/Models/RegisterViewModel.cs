namespace Ping.Models
{
    public class RegisterViewModel
    {
        public string Name { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string PhoneNumber { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string ConfirmPassword { get; set; } = null!;
    }
}