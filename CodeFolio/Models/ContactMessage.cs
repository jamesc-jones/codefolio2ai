using System.ComponentModel.DataAnnotations;

namespace CodeFolio.Models;

public class ContactMessage
{
    
    public int ContactMessageId { get; set; }
    
    [Required(ErrorMessage = "Name is required.")]
    [Display(Name = "Your Name")]
    [StringLength(75, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 75 characters.")]
    public string ContactName { get; set; }
    
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email.")]
    [Display(Name = "Your Email")]
    [StringLength(254, ErrorMessage = "Email cannot exceed 254 characters.")] // 254 is email RFC standard limit
    public string ContactEmail { get; set; }
    
    [Phone(ErrorMessage = "Please enter a valid phone number.")]
    [Display(Name = "Your Phone Number")]
    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
    public string? ContactPhone { get; set; }
    
    [Required(ErrorMessage = "Message is required.")]
    [Display(Name = "Your Message")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Message must be between 10 and 2000 characters.")]
    public string ConMessage { get; set; }
    
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}