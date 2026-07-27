using System.ComponentModel.DataAnnotations;

namespace CodeFolio.Models;

public class BlogPost
{
    public int BlogPostId { get; set; }
    
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(150, MinimumLength = 5, ErrorMessage = "The title must be between 5 and 150 characters.")]
    public string BlogTitle { get; set; }
    
    [Required(ErrorMessage = "Content is required.")]
    [StringLength(5000, MinimumLength = 20, ErrorMessage = "The content must be between 20 and 5000 characters.")]
    public string BlogContent { get; set; }
    
    [DataType(DataType.DateTime)]
    public DateTime PostedOn { get; set; } = DateTime.UtcNow;
    
    
}