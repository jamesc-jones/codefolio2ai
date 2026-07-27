using CodeFolio.Data;
using CodeFolio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace CodeFolio.Controllers;

public class ContactController : Controller
{
    private readonly AppDbContext _context;
    private readonly IEmailSender _emailSender;

    public ContactController(AppDbContext context, IEmailSender emailSender)
    {
        _context = context;
        _emailSender = emailSender;
    }
    
    // GET: Contact
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index()
    {
        return View();
    }
    
    // POST: Contact
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        [Bind("ContactName", "ContactEmail", "ConMessage", "ContactPhone")] ContactMessage contactMessage)
    {
        if (ModelState.IsValid)
        {
            contactMessage.SentAt = DateTime.UtcNow;
            _context.Add(contactMessage);
            await _context.SaveChangesAsync();
            
            // TODO Optionally, send an email here (SendGrid)
            // Send an email notification
            await _emailSender.SendEmailAsync(
                "djincognito2366@gmail.com", // your email
                $"New Contact Message from {contactMessage.ContactName}",
                $"From: {contactMessage.ContactName} ({contactMessage.ContactEmail})\n\n{contactMessage.ConMessage}"
            );
            
            TempData["SentAt"] = contactMessage.SentAt.ToString("o"); // Store as ISO string/format
            TempData["SenderName"] = contactMessage.ContactName;      // store name
            return RedirectToAction("ThankYou");
        }
        return View(contactMessage);
    }
    
    
    [AllowAnonymous]
    public IActionResult ThankYou()
    {
        return View();
    }
}