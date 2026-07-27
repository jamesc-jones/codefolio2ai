using System.ComponentModel.DataAnnotations;

namespace CodeFolio.Models;

public class Project
{
    public int ProjectId { get; set; }

    [Display(Name = "Project Title")]
    [Required]
    [StringLength(100, ErrorMessage = "Title must be 100 characters or fewer.")]
    public string ProjectTitle { get; set; }
    
    [Display(Name = "Course")]
    [Required]
    [StringLength(100, ErrorMessage = "Course name must be 100 characters or fewer.")]
    public string ProjectCourse { get; set; }

    [Display(Name = "Date")]
    [DataType(DataType.Date)]
    [Required]
    public DateTime ProjectDate { get; set; } = DateTime.UtcNow;
    
    [Display(Name = "Technologies")]
    [Required]
    [StringLength(200, ErrorMessage = "Technologies must be 200 characters or fewer.")]
    public string ProjectTechnologies { get; set; }

    [Display(Name = "Description")]
    [Required]
    [StringLength(1000, ErrorMessage = "Description must be 1000 characters or fewer.")]
    public string ProjectDescription { get; set; }

    [Display(Name = "Contribution / Learning Outcome")]
    [Required]
    [StringLength(1000, ErrorMessage = "Contribution must be 1000 characters or fewer.")]
    public string ProjectContribution { get; set; }

    [Url]
    [Display(Name = "YouTube Link")]
    [Required]
    [StringLength(200, ErrorMessage = "YouTube URL must be 200 characters or fewer.")]
    public string YouTubeLink { get; set; }   // YouTube URL of the project (YouTube video walkthrough)
    
    [Url]
    [Display(Name = "Image Url")]
    [StringLength(500, ErrorMessage = "Image URL must be 500 characters or fewer.")]
    public string ImageUrl { get; set; }        // Optional image link
}