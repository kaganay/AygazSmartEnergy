using Microsoft.AspNetCore.Identity;

namespace AygazSmartEnergy.Models
{
    public class ApplicationUser : IdentityUser
    {
        [PersonalData]
        public string FirstName { get; set; } = string.Empty;

        [PersonalData]
        public string LastName { get; set; } = string.Empty;

        [PersonalData]
        public string? Address { get; set; }

        [PersonalData]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [PersonalData]
        public DateTime? LastLoginAt { get; set; }

        [PersonalData]
        public bool IsActive { get; set; } = true;

        [PersonalData]
        public string? UserType { get; set; } = "Individual"; // Individual, Corporate, Admin

        // Navigation properties
        public virtual ICollection<Device> Devices { get; set; } = new List<Device>();
        public virtual ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    }
}
