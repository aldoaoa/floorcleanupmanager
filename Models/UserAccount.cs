using System;

namespace EsdCleaningSystem.Models
{
    public class UserAccount
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = "TECHNICIAN"; // TECHNICIAN, ADMIN
        public string Department { get; set; } = "Mantenimiento ESD";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class LoginDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class CreateUserDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = "TECHNICIAN";
        public string Department { get; set; } = "Mantenimiento ESD";
    }
}
