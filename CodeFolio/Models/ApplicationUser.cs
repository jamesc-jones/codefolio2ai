using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CodeFolio.Models
{
    public class ApplicationUser : IdentityUser
    {
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First Name must be between 2 and 50 characters.")]
        public string FirstName { get; set; } = string.Empty;
        
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Last Name must be between 2 and 50 characters.")]
        public string LastName { get; set; } = string.Empty;
    }
}