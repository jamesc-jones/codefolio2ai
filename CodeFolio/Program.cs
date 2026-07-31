using CodeFolio.Data;
using CodeFolio.Models;
using CodeFolio.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

    // Add services to the container.
    builder.Services.AddControllersWithViews();
    builder.Services.AddRazorPages();     // Add this line

    // Add the context to the service collection with a connection string
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));


    // Add Identity with a default role (including RoleManager)
    // Adding required services to install ASP.NET Core Identity
    // Add Identity with Role management (including RoleManager)
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = true)
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();  // This should be sufficient for most cases

    // Register your custom ClaimsPrincipalFactory
    builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AppClaimPrincipalFactory>();

    // Add the RoleManager service explicitly (this is needed to handle roles)
    builder.Services.AddScoped<RoleManager<IdentityRole>>();

    // Configure cookie authentication paths to match the Identity Razor Pages routes
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Identity/Account/Login";
        options.AccessDeniedPath = "/Home/AccessDenied";
    });

    // Persist DataProtection keys to a mounted volume in production so container
    // restarts/redeploys don't generate a fresh key ring and invalidate every
    // existing login cookie. Left as the framework default in Development.
    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.AddDataProtection()
            .SetApplicationName("CodeFolio")
            .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"));
    }

    // Inject our SendGrid email sender
    builder.Services.AddSingleton<IEmailSender, EmailSender>();

    // AI assistant service (gracefully disabled if API key is absent)
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<IClaudeService, ClaudeService>();

    builder.Services.AddHealthChecks();

    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("contact-form", limiterOptions =>
        {
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.QueueLimit = 0;
        });

        // AI assistant endpoint — same limit as contact form, separate policy
        options.AddFixedWindowLimiter("ai-chat", limiterOptions =>
        {
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.QueueLimit = 0;
        });

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    var app = builder.Build();

    // Seed Admin User and Role (non-destructive: only creates if missing, never deletes)
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        await DbInitializer.SeedAdmin(services);
        await DbInitializer.SeedResumeSections(services);
    }



    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    // Trust forwarded headers from Nginx reverse proxy
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });

    app.UseHttpsRedirection();
    app.UseStaticFiles();           // Serve CSS/JS/img etc.
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapRazorPages();
    app.MapStaticAssets();
    app.MapHealthChecks("/health");

    app.MapControllers();

    app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();


    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}