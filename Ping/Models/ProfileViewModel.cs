using System.ComponentModel.DataAnnotations;

namespace Ping.Models
{
    public class ProfileViewModel
    {
        [Required(ErrorMessage = "Ім'я не може бути порожнім")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Телефон не може бути порожнім")]
        public string PhoneNumber { get; set; } = null!;

        public string? CurrentPassword { get; set; }

        public string? NewPassword { get; set; }

        public string? ConfirmPassword { get; set; }
        public string? AvatarPath { get; set; }
        public string Username { get; set; }
    }
}