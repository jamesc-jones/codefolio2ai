using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CodeFolio.Models;

namespace CodeFolio.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects { get; set; }
    
    public DbSet<ResumeSection> ResumeSections { get; set; }
    
    public DbSet<BlogPost> BlogPosts { get; set; }
    
    public DbSet<ContactMessage> ContactMessages { get; set; }
}