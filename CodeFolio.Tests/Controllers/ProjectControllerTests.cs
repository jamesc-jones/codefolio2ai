using CodeFolio.Controllers;
using CodeFolio.Data;
using CodeFolio.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CodeFolio.Tests.Controllers;

public class ProjectControllerTests
{
    private static AppDbContext NewInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Index_Get_ReturnsViewResult_WithProjectList()
    {
        await using var context = NewInMemoryContext();
        context.Projects.Add(new Project
        {
            ProjectTitle = "Test Project",
            ProjectCourse = "Test Course",
            ProjectTechnologies = "C#",
            ProjectDescription = "A test project.",
            ProjectContribution = "Built it.",
            YouTubeLink = "https://youtube.com/watch?v=test",
            ImageUrl = ""
        });
        await context.SaveChangesAsync();

        var controller = new ProjectController(context);

        var result = await controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<Project>>(viewResult.Model);
        Assert.Single(model);
    }
}
