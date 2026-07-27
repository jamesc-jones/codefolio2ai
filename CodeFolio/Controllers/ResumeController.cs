using CodeFolio.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CodeFolio.Controllers;

public class ResumeController : Controller
{
    private readonly AppDbContext _context;

    public ResumeController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var resumeSections = await _context.ResumeSections.ToListAsync();
        return View(resumeSections);
    }
    
    [HttpGet]
    [Authorize]  // Anyone logged in can view the Dynamic resume 
    public async Task<IActionResult> Dynamic()
    {
        var resumeSections = await _context.ResumeSections.ToListAsync();
        return View("Dynamic", resumeSections);
    }
}