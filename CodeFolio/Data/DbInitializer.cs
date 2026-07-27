using CodeFolio.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CodeFolio.Data;

public class DbInitializer
{
    public static async Task SeedAdmin(IServiceProvider serviceProvider)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

        string adminEmail = "admin@example.com";

        // Create Admin Role if it doesn't exist - Ensure that Admin role exists
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        // Create User Role if it doesn't exist
        if (!await roleManager.RoleExistsAsync("User"))
        {
            await roleManager.CreateAsync(new IdentityRole("User"));
        }

        // Only create the admin user if it doesn't already exist — never delete existing data.
        var existingUser = await userManager.FindByEmailAsync(adminEmail);
        if (existingUser != null)
        {
            return;
        }

        var adminPassword = configuration["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(adminPassword))
        {
            logger.LogWarning("Seed:AdminPassword is not configured. Skipping admin user creation.");
            return;
        }

        // Create new Admin user with claims
        var user = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "User"
        };
        var result = await userManager.CreateAsync(user, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Admin");

            // Add FirstName and LastName claims
            await userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FirstName", user.FirstName));
            await userManager.AddClaimAsync(user, new System.Security.Claims.Claim("LastName", user.LastName));
        }
        else
        {
            logger.LogWarning("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
    
    // Added this method to seed ResumeSections
    public static async Task SeedResumeSections(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed only when the table is empty — never remove existing rows.
        if (await context.ResumeSections.AnyAsync())
        {
            return;
        }

        context.ResumeSections.AddRange(
                new ResumeSection
                {
                    ResumeTitle = "Highlight of Skills",
                    ResumeContent = "     "
                    
                },
                new ResumeSection
                {
                    ResumeTitle = "Computer Programming Skills",
                    ResumeContent = "<hr>" +
                                    "<ul>" +
                                    "<li>Proficiency in Object-Oriented Programming (Java, C#)</li>" +
                                    "<li>Web Development Fundamentals (ASP.NET, HTML, CSS, JavaScript)</li>" +
                                    "<li>Relational Databases & SQL (Schema Design, Query Optimization)</li>" +
                                    "<li>Full-Stack Development Experience</li>" +
                                    "<li>Understanding of Software Development Life Cycle (SDLC), Agile methodologies (Scrum), and Waterfall model</li>" +
                                    "</ul>"
                    
                },
                new ResumeSection
                {
                    ResumeTitle = "Core Competency Skills",
                    ResumeContent = "<hr>" +
                                    "<ul>" +
                                    "<li>Clear and Effective Communication</li>" +
                                    "<li>Strong Interpersonal Skills</li>" +
                                    "<li>Proactive Work Ethic</li>" +
                                    "<li>Team Collaboration across the SDLC, including Agile methodologies (Scrum) and traditional models like Waterfall</li>" +
                                    "</ul>"
                    
                },
                new ResumeSection
                {
                    ResumeTitle = "Educational Experience",
                    ResumeContent =
                        "<hr>" +
                        "<p><strong>Computer Programming and Analysis</strong> | Sept. 2023 – Apr. 2025<br />George Brown College, Toronto</p>" +
                        "<ul>" +
                        "<li>Full-stack development, web applications, and mobile app development experience</li>" +
                        "<li>Proficiency in software development methodologies</li>" +
                        "<li>Studied database management and optimization</li>" +
                        "</ul>"
                },
                new ResumeSection
                {
                    ResumeTitle = "Work History",
                    ResumeContent = "<hr>" +
                                    "<p><strong>Fitness Coach/Owner</strong> | Nov. 2020 – Present<br />JJ’s Fitness Toronto</p>" +
                                    "<ul>" +
                                    "<li>Develop and implement structured training programs</li>" +
                                    "<li>Analyze client progress and adjust plans</li>" +
                                    "<li>Manage business operations, budgeting, and marketing</li>" +
                                    "</ul>"
                },
                new ResumeSection
                {
                    ResumeTitle = "Special Projects",
                    ResumeContent = "     "
                    
                },
                new ResumeSection
                {
                    ResumeTitle = " ",
                    ResumeContent = "<p><strong>Project Management Tool – Web Application Development</strong> | Jan. 2025</p>" +
                                    "<ul>" +
                                    "<li>Developed dynamic web application featuring front-end UI/UX design, back-end logic, and database integration</li>" +
                                    "<li>Integrated PostgreSQL database management for storing and retrieving data efficiently" +
                                    "<li>Focused on scalability and maintainability, applying best coding practices to optimize performance</li>" +
                                    "</ul>"
                }
            );
            await context.SaveChangesAsync();
     }
 }
