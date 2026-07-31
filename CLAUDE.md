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
- `CodeFolio/appsettings.Development.json` (gitignored) holds the ASP.NET Core app's own config (`ConnectionStrings:DefaultConnection`, `Seed:AdminPassword`, `SendGrid:ApiKey`/`FromEmail`/`FromName`) — these are separate files serving separate consumers and must be kept in sync manually (same Postgres password in both).
- The containerized PostgreSQL is exposed on host port **5433** (not 5432), to avoid colliding with any natively-installed local Postgres.

## Production Configuration

`CodeFolio/appsettings.Production.json` is tracked in git (unlike `appsettings.Development.json`) and contains **placeholders only** — a locked-down `AllowedHosts` value and placeholder keys for `ConnectionStrings`, `SendGrid`, and `Seed` documenting what must be supplied at deploy time. Real production secrets are never committed to `appsettings.json` or `appsettings.Production.json`.

Instead, production secrets come from **environment variables**, supplied via the VPS's environment (systemd unit `Environment=` entries or an `EnvironmentFile=`, per the Phase 3 deployment plan in `ROADMAP.md`). ASP.NET Core's configuration system maps a nested JSON key to an environment variable by replacing each `:` with a double underscore `__`. For example:

| JSON config key | Environment variable |
|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `SendGrid:ApiKey` | `SendGrid__ApiKey` |
| `SendGrid:FromEmail` | `SendGrid__FromEmail` |
| `Seed:AdminPassword` | `Seed__AdminPassword` |

Environment variables override matching keys from `appsettings.json`/`appsettings.Production.json` at runtime, so the checked-in placeholder files can stay generic while each deployment target supplies its own real values out-of-band.

**Always run `docker-compose.production.yml` commands with `--env-file .env.production` specified explicitly**, e.g.:

```
docker compose -f docker-compose.production.yml --env-file .env.production up -d
```

Docker Compose automatically loads a `.env` file from the current directory if one is present, even when it isn't passed via `--env-file`. Since the repo root already has a `.env` for local development (Postgres credentials, and now `ANTHROPIC_API_KEY` for local testing), running a production compose command without `--env-file .env.production` can silently fall back to those development values instead of failing loudly.

## Architecture

Standard ASP.NET Core MVC layout, all under `CodeFolio/`:

- **`Program.cs`** — composition root. Registers `AppDbContext` (Npgsql), ASP.NET Core Identity (`ApplicationUser` + `IdentityRole`, with `RequireConfirmedAccount = true`), a custom `IUserClaimsPrincipalFactory`, and `IEmailSender` (SendGrid). Cookie authentication is configured via `ConfigureApplicationCookie` with `LoginPath = "/Identity/Account/Login"` (matching the scaffolded Identity Razor Pages route) and `AccessDeniedPath = "/Home/AccessDenied"` (a hand-written `HomeController` action). On startup it seeds the admin user/role and `ResumeSection` defaults non-destructively (see `DbInitializer` — both seeders only insert when nothing exists yet; neither deletes or overwrites existing rows).
- **`Data/AppDbContext.cs`** — `IdentityDbContext<ApplicationUser>` exposing `DbSet`s for `Projects`, `ResumeSections`, `BlogPosts`, `ContactMessages` (Identity tables come from the base class).
- **`Data/DbInitializer.cs`** — startup seeding logic (admin user/roles, resume section defaults). Runs unconditionally on every app start via the scope block in `Program.cs`.
- **`Models/`** — plain EF entities (`Project`, `BlogPost`, `ContactMessage`, `ResumeSection`) with DataAnnotations for validation, plus `ApplicationUser : IdentityUser` (adds `FirstName`/`LastName`).
- **`Controllers/`** — one controller per entity (`ProjectController`, `BlogPostController`, `ResumeController`, `ContactController`) plus `HomeController`. Pattern is consistent across CRUD controllers: `Index`/read actions are `[AllowAnonymous]`, write actions (`Create`/`Edit`/`Delete`) are `[Authorize(Roles = "Admin")]`. `ContactController` also sends an email via `IEmailSender` on submission.
- **`Services/`**
  - `AppClaimPrincipalFactory` — adds `FirstName` and role claims to the user's `ClaimsPrincipal` on sign-in.
  - `EmailSender` — SendGrid-backed `IEmailSender` implementation; reads the API key and sender identity from `SendGrid:ApiKey`/`FromEmail`/`FromName` config. Degrades gracefully if the key is missing or a send fails (logs a warning and returns) rather than throwing — email failures never break the calling request.
- **`Areas/Identity/Pages/`** — scaffolded ASP.NET Core Identity Razor Pages (login, register, 2FA, account management, etc.) — standard scaffolded output, not hand-rolled.
- **`Views/`** — Razor views grouped by controller (`Views/Project`, `Views/BlogPost`, `Views/Contact`, `Views/Home`), plus `Views/Shared/_Layout.cshtml` for the site chrome.

### Roles

Two roles exist: `Admin` and `User`. Admin is required for all content mutation (Projects, BlogPosts). The seeded admin account is `admin@example.com` (see `DbInitializer.SeedAdmin` — it only creates this user if it doesn't already exist, and skips creation entirely with a logged warning if `Seed:AdminPassword` is unset; manual changes to the account persist across restarts).

### Resolved issue: login redirect

Anonymous requests to `[Authorize]`-protected pages previously redirected to `/Account/Login`, which 404s — the actual route is `/Identity/Account/Login` (Razor Pages area). Fixed by explicitly setting `options.LoginPath = "/Identity/Account/Login"` in the `ConfigureApplicationCookie` call in `Program.cs`. Verified via Playwright: anonymous navigation to `/Project/Create` and `/BlogPost/Create` now redirects to `/Identity/Account/Login` and the login page renders correctly.

### Known issues (not yet fixed)

- A duplicate, incorrectly-pathed `<script>` tag in `Views/Shared/_Layout.cshtml` (`~/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js`, missing the `/dist/` segment) 404s on every page load. The correctly-pathed copy is loaded separately via `_ValidationScriptsPartial` on pages that need client-side validation (forms), so validation still functions — this is a harmless but noisy console error on every page. Found during Phase 1.5 browser QA; not yet assigned to a phase.

## Production Engineering Experience

> *This section documents production-readiness work completed during Phase 2 for portfolio and interview reference.*

Phase 2 (commit `0b1eb67`, tag `phase-2-production-hardening`) brought CodeFolio from a working local application to a production-hardened platform. The work demonstrates experience beyond feature development:

**Structured Logging (Serilog):** Integrated Serilog with a bootstrap logger pattern that captures startup failures before the host is fully built. Configured console and rolling daily file sinks with a 14-day retention window. Applied environment-specific log level overrides — verbose in development, `Warning`-minimum in production to reduce disk usage.

**Health Monitoring:** Exposed a `/health` endpoint using ASP.NET Core's built-in health check infrastructure. The endpoint is anonymous (no authentication required) and returns a machine-readable status used by Nginx upstream health probes and external uptime monitors.

**Rate Limiting:** Applied ASP.NET Core's built-in fixed-window rate limiter to the contact form POST action — the application's primary abuse surface. Scoped precisely to the POST endpoint only (not page views or other routes), with HTTP 429 rejection and a user-facing message. Middleware ordering relative to authentication and routing was verified.

**Email Reliability:** Eliminated a class of startup failures — `EmailSender` previously threw `ArgumentNullException` at construction if `SendGrid:ApiKey` was absent, bringing down the entire application. Replaced with graceful degradation: the service logs a startup warning, marks itself as disabled, and allows all contact submissions to succeed (saving to the database) even when email delivery is unavailable. Network-level send failures are caught, logged, and do not propagate to the caller.

**Production Configuration Management:** Created `appsettings.Production.json` as a committed, secret-free template that documents the shape of production configuration. Real secrets are supplied at runtime via environment variables following ASP.NET Core's `__`-separator naming convention (`ConnectionStrings__DefaultConnection`, `SendGrid__ApiKey`, etc.), keeping them out of source control entirely.

**Release Validation:** Verified `dotnet publish -c Release` output for security (no `appsettings.Development.json` in output), confirmed `appsettings.Production.json` is present, and ran a full browser regression test post-publish covering authentication, CRUD workflows, and the contact form.

*Implemented structured logging, health monitoring, rate limiting, production configuration management, and release validation for an ASP.NET Core MVC portfolio platform — demonstrating experience in application reliability, operational observability, security-conscious configuration, and deployment readiness.*

### Phase 3 — Production Deployment ✅ COMPLETE

> *Completed July 29, 2026 — 3:13 PM. Git tag: `phase-3-production-deployment`.*

Phase 3 took CodeFolio live at **https://codefolio2ai.com** on a DigitalOcean VPS using a full Docker Compose stack. The application is stable in production. All smoke tests passed.

**Containerization:** Multi-stage Dockerfile — SDK image for `dotnet publish -c Release`, ASP.NET 9 runtime image for the final artifact. Non-root `codefolio` process user, deterministic layer caching via `COPY *.csproj` before full source copy. A `.dockerignore` excludes `bin/`/`obj/`, preventing stale Windows-path NuGet artifacts from breaking the Linux container build.

**Container Orchestration:** Three-service `docker-compose.production.yml` (Nginx + ASP.NET Core + PostgreSQL) on the private `codefolio_codefolio-net` Docker bridge network. Postgres has no host port — accessible only within the network. `depends_on: condition: service_healthy` ensures the app starts only after Postgres passes its healthcheck.

**Reverse Proxy + TLS:** Nginx terminates TLS using a Let's Encrypt certificate (Certbot standalone mode) for `codefolio2ai.com`/`www.codefolio2ai.com`, valid through 2026-10-26. TLSv1.2+, ECDHE cipher suite, HTTP → HTTPS redirect (`301`), security headers (`X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`) active. Auto-renewal via cron + Nginx reload hook; `certbot renew --dry-run` succeeded.

**Server Hardening:** UFW (ports 22/80/443 only), fail2ban SSH brute-force protection, non-root `deploy` user, SSH key auth only, root SSH login disabled.

**Database Migration:** `InitialCreate` applied via a disposable SDK container on the app's Docker network. Key lesson: a migration-only source bundle (`Migrations/`/`Data/`/`Models/`/`.csproj`) fails to compile — `CodeFolio.csproj` is a single executable project, so `Program.cs` and `Services/` must be included in the migration source.

**Secrets Management:** All production secrets in `/home/deploy/codefolio/.env.production` (`chmod 600`) on the server — never in source control. ASP.NET Core's `__`-separator env var naming maps Docker Compose passthrough directly to the config system.

**Production Verification:** Full smoke test passed live — HTTPS, HTTP→HTTPS redirect, homepage, Projects/Blog pages, contact form (submission + DB persistence), authentication and authorization, `/health` (returns `Healthy`). Both a container restart and a full `docker compose down → up -d` recreation verified with all 12 tables and seeded data intact.

**Known limitation:** SendGrid email delivery currently blocked by the SendGrid account's credit limit ("Maximum credits exceeded") — an external account-plan issue, not an application defect. Per Phase 2 hardening, contact submissions still validate and persist correctly regardless.

**Deferred to Phase 5:** deployment update/rollback workflow and PostgreSQL backup cron not yet executed.

*Full tutorial and task log: `PHASE_3_DEPLOYMENT.md`.*

### Phase 4 — AI Assistant Integration ✅ COMPLETE

> *Git tag: `phase-4-ai-assistant`. Deployed to production and independently verified live at https://codefolio2ai.com on 2026-07-30 — `/health` returns `Healthy`, `POST /api/ai/chat` returns a real grounded reply, and the chat widget works end-to-end in a live browser session.*

Phase 4 adds a Claude-powered AI assistant to the portfolio. The feature is entirely additive — no existing controller, view, service, or database schema was modified.

**AI Service Architecture:** Introduced `IClaudeService` / `ClaudeService` as a registered singleton, decoupling the Anthropic SDK from the controller layer. `ClaudeService` follows the same graceful-degradation pattern established in Phase 2 for `EmailSender` — if `Anthropic:ApiKey` is absent or invalid, the service marks itself as disabled at startup and returns HTTP 503 from the endpoint, with no exception propagating to the host.

**Dynamic System Prompt with Database Integration:** The system prompt sent to Claude is not a hardcoded string. It is constructed at startup by querying `ResumeSections` and `Projects` from the database, giving the assistant current, accurate knowledge of the portfolio's actual content. The constructed prompt is cached via `IMemoryCache`, eliminating repeated DB reads on every chat request.

**API Endpoint and Routing:** `AiController` uses `[ApiController]` + attribute routing (`POST /api/ai/chat`). Because the existing pipeline uses `MapControllerRoute` for conventional MVC routing, `app.MapControllers()` was added to register attribute-routed controllers — a minimal, non-breaking pipeline addition.

**Rate Limiting:** A second fixed-window policy (`"ai-chat"`, 5 req/min/IP) was added to the existing `AddRateLimiter` block in `Program.cs`. The middleware call (`UseRateLimiter()`) was already in the pipeline from Phase 2 and required no changes — demonstrating the value of Phase 2's foundation work.

**Chat Widget:** `_ChatWidget.cshtml` is a self-contained partial view (scoped CSS embedded, no external stylesheet) injected into `_Layout.cshtml` before `</body>`, making it available on every page. `wwwroot/js/chat.js` is an IIFE-scoped vanilla JavaScript fetch client with explicit handling for HTTP 429, 503, network failures, and a loading state. All AI response text is rendered via `textContent` (not `innerHTML`), preventing XSS from any model output.

**Secrets and Deployment:** `Anthropic__ApiKey` follows the same `__`-separator env var convention established in Phase 2 — added to `/home/deploy/codefolio/.env.production` on the server and passed through `docker-compose.production.yml`. Deployed following the same additive, container-only restart pattern used for prior config-only changes.

**Observability:** `ClaudeService` logs the input and output token count of every API response via Serilog, enabling cost visibility without any external billing dashboard monitoring.

*Full tutorial: `PHASE_4_AI_ASSISTANT.md`.*

### Phase 5 — Production Hardening ✅ COMPLETE

> *Completed 2026-07-30. Automated backups, uptime monitoring, disaster recovery validation, Nginx security hardening, and the GitHub Actions CI/CD pipeline all verified live at https://codefolio2ai.com. Not yet tagged.*

**Automated Backups:** A `pg_dump` + gzip backup script runs daily via cron on the production VPS (3 AM, 14-day retention), writing to `/opt/backups/codefolio` as `codefolio_YYYY_MM_DD_HHMMSS.sql.gz`. A restore was verified end-to-end against a temporary throwaway database on the same Postgres container — the live `codefolio` database and `pgdata` volume are never touched by the test.

**Monitoring:** UptimeRobot polls both the homepage and `/health` (keyword `Healthy`) every 5 minutes, with email alerting configured.

**Disaster Recovery:** `PHASE_5_PRODUCTION_HARDENING.md` documents three scenarios — application container failure, database container failure, and full VPS loss — each ending in a shared post-recovery verification checklist (health, auth, projects, blog, contact, AI assistant). Scenario A (application container failure and recovery via `docker compose up -d --no-deps codefolio-web`) has been performed live against production.

**Nginx Security Hardening:** `nginx/codefolio.conf` was updated with HSTS, Permissions-Policy, and a Content-Security-Policy built from the app's actual verified resource usage (`cdn.jsdelivr.net` for Bootstrap, `cdnjs.cloudflare.com` for Font Awesome, `'self'`-only images and connections) rather than a generic policy — the original draft in an earlier tutorial pass would have broken every icon on the site. Deployed to the live server; `curl -I https://codefolio2ai.com` confirms all headers present and the CSP matches exactly.

**CI/CD Pipeline:** `.github/workflows/deploy.yml` runs `test` (restores, builds, runs `CodeFolio.Tests`) → `build-and-push` (builds the image, pushes to `ghcr.io/jamesc-jones/codefolio`, using `docker/setup-buildx-action@v3` for GHA cache export) → `deploy` (SSHes to the VPS, tags the running image as `:previous`, `docker compose pull` + `up -d --no-deps codefolio-web`, verifies `/health`, auto-rolls-back on failure). Verified on a real GitHub Actions run against production: all three jobs succeeded (`test` 39s, `build-and-push` 3m 6s, `deploy` 23s), and `/health` plus all core routes were independently re-checked live immediately after.

**Follow-up (not blocking this completion):** domain email (SendGrid remains blocked by its account credit limit) is prepared but not yet executed — see `PHASE_5_MANUAL_PRODUCTION_EXECUTION.md` §5.6.

### Phase 6 — Production Refinement & Portfolio Optimization ⏳ IN PROGRESS

> *Code/config for the first two reliability fixes is complete and locally validated (`dotnet build`, `docker compose config`, `nginx -t`). Not yet exercised against the live VPS — see `PHASE_6_MANUAL_PRODUCTION_EXECUTION.md` for the remaining manual steps. SEO, testing, and analytics work has not started; Phase 6 is not complete.*

**✅ ASP.NET Core DataProtection key persistence:** Previously, nothing in `Program.cs` configured a persistent key ring, so every container restart or redeploy — including the CI/CD pipeline's own `up -d --no-deps codefolio-web` on every push to `main` — silently generated a fresh DataProtection key ring and invalidated every existing login cookie, forcing unplanned re-authentication with no visible error. `Program.cs` now calls `builder.Services.AddDataProtection().SetApplicationName("CodeFolio").PersistKeysToFileSystem(new DirectoryInfo("/app/keys"))`, gated behind `!builder.Environment.IsDevelopment()` so local `dotnet run` keeps the framework's default behavior unchanged. `docker-compose.production.yml` adds a `dataprotection-keys` named Docker volume mounted at `/app/keys` on `codefolio-web` — Docker-managed (not a host bind mount), never committed to source control, and not reachable from outside the container.

**✅ Zero-downtime Certbot webroot renewal:** The certificate was originally issued via `certbot certonly --standalone`, which binds directly to port 80 and therefore requires briefly stopping Nginx; the renewal cron inherited the same method, so every ~60-day renewal caused a brief outage. `nginx/codefolio.conf` already served `/.well-known/acme-challenge/` from `/var/www/certbot` since Phase 3 issuance, but it was unused because that path was a Docker-managed named volume with no predictable host-filesystem location for the host's certbot process to write into. `docker-compose.production.yml`'s `nginx` service now bind-mounts the **host** directory `/var/www/certbot` directly, giving the host's certbot process (running in webroot mode) and the Nginx container the exact same physical directory. Nginx never needs to stop for a renewal under this scheme.

**Why now:** both were flagged as known gaps in Phase 5's "Known Improvements" section — small, well-understood fixes to real reliability issues, prioritized ahead of SEO/analytics work so every deploy for the rest of Phase 6 is safer by default.

**What's still manual:** this development session has no SSH access to the production droplet. The code and compose-file changes are correct and locally validated, but the VPS's own separate copies of `docker-compose.production.yml` and `/etc/letsencrypt/renewal/codefolio2ai.com.conf` must be updated by hand over SSH, then verified with `certbot renew --dry-run` and a container-restart key-ring check. Full runbook: `PHASE_6_MANUAL_PRODUCTION_EXECUTION.md`.

**Deferred within Phase 6:** SEO, expanded automated test coverage, and analytics integration have not started.

*Full tutorials: `PHASE_5_PRODUCTION_HARDENING.md`, `PHASE_5_MANUAL_PRODUCTION_EXECUTION.md`.*
