using CodeFolio.Data;       // To use AppDbContext
using CodeFolio.Models;
using Microsoft.AspNetCore.Authorization; // To use the Project model
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CodeFolio.Controllers;

public class ProjectController : Controller
{
    private readonly AppDbContext _context;

        public ProjectController(AppDbContext context)
        {
            _context = context;
        }

        // READ: List all projects
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var projects = await _context.Projects
                .OrderByDescending(p => p.ProjectDate)
                .ToListAsync();
            return View(projects);
        }

        // CREATE: Show the form
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // CREATE: Submit new project
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Project project)
        {
            if (ModelState.IsValid)
            {
                // Ensure ProjectDate is stored as UTC
                project.ProjectDate = DateTime.SpecifyKind(project.ProjectDate, DateTimeKind.Utc);
                
                _context.Add(project);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }

            return View(project);
        }

        // EDIT: Show the form
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            return View(project);
        }

        // EDIT: Submit changes
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Project project)
        {
            if (id != project.ProjectId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Ensure ProjectDate is stored as UTC
                    project.ProjectDate = DateTime.SpecifyKind(project.ProjectDate, DateTimeKind.Utc);
                    
                    _context.Update(project);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Projects.Any(p => p.ProjectId == id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction("Index");
            }

            return View(project);
        }

        // DELETE: Show confirmation view
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var project = await _context.Projects
                .FirstOrDefaultAsync(p => p.ProjectId == id);
            if (project == null) return NotFound();

            return View(project);
        }

        // DELETE: Confirm and delete project
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null) return NotFound();

            _context.Projects.Remove(project);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }
}