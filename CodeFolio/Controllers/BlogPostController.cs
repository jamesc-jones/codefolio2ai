using CodeFolio.Data;       // To use AppDbContext
using CodeFolio.Models;
using Microsoft.AspNetCore.Authorization; // To use the Project model
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CodeFolio.Controllers;

public class BlogPostController : Controller
{
    private readonly AppDbContext _context;

    public BlogPostController(AppDbContext context)
    {
        _context = context;
    }

    // GET: View all BlogPosts
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var posts = await _context.BlogPosts.ToListAsync();
        return View(posts);
    }

    // GET: Create BlogPosts
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult Create()
    {
        return View();
    }
    
    // POST: Create BlogPosts
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("BlogTitle", "BlogContent")] BlogPost blogPost)
    {
        if (ModelState.IsValid)
        {
            blogPost.PostedOn = DateTime.UtcNow;
            _context.Add(blogPost);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        return View(blogPost);
    }

    // Edit and Delete Actions will go here...

    // GET: BlogPost/Edit/5
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var blogPost = await _context.BlogPosts.FindAsync(id);
        if (blogPost == null)
            return NotFound();

        return View(blogPost);
    }

    // POST: BlogPost/Edit/5
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("BlogPostId", "BlogTitle", "BlogContent")] BlogPost blogPost)
    {
        if (id != blogPost.BlogPostId)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(blogPost);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.BlogPosts.Any(e => e.BlogPostId == blogPost.BlogPostId))
                    return NotFound();
                else
                    throw;
            }

            return RedirectToAction(nameof(Index));
        }

        return View(blogPost);
    }
    
    // GET: BlogPost/Delete/5
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();
            
        var blogPost = await _context.BlogPosts
                .FirstOrDefaultAsync(m => m.BlogPostId == id);
            
            if (blogPost == null)
                return NotFound();
            
            return View(blogPost);
        }
    
    // POST: BlogPost/Delete/5
    [HttpPost, ActionName("Delete")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var blogPost = await _context.BlogPosts.FindAsync(id);
        if (blogPost != null)
        {
            _context.BlogPosts.Remove(blogPost);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
    
    
    }

    