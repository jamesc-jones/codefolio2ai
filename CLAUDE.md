# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

CodeFolio is an ASP.NET Core 9.0 MVC web application (a personal portfolio site) using Razor views, EF Core with PostgreSQL (Npgsql), and ASP.NET Core Identity for auth. It's a single-project solution — no separate test project exists yet.

## Commands

Run all commands from the repo root (`CodeFolio.sln`) or from `CodeFolio/` (the project directory).

```
dotnet build                          # Build the solution
dotnet run --project CodeFolio        # Run the app (see launchSettings.json for ports)
dotnet watch --project CodeFolio run  # Run with hot reload
```

There is no test project in the repo currently, so there is no `dotnet test` target.

### Database (EF Core / PostgreSQL)

`CodeFolio/Migrations/` contains the `InitialCreate` migration covering all Identity tables plus `Projects`, `BlogPosts`, `ResumeSections`, `ContactMessages`. To create additional migrations or reapply:

```
dotnet ef migrations add <Name> --project CodeFolio
dotnet ef database update --project CodeFolio
```

The connection string lives in `CodeFolio/appsettings.json` under `ConnectionStrings:DefaultConnection` (Npgsql/PostgreSQL) — tracked in git with placeholder values only. Local overrides belong in `CodeFolio/appsettings.Development.json`, which is gitignored — don't commit real credentials there or to `appsettings.json`.

## Local Development Environment

Local PostgreSQL runs in Docker (development only — the ASP.NET Core app itself runs natively via `dotnet run`, not containerized). See `docker-compose.yml` at the solution root.

Prerequisites: .NET 9 SDK, Docker Desktop.

```
docker compose up -d              # Start local PostgreSQL (solution root)
docker compose down                # Stop it (keeps data volume)
docker compose down -v             # Stop it and delete the data volume (full reset)
dotnet ef database update --project CodeFolio   # Apply migrations
dotnet run --project CodeFolio     # Run the app
```

Configuration notes:
- Root `.env` (gitignored) holds Docker Compose's Postgres credentials (`POSTGRES_DB`/`POSTGRES_USER`/`POSTGRES_PASSWORD`) — copy `.env.example` to get started.
- `CodeFolio/appsettings.Development.json` (gitignored) holds the ASP.NET Core app's own config (`ConnectionStrings:DefaultConnection`, `Seed:AdminPassword`, `SendGrid:ApiKey`) — these are separate files serving separate consumers and must be kept in sync manually (same Postgres password in both).
- The containerized PostgreSQL is exposed on host port **5433** (not 5432), to avoid colliding with any natively-installed local Postgres.

## Architecture

Standard ASP.NET Core MVC layout, all under `CodeFolio/`:

- **`Program.cs`** — composition root. Registers `AppDbContext` (Npgsql), ASP.NET Core Identity (`ApplicationUser` + `IdentityRole`, with `RequireConfirmedAccount = true`), a custom `IUserClaimsPrincipalFactory`, and `IEmailSender` (SendGrid). Cookie authentication is configured via `ConfigureApplicationCookie` with `LoginPath = "/Identity/Account/Login"` (matching the scaffolded Identity Razor Pages route) and `AccessDeniedPath = "/Home/AccessDenied"` (a hand-written `HomeController` action). On startup it seeds the admin user/role and `ResumeSection` defaults non-destructively (see `DbInitializer` — both seeders only insert when nothing exists yet; neither deletes or overwrites existing rows).
- **`Data/AppDbContext.cs`** — `IdentityDbContext<ApplicationUser>` exposing `DbSet`s for `Projects`, `ResumeSections`, `BlogPosts`, `ContactMessages` (Identity tables come from the base class).
- **`Data/DbInitializer.cs`** — startup seeding logic (admin user/roles, resume section defaults). Runs unconditionally on every app start via the scope block in `Program.cs`.
- **`Models/`** — plain EF entities (`Project`, `BlogPost`, `ContactMessage`, `ResumeSection`) with DataAnnotations for validation, plus `ApplicationUser : IdentityUser` (adds `FirstName`/`LastName`).
- **`Controllers/`** — one controller per entity (`ProjectController`, `BlogPostController`, `ResumeController`, `ContactController`) plus `HomeController`. Pattern is consistent across CRUD controllers: `Index`/read actions are `[AllowAnonymous]`, write actions (`Create`/`Edit`/`Delete`) are `[Authorize(Roles = "Admin")]`. `ContactController` also sends an email via `IEmailSender` on submission.
- **`Services/`**
  - `AppClaimPrincipalFactory` — adds `FirstName` and role claims to the user's `ClaimsPrincipal` on sign-in.
  - `EmailSender` — SendGrid-backed `IEmailSender` implementation; reads the API key from `SendGrid:ApiKey` config and throws at construction if it's missing.
- **`Areas/Identity/Pages/`** — scaffolded ASP.NET Core Identity Razor Pages (login, register, 2FA, account management, etc.) — standard scaffolded output, not hand-rolled.
- **`Views/`** — Razor views grouped by controller (`Views/Project`, `Views/BlogPost`, `Views/Contact`, `Views/Home`), plus `Views/Shared/_Layout.cshtml` for the site chrome.

### Roles

Two roles exist: `Admin` and `User`. Admin is required for all content mutation (Projects, BlogPosts). The seeded admin account is `admin@example.com` (see `DbInitializer.SeedAdmin` — it only creates this user if it doesn't already exist, and skips creation entirely with a logged warning if `Seed:AdminPassword` is unset; manual changes to the account persist across restarts).

### Resolved issue: login redirect

Anonymous requests to `[Authorize]`-protected pages previously redirected to `/Account/Login`, which 404s — the actual route is `/Identity/Account/Login` (Razor Pages area). Fixed by explicitly setting `options.LoginPath = "/Identity/Account/Login"` in the `ConfigureApplicationCookie` call in `Program.cs`. Verified via Playwright: anonymous navigation to `/Project/Create` and `/BlogPost/Create` now redirects to `/Identity/Account/Login` and the login page renders correctly.

### Known issues (not yet fixed)

- A duplicate, incorrectly-pathed `<script>` tag in `Views/Shared/_Layout.cshtml` (`~/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js`, missing the `/dist/` segment) 404s on every page load. The correctly-pathed copy is loaded separately via `_ValidationScriptsPartial` on pages that need client-side validation (forms), so validation still functions — this is a harmless but noisy console error on every page. Found during Phase 1.5 browser QA; not yet fixed.
- `EmailSender` logs a warning and continues (does not crash the request) when SendGrid rejects a send (e.g. invalid/placeholder API key) — confirmed via the Contact form: the message still saves to `ContactMessages` and the user still reaches the Thank You page even though the email itself fails. Making this explicit and graceful by design is tracked as Phase 2, Task 4.
