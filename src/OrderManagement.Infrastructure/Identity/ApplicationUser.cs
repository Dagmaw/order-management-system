
using Microsoft.AspNetCore.Identity;

using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Identity
{
    public class ApplicationUser : IdentityUser
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;
    }
}
