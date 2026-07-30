using CodeFolio.Controllers;
using CodeFolio.Data;
using CodeFolio.Models;
using CodeFolio.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CodeFolio.Tests.Controllers;

public class ContactControllerTests
{
    private static AppDbContext NewInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Index_Post_WithValidMessage_RedirectsToThankYouAndPersists()
    {
        await using var context = NewInMemoryContext();
        var controller = new ContactController(context, new FakeEmailSender())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider())
        };

        var message = new ContactMessage
        {
            ContactName = "Test User",
            ContactEmail = "test@example.com",
            ConMessage = "This is a valid test message for CI."
        };

        var result = await controller.Index(message);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ThankYou", redirect.ActionName);
        Assert.Equal(1, await context.ContactMessages.CountAsync());
    }
}
