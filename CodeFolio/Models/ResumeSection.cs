using System.ComponentModel.DataAnnotations;

namespace CodeFolio.Models;

public class ResumeSection
{
    public int ResumeSectionId { get; set; }
    
    [Display(Name = "Resume Title")]
    [Required]
    [StringLength(100, ErrorMessage = "Resume Title must be 100 characters or fewer.")]
    public string ResumeTitle { get; set; }  // "Education", "Work Experience", etc.
    
    [Display(Name = "Resume Content")]
    [Required]
    [StringLength(3000, ErrorMessage = "Resume Content must be 3000 characters or fewer.")]
    public string ResumeContent { get; set; }  // HTML or Markdown
    
    
    
}