# CodeFolio — Project Development Roadmap

> **Last Updated:** 2026-07-30 — Phase 5 (Production Hardening) complete and verified live in production, including the CI/CD pipeline; domain email remains as optional follow-up  
> **Stack:** ASP.NET Core 9 MVC · Razor Views · EF Core · PostgreSQL · ASP.NET Core Identity · SendGrid  
> **Target:** DigitalOcean VPS · Docker Compose · Claude AI Assistant

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Complete — verified in source |
| ⏳ | In progress / blocked |
| 🔲 | Pending — not started |
| 🚫 | Blocked — cannot proceed without prerequisite |

---

## Phase 1 — Stabilize Backend

**Objective:** Make the codebase safe, correct, and survivable across restarts before any deployment work begins.

**Status: Complete. Runtime validation completed 2026-07-27 via the Phase 1.5 Docker environment.**

### Completed

| # | Task | Notes |
|---|------|-------|
| 1 | ✅ Remove hardcoded admin password from `DbInitializer.cs` | Was `"Pippen$33scottie"` — now reads from `IConfiguration["Seed:AdminPassword"]` |
| 2 | ✅ Move admin password loading to configuration | `DbInitializer.SeedAdmin` injects `IConfiguration` and logs a warning if key is absent |
| 3 | ✅ Remove destructive admin user recreation logic | Seeder now returns early if `admin@example.com` already exists — no delete/recreate |
| 4 | ✅ Make `SeedResumeSections` non-destructive | Seeder now returns early if any `ResumeSection` rows exist — no table wipe |
| 5 | ✅ Remove duplicate admin role/email block from `Program.cs` | Dead code block referencing `admin@yourdomain.com` removed; `Program.cs` startup block now only calls `SeedAdmin` + `SeedResumeSections` |
| 6 | ✅ Add `InitialCreate` EF Core migration | Migration file: `Migrations/20260727142948_InitialCreate.cs` — covers all Identity tables, `Projects`, `BlogPosts`, `ResumeSections`, `ContactMessages` |
| 7 | ✅ Review generated migration | Migration reviewed and confirmed correct |

### Runtime Validation (Completed via Phase 1.5)

| # | Task | Result |
|---|------|--------|
| 8 | ✅ Apply migration to a running PostgreSQL instance | Applied `InitialCreate` against the Dockerized Postgres container; all 11 app/Identity tables confirmed via `\dt` |
| 9 | ✅ Verify application startup connects to DB successfully | `dotnet run` connected, logged "Hosting environment: Development", no startup errors |
| 10 | ✅ Confirm `DbInitializer` non-destructive behavior at runtime | Verified: `AspNetUsers` and `ResumeSections` row counts identical (1 and 7) before and after an app restart; second-run logs show lookup-only queries, no inserts/deletes |
| 11 | ✅ Browser smoke test (login, CRUD, contact form) | Completed with real Playwright browser automation. Login as seeded admin succeeds (name/role/logout render correctly); `/Project/Create` and `/BlogPost/Create` load once authenticated; full Project and BlogPost CRUD (create/edit/delete) verified end-to-end with test data cleaned up afterward; Contact form validated (client-side required-field validation, successful submission, DB row persisted, graceful handling of a SendGrid send failure). See Phase 1.5 task 10 for full detail and caveats found along the way. |

---

## Phase 1.5 — Development Environment Containerization

**Objective:** Create a reproducible local development environment using Docker for PostgreSQL only. The ASP.NET Core application continues to run natively via `dotnet run`. This unblocks all Phase 1 runtime validation and lays the structural groundwork for Phase 3's Docker Compose production deployment.

**Status: Complete (2026-07-27). All tasks including the browser smoke test are done.**

### Scope Boundaries

**In scope:**
- Docker container running PostgreSQL with known credentials
- `docker-compose.yml` for local development (DB only)
- `.env` file pattern for local credential management
- EF Core migration applied against containerized DB
- Application startup and `DbInitializer` verified

**Out of scope (deferred to later phases):**
- Containerizing the ASP.NET Core application
- Any production Docker Compose configuration
- CI/CD pipelines
- Kubernetes
- Any Phase 2+ work

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | ✅ Create `docker-compose.yml` at repo root | PostgreSQL service only; host port **5433** (not 5432, to avoid clashing with the native local Postgres install); `postgres:17-alpine`, named volume, healthcheck |
| 2 | ✅ Create `.env.example` with documented variable names | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` |
| 3 | ✅ Create `.env` (gitignored) with real local values | Generated a strong random local password; never printed or committed |
| 4 | ✅ Update `appsettings.Development.json` to use Docker DB credentials | Points to `localhost:5433` with the containerized user/password |
| 5 | ✅ Verify `.env` and `appsettings.Development.json` are gitignored | Solution-root `.gitignore` created (didn't exist before); both files confirmed ignored via `git status --ignored` |
| 6 | ✅ Run `docker compose up -d` and confirm PostgreSQL is healthy | Confirmed healthy via `docker compose ps` |
| 7 | ✅ Run `dotnet ef database update` against containerized DB | Applied `InitialCreate`; all 11 tables verified |
| 8 | ✅ Run `dotnet run` and verify application starts cleanly | Started cleanly, no errors, admin + resume sections seeded on first run |
| 9 | ✅ Confirm `DbInitializer` non-destructive behavior | Restarted app; `AspNetUsers`/`ResumeSections` counts unchanged (1 / 7) |
| 10 | ✅ Run browser smoke test | Completed 2026-07-27 with real Playwright browser automation (see "Final Validation Results (2026-07-27)" below for full detail). |

Also removed during this phase: the pre-existing `CodeFolio/.env` file (obsolete — pointed at an external, unrelated database and used non-matching variable names; contents were not migrated).

### Final Validation Results (2026-07-27)

Performed with real Playwright browser automation, closing out the one open caveat from the initial Phase 1.5 pass.

- **Login redirect bug fixed.** `Program.cs` now sets `options.LoginPath = "/Identity/Account/Login"` in `ConfigureApplicationCookie`. Verified: anonymous navigation to `/Project/Create` and `/BlogPost/Create` redirects to `/Identity/Account/Login` (not the 404ing `/Account/Login`), and the login page renders fully.
- **Public pages** (`/`, `/Project`, `/BlogPost`, `/Resume`, `/Contact`, `/Identity/Account/Login`) all load with correct titles and no broken layout. Each page shows one recurring, pre-existing console 404 for a duplicate/mis-pathed script tag in `_Layout.cshtml` (`jquery.validate.unobtrusive.min.js`, missing `/dist/`) — harmless, since the correctly-pathed copy loads separately via `_ValidationScriptsPartial` on form pages. Not fixed (out of scope for this session — see CLAUDE.md Known Issues).
- **Auth/authorization:** logged in as seeded admin (`admin@example.com`); "Hello, Admin!", "Role: Admin", Logout button, and admin nav links all render. `/Project/Create` and `/BlogPost/Create` load correctly while authenticated.
- **Project CRUD:** created, edited, and deleted a test project via the UI; each step confirmed against the listing page. Test data removed.
- **BlogPost CRUD:** created, edited, and deleted a test blog post via the UI; each step confirmed. Test data removed.
- **Contact form:** empty submission is blocked client-side (no server round-trip — confirmed via server logs) with inline "required" messages. Valid submission POSTs successfully, persists a row to `ContactMessages`, and redirects to the Thank You page. SendGrid is not configured locally (placeholder API key) — `EmailSender` attempted the send, received an auth-rejection response from SendGrid, logged a warning, and did **not** crash the request — the contact flow degrades gracefully end-to-end. Email delivery itself requires a real `SendGrid:ApiKey` in production. Test row removed from `ContactMessages` after verification.
- **Database:** `docker compose ps` shows the Postgres container healthy; `dotnet ef migrations list` shows only `InitialCreate`, applied. Restarted the app after all QA — server logs show only `SELECT`/`EXISTS` seeding checks, no `INSERT`/`DELETE`; `AspNetUsers` (1) and `ResumeSections` (7) row counts unchanged before and after restart, confirming non-destructive seeding holds under real usage.

### Architectural Notes on Introducing Docker at This Stage

**No risk to application architecture.** The ASP.NET Core app remains unchanged — Docker is only managing the PostgreSQL container. `dotnet run` continues to work exactly as before. The only change is that the connection string in `appsettings.Development.json` points to `localhost:5432` backed by the container rather than a native install.

**`docker-compose.yml` at the repo root is the right location.** It will be extended in Phase 3 to include the app service and Nginx, so starting it there now avoids a future move.

**The `.env` pattern established here carries forward.** The same variable names used locally will map to environment variables in the Phase 3 `systemd` unit file or Docker Compose production secrets, keeping the credential surface consistent across environments.

**One deliberate limitation:** the `docker-compose.yml` created here should explicitly be named or commented as "development only" until Phase 3. The production compose file will differ (named volumes for persistence, no exposed port 5432 to the host, etc.).

---

## Phase 2 — Production Hardening

**Objective:** Make the application observable, resilient, and deployment-ready before it touches a production server.

**Status: Complete (2026-07-28). Git tag: `phase-2-production-hardening` → commit `0b1eb67`.**

### Completed

| # | Task | Notes |
|---|------|-------|
| 0 | ✅ Dependency and import cleanup | Removed `Npgsql.EntityFrameworkCore.PostgreSQL.Design` v1.1.0 (2016-era stale package); added `<PrivateAssets>all</PrivateAssets>` to `Microsoft.VisualStudio.Web.CodeGeneration.Design`; removed unused `using` directives from `EmailSender.cs` and `ContactController.cs`; `dotnet build` confirmed 0 errors, 0 warnings |
| 1 | ✅ Structured logging via Serilog | `Serilog.AspNetCore` + `Serilog.Sinks.File` installed; `UseSerilog()` added to host with bootstrap logger pattern (catches startup-time failures); `appsettings.json` `Serilog` section configures console + rolling daily file sink, 14-day retention; `appsettings.Production.json` overrides minimum level to `Warning`; `logs/` added to `.gitignore` |
| 2 | ✅ Health monitoring endpoint | `AddHealthChecks()` registered; `app.MapHealthChecks("/health")` mapped before Razor Pages and MVC routes; endpoint is anonymous (no auth required); verified `GET /health` returns HTTP 200 `Healthy` |
| 3 | ✅ Contact form rate limiting | ASP.NET Core built-in rate limiting (no extra package); fixed-window limiter `"contact-form"` policy: 5 requests/minute per IP, queue limit 0, HTTP 429 rejection; `UseRateLimiter()` placed after `UseRouting()` and before `UseAuthentication()`; `[EnableRateLimiting("contact-form")]` applied only to the POST action of `ContactController`; 6th submission within window confirmed to return 429 |
| 4 | ✅ EmailSender reliability hardening | Constructor no longer throws if `SendGrid:ApiKey` is null/missing — logs a `Warning` and marks email as disabled; `SendEmailAsync` returns early with a log warning when key is absent; `catch` block logs error and returns instead of rethrowing, so network-level send failures cannot crash the calling request; sender identity (`FromEmail`, `FromName`) moved from hardcoded values to `SendGrid:FromEmail`/`SendGrid:FromName` config keys |
| 5 | ✅ Production configuration template | `CodeFolio/appsettings.Production.json` created and committed (safe — contains placeholder/empty values only); `AllowedHosts` set to production domain placeholder; `appsettings.*.json` gitignore exception added (`!appsettings.Production.json`) so the template is tracked; Production Serilog override included |
| 6 | ✅ Environment variable and secrets documentation | `CLAUDE.md` updated with production secrets strategy and `__`-separator env var naming table; confirmed no real secrets in any tracked file via `git ls-files` audit |
| 7 | ✅ Release build verification | `dotnet publish -c Release` succeeds cleanly; `appsettings.Development.json` confirmed absent from output; `appsettings.Production.json` confirmed present; `publish-output/` added to root `.gitignore`; browser regression test passed: authentication, Project CRUD, BlogPost CRUD, Contact form, `/health` endpoint all verified post-publish |

---

## Phase 3 — DigitalOcean VPS Deployment

**Objective:** Deploy to a production VPS with HTTPS, a reverse proxy, persistent database storage, and a clean deployment workflow.

**Status: ✅ COMPLETE — July 29, 2026. Live at https://codefolio2ai.com. Git tag: `phase-3-production-deployment`. Tasks 1–13 verified in production; Tasks 14–15 deferred to Phase 5 — Production Operations.**

**Full tutorial:** `PHASE_3_DEPLOYMENT.md` at the solution root.

### Architecture Decision

The original plan had the ASP.NET Core app running natively via `systemd` + Kestrel. **Phase 3 uses full Docker Compose instead** — all three components (Nginx, the ASP.NET Core app, PostgreSQL) run as containers. See `PHASE_3_DEPLOYMENT.md` for rationale.

### Infrastructure Stack

```
[Internet :443/:80]
      │
   Nginx  (Docker container — TLS termination + reverse proxy)
      │
   codefolio-web  (Docker container — ASP.NET Core 9 / Kestrel, port 8080 internal)
      │
   postgres  (Docker container — no host port exposed, named volume for persistence)

All three containers on private bridge network: codefolio-net
Orchestrated by: docker-compose.production.yml
```

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | ✅ Add `UseForwardedHeaders` to `Program.cs` | Required so Kestrel sees real client IP from behind Nginx; uses `Microsoft.AspNetCore.HttpOverrides` (no new package) |
| 2 | ✅ Create `CodeFolio/Dockerfile` | Multi-stage build: SDK image for publish, ASP.NET runtime image for final; non-root user; EXPOSE 8080. Required adding `.dockerignore` (excluding `bin/`/`obj/`) after a local build failure caused by stale Windows-path NuGet artifacts |
| 3 | ✅ Create `nginx/codefolio.conf` | HTTP-only first; HTTPS `server` block added after cert issued in Task 12 |
| 4 | ✅ Create `docker-compose.production.yml` | nginx + codefolio-web + postgres; postgres has no host port; secrets from `.env.production` |
| 5 | ✅ Update `.gitignore` for `.env.production`; commit Dockerfile and compose files | `.env.production` must never be committed |
| 6 | ✅ Provision DigitalOcean Droplet | `codefolio2ai-prod`, Ubuntu 24.04 LTS, Toronto (TOR1), 1 vCPU/1GB RAM |
| 7 | ✅ Initial server hardening | Non-root `deploy` user; root SSH disabled (verified `Permission denied`); UFW active (22/80/443); fail2ban installed and running |
| 8 | ✅ Install Docker + Docker Compose on Droplet | Docker 29.6.2, Compose v5.3.1, verified via `docker run hello-world` |
| 9 | ✅ Configure DNS | `codefolio2ai.com` and `www.codefolio2ai.com` both resolve to the Droplet IP; verified via `dig` |
| 10 | ✅ Deploy the application (HTTP first): build image, transfer to Droplet, create `.env.production`, start the stack | `docker save` + `scp`; copied `docker-compose.production.yml` + `nginx/codefolio.conf`; `.env.production` created directly on the server (chmod 600); stack started; `/health` initially failed as expected (no schema yet) until Task 11 |
| 11 | ✅ Apply EF Core migrations against production Postgres | Ran via temporary SDK container on `codefolio_codefolio-net`. First two attempts failed (missing NuGet restore, then missing `Program.cs`/`Services/` in the migration source bundle — `CodeFolio.csproj` is a single executable project, not a class library); corrected bundle applied `InitialCreate` successfully; admin user + 7 `ResumeSection` rows seeded |
| 12 | ✅ Issue TLS certificate via Certbot + update Nginx config for HTTPS | Real Let's Encrypt cert issued for both domains (valid through 2026-10-26); HTTPS server block deployed with strong TLS settings + security headers; renewal cron configured; `certbot renew --dry-run` succeeded |
| 13 | ✅ Full production smoke test | Homepage, Projects, Blog, Contact form (real submission + DB persistence verified), authentication + authorization redirects, `/health`, container restart persistence, and full `down`/`up -d` recreation (12 tables intact) — all verified directly against https://codefolio2ai.com |
| 14 | ⏳ Document and test deployment update workflow | Not yet exercised — deferred to Phase 5 |
| 15 | ⏳ Set up PostgreSQL backup | `pg_dump` cron documented in `PHASE_3_DEPLOYMENT.md` Task 15 — deferred to Phase 5 |

---

## Phase 4 — AI Assistant Integration

**Objective:** Add a Claude-powered AI assistant to the portfolio.

**Status: ✅ COMPLETE — deployed and independently verified live in production on 2026-07-30.**  
**Full tutorial:** `PHASE_4_AI_ASSISTANT.md` at the solution root.

### Architecture

The AI layer is purely additive — no existing controllers, views, or services were modified:

```
User (browser)
 │
_ChatWidget.cshtml  (partial view, injected in _Layout.cshtml — site-wide)
 │  vanilla JS fetch(), in-memory message history, loading state, error handling
 │
POST /api/ai/chat  (AiController — attribute-routed, separate from MVC routes)
 │  [EnableRateLimiting("ai-chat")]  5 req/min/IP  →  HTTP 429 on limit
 │
IClaudeService / ClaudeService  (singleton, graceful degradation if key absent)
 │  system prompt built dynamically from ResumeSections + Projects (IMemoryCache)
 │
Anthropic.SDK  →  Anthropic Messages API  (claude-sonnet-4-5)
 │
Response  (token usage logged via Serilog)
```

No existing controller, view, or service was modified. If the AI endpoint fails, the portfolio continues working without interruption.

### Key Components

| Component | Notes |
|---|---|
| `Anthropic.SDK` NuGet | Anthropic's official .NET SDK |
| `IClaudeService` / `ClaudeService` | Singleton service abstraction; gracefully disabled if `Anthropic:ApiKey` absent |
| `AiController` (`POST /api/ai/chat`) | `[ApiController]` + attribute routing; `app.MapControllers()` added to pipeline |
| Dynamic system prompt | Built from live `ResumeSections` + `Projects` DB data; cached via `IMemoryCache` |
| `"ai-chat"` rate limit policy | Added to existing `AddRateLimiter` block — separate from `"contact-form"` |
| `_ChatWidget.cshtml` | Self-contained partial with embedded CSS; `textContent` rendering (XSS-safe) |
| `wwwroot/js/chat.js` | Fetch, IIFE-scoped, handles 429/503/network errors explicitly |
| Serilog token logging | Input/output token counts logged per request for cost visibility |
| `Anthropic__ApiKey` env var | Injected via `.env.production` + Docker Compose — never in source control |

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | ✅ Install `Anthropic.SDK` NuGet package | `dotnet add package Anthropic.SDK` |
| 2 | ✅ Configure API key | `appsettings.json` placeholder; real key via `Anthropic__ApiKey` env var on server |
| 3 | ✅ Create `IClaudeService` interface | `AskAsync(string, CancellationToken)` + `IsConfigured` — decouples controller from SDK |
| 4 | ✅ Create `ClaudeService` implementation | Singleton; graceful degradation; Serilog token logging; catch-and-return error handling |
| 5 | ✅ Build dynamic system prompt | Reads `ResumeSections` + `Projects` from DB at startup; cached via `IMemoryCache` |
| 6 | ✅ Register service in `Program.cs` | `AddSingleton<IClaudeService, ClaudeService>()` + `AddMemoryCache()` |
| 7 | ✅ Add `"ai-chat"` rate limiter policy | Fixed-window, 5 req/min/IP, added to existing `AddRateLimiter` block |
| 8 | ✅ Create `AiController` | `POST /api/ai/chat`; `[ApiController]`; `app.MapControllers()` added to pipeline |
| 9 | ✅ Build `_ChatWidget.cshtml` + `chat.js` | Floating panel, loading/error states, XSS-safe `textContent` rendering |
| 10 | ✅ Inject into `_Layout.cshtml` | `<partial name="_ChatWidget" />` + `chat.js` before `</body>` — site-wide |
| 11 | ✅ Deploy to production and smoke test | Container-only restart (`--no-deps codefolio-web`); verified live 2026-07-30 |

### Production Validation Results (2026-07-30)

| Test | Result |
|---|---|
| `GET https://codefolio2ai.com/health` returns `Healthy` | ✅ Pass — verified live |
| `POST https://codefolio2ai.com/api/ai/chat` returns a coherent, grounded response | ✅ Pass — verified live (200 OK, ~4.4s latency) |
| `Anthropic__ApiKey` absent from source, logs, and response headers | ✅ Pass |
| Chat widget renders and completes a full round-trip in a live browser session | ✅ Pass — verified via Playwright against the live domain |
| No new browser console errors introduced (only the pre-existing, documented `jquery.validate.unobtrusive` 404 remains) | ✅ Pass |

Rate-limit behavior (5 req/min/IP) and the earlier local validation pass (empty-message 400, concurrent 429s, Serilog token logging, `ClaudeService configured: True` at startup) were already confirmed during local testing prior to deployment.

---

## Phase 5 — Production Operations

**Objective:** Operate CodeFolio as a production-grade system with automated backups, monitoring, reliable deployment, and hardened security posture.

**Status: ✅ COMPLETE — 2026-07-30. Automated backups, uptime monitoring, disaster recovery validation, Nginx security hardening, and the GitHub Actions CI/CD pipeline (build → test → push to GHCR → SSH deploy → health verification, with automatic rollback) are all live and verified in production. Domain email (Task 7) remains as optional, non-blocking follow-up — see "Remaining Follow-Up" below. Full tutorial: `PHASE_5_PRODUCTION_HARDENING.md`. Manual execution runbook: `PHASE_5_MANUAL_PRODUCTION_EXECUTION.md`. Includes deferred tasks from Phase 3 (Tasks 14–15).**

### Scope

| Area | Work |
|---|---|
| **PostgreSQL Backup** | `pg_dump` cron (3 AM daily, 14-day local retention); off-site to DigitalOcean Spaces; restore procedure tested against scratch DB (carried from Phase 3, Task 15) |
| **Deployment Update Workflow** | Exercise and document the `docker build → scp → docker load → compose up --no-deps` cycle with a real code update; verify rollback via image re-tag (carried from Phase 3, Task 14) |
| **Monitoring & Alerts** | Health endpoint polling; uptime alert (e.g., UptimeRobot free tier or DO Monitoring on `/health`); Serilog error-level alert integration |
| **Disaster Recovery** | Documented step-by-step procedure: app container failure, database container failure, Droplet replacement/DNS cutover; target RTO < 2 hours |
| **CI/CD Pipeline** | GitHub Actions: build → test → publish → Docker image push → deploy-on-push (or manual approval gate) |
| **Security Headers Hardening** | Enable HSTS (`Strict-Transport-Security`) in Nginx after HTTPS is stable; validate via securityheaders.com; consider Content-Security-Policy |
| **Domain Email Setup** | Configure SPF, DKIM, DMARC for the production sending domain; verify SendGrid sender authentication; resolve current "Maximum credits exceeded" account limit |

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | ✅ Backup script installed on VPS, cron scheduled, restore verified | `PHASE_5_PRODUCTION_HARDENING.md` 5.1 / `PHASE_5_MANUAL_PRODUCTION_EXECUTION.md` §5.1 |
| 2 | ✅ Deployment update workflow tested end-to-end | Verified live via the GitHub Actions CI/CD pipeline itself — see Task 5 |
| 3 | ✅ Uptime monitoring configured on `/health` and homepage | UptimeRobot — see Production Deployment Summary below |
| 4 | ✅ Document disaster recovery procedure | `PHASE_5_PRODUCTION_HARDENING.md` 5.3 — app container failure, DB container failure, and full VPS loss, each with a post-recovery verification checklist; Scenario A restart recovery verified live — see Production Validation Results below |
| 5 | ✅ GitHub Actions CI/CD Deployment | `test` → `build-and-push` (GHCR) → `deploy` (SSH, health-gated, auto-rollback) — all three jobs verified green on a real run against production; see CI/CD Pipeline Verification below |
| 6 | ✅ HSTS + CSP + Permissions-Policy applied to production Nginx | Verified live via `curl -I https://codefolio2ai.com` — see Production Validation Results below |
| 7 | 🔲 Resolve domain email / SendGrid account | Follow-up — instructions ready (5.6); requires an email provider account and live DNS changes |

### Production Deployment Summary

- **VPS:** DigitalOcean Droplet, Ubuntu 24.04 LTS
- **Deployment:** Docker Compose (`docker-compose.production.yml`)
- **Services:**
  - Nginx reverse proxy (`nginx:alpine`)
  - ASP.NET Core app (`codefolio:latest`)
  - PostgreSQL 17 (`postgres:17-alpine`)
- **HTTPS:**
  - Let's Encrypt certificate (valid through 2026-10-26)
  - HTTP → HTTPS redirect (301)
  - Security headers: HSTS, Content-Security-Policy, Permissions-Policy, X-Frame-Options, X-Content-Type-Options, Referrer-Policy
- **Environment configuration:** `.env.production` supplies all runtime secrets to `docker compose`, following the `__`-separator convention (`ConnectionStrings__DefaultConnection`, `SendGrid__ApiKey`, `Anthropic__ApiKey`, etc.) — no secrets in source control
- **Monitoring:** UptimeRobot — website monitor (`https://www.codefolio2ai.com`) and health endpoint monitor (`/health`, keyword `Healthy`), both on 5-minute polling
- **Backup:** PostgreSQL `pg_dump` + gzip backup script installed on the VPS, scheduled via cron (3 AM daily, 14-day retention), with a restore verified against a temporary database

### CI/CD Pipeline Verification (2026-07-30)

`.github/workflows/deploy.yml` was verified end-to-end on a real, public GitHub Actions run against production (workflow run #5, re-run after adding `VPS_HOST`/`VPS_SSH_KEY` secrets and configuring GHCR auth on the VPS):

- **GitHub Actions automated deployment:** `test` → `build-and-push` → `deploy` all completed successfully on push to `main` (`test`: 39s, `build-and-push`: 3m 6s, `deploy`: 23s) — no manual step required in between
- **GHCR container registry integration:** the image built and pushed to `ghcr.io/jamesc-jones/codefolio:latest` without error, including the Buildx cache export fix (`docker/setup-buildx-action@v3`)
- **SSH deployment automation:** the `deploy` job authenticated to the VPS via `appleboy/ssh-action` using the `VPS_HOST`/`VPS_SSH_KEY` secrets and ran its deployment script successfully
- **Production container recreation:** `docker compose pull` + `up -d --no-deps codefolio-web` recreated only the app container — Nginx and Postgres were not restarted
- **Automated health verification:** the deploy script's own `curl -sf https://codefolio2ai.com/health` gate is what determines job success or failure (`exit 1` unless the response is exactly `Healthy`) — a "succeeded" job status is only possible if that check passed
- **Rollback capability:** the script tags the previously-running image as `:previous` before deploying, and automatically re-tags and redeploys it if the post-deploy health check fails

Independently re-verified outside the CI log itself: `curl -s https://codefolio2ai.com/health` → `Healthy` (200), and `/`, `/Project`, `/BlogPost`, `/Contact`, `/Resume` all return 200, all checked fresh after this deployment.

### Production Validation Results (2026-07-30)

| Test | Result | Verified by |
|---|---|---|
| Homepage returns 200 OK over HTTPS | ✅ Pass | Directly re-verified via `curl` |
| `/Project` returns 200 | ✅ Pass | Directly re-verified via `curl` |
| `/BlogPost` returns 200 | ✅ Pass | Directly re-verified via `curl` |
| `/Contact` returns 200 | ✅ Pass | Directly re-verified via `curl` |
| `/Resume` returns 200 | ✅ Pass | Directly re-verified via `curl` |
| `/health` returns `Healthy` | ✅ Pass | Directly re-verified via `curl` |
| Security headers (HSTS, CSP, Permissions-Policy, X-Frame-Options, X-Content-Type-Options) present on live responses | ✅ Pass | Directly re-verified via `curl -I` — CSP matches the codebase-derived policy exactly |
| Contact form: submission → 302 redirect → ThankYou page | ✅ Pass | Directly re-submitted live via Playwright — landed on `/Contact/ThankYou` |
| Admin authentication (login succeeds) | ✅ Pass | Confirmed |
| Restart recovery (`docker compose restart` / container recreation) | ✅ Pass | Confirmed |
| UptimeRobot monitors active and reporting Up | ✅ Pass | Confirmed |
| Backup + restore test completed on VPS | ✅ Pass | Confirmed |

### Known Improvements (Post-Phase 5)

- **Persist ASP.NET Core DataProtection keys.** No key storage volume or config exists today (verified — nothing in `Program.cs` or `docker-compose.production.yml` configures a persistent key ring). Every container restart or redeploy currently generates a new key ring, silently invalidating all existing login cookies and forcing re-authentication. Low urgency (a resilient, low-friction failure mode) but worth fixing with a mounted volume + `PersistKeysToFileSystem`.
- **Fix Certbot renewal to use the webroot method.** The certificate was originally issued via `certbot certonly --standalone`, which requires briefly stopping Nginx to rebind port 80. The renewal cron (`certbot renew --quiet`) inherits that same method today, meaning each ~60-day renewal causes a brief outage unless manually mitigated. The webroot path (`/var/www/certbot`) and the matching Nginx `location /.well-known/acme-challenge/` block already exist and are unused — switching renewal to `--webroot` would eliminate the renewal-time downtime entirely.
- **Optional: SSL certificate expiry monitoring beyond UptimeRobot's free tier**, or an alternative dedicated cert-expiry checker, for earlier warning ahead of the existing 30-day Let's Encrypt auto-renewal window.

### Remaining Follow-Up (optional, not blocking Phase 5 completion)

- **Domain email (Task 7):** `contact@codefolio2ai.com` is not yet live; SendGrid remains blocked by its account credit limit. See `PHASE_5_MANUAL_PRODUCTION_EXECUTION.md` §5.6.

---

## Phase 6 — Production Refinement & Portfolio Optimization ✅ COMPLETE

**Status: Complete, verified live on production VPS on 2026-07-31.** The two highest-value production reliability fixes identified in Phase 5's "Known Improvements" are implemented and verified directly against the droplet. SEO, expanded automated test coverage, and analytics are optional future enhancements — not required for project completion — and remain unstarted by choice.

| # | Task | Status | Notes |
|---|------|--------|-------|
| 1 | ✅ Persist ASP.NET Core DataProtection keys | Verified live on production VPS | `Program.cs` calls `AddDataProtection().SetApplicationName("CodeFolio").PersistKeysToFileSystem(new DirectoryInfo("/app/keys"))`, gated to non-Development so local dev behavior is unchanged. `docker-compose.production.yml` adds a `dataprotection-keys` named volume mounted at `/app/keys` on `codefolio-web`. Verified via a full `docker compose down` / `up -d` container recreation on the VPS: the same key file (`key-c7138ba2-5d83-4b8c-84e2-dd68f6f83f5f.xml`) was present both before and after recreation. |
| 2 | ✅ Convert Certbot renewal from standalone to webroot mode | Verified live on production VPS | `docker-compose.production.yml`'s `nginx` service bind-mounts the host directory `/var/www/certbot` directly; `/etc/letsencrypt/renewal/codefolio2ai.com.conf` on the VPS updated to `authenticator = webroot` / `webroot_path = /var/www/certbot`. Verified via `docker exec codefolio_nginx nginx -t` (syntax ok), a manual ACME test file served successfully at `http://codefolio2ai.com/.well-known/acme-challenge/test.txt`, and `sudo certbot renew --dry-run` reporting "Congratulations, all simulated renewals succeeded" — with no Nginx stop/restart required. |

**Why these were prioritized:** both were flagged in Phase 5's "Known Improvements" as low-effort, real reliability gaps rather than new features — one causes silent forced re-authentication on every deploy, the other causes brief downtime on every ~60-day certificate renewal. Fixing both before starting SEO/analytics work means every subsequent deploy this phase is safer by default.

**How they improve production reliability:**
- **DataProtection persistence** — previously every container restart or redeploy (including the CI/CD pipeline's own `up -d --no-deps codefolio-web` on every push to `main`) generated a brand-new DataProtection key ring, silently invalidating every existing login cookie. Admins and any authenticated user would be logged out on every deploy with no error surfaced. Persisting the key ring to a volume means auth cookies now survive restarts and redeploys.
- **Webroot Certbot renewal** — the certificate was originally issued via `certbot certonly --standalone`, which requires briefly stopping Nginx to rebind port 80; the renewal cron inherited the same method, meaning every ~60-day renewal caused a brief production outage. Webroot mode lets Certbot satisfy the ACME HTTP challenge through a file Nginx serves while remaining up the entire time — renewal becomes a zero-downtime event.

**Verification notes (2026-07-31, performed on the production VPS):**
- DataProtection key persistence verified through a full `docker compose down` → `up -d` container recreation test (all three containers — `codefolio_nginx`, `codefolio_web`, `codefolio_postgres_prod` — cleanly removed and restarted)
- Docker volume persistence confirmed — the DataProtection key file survived container recreation with correct ownership/permissions (`-rw------- codefolio codefolio`)
- Nginx configuration validated via `docker exec codefolio_nginx nginx -t` — syntax ok
- ACME challenge path validated end-to-end — a test file written to the host webroot was served correctly over HTTP at `/.well-known/acme-challenge/test.txt`
- Certbot webroot renewal dry run succeeded — `sudo certbot renew --dry-run` reported all simulated renewals succeeded, with Nginx remaining up throughout

Full step-by-step runbook and command reference: `PHASE_6_MANUAL_PRODUCTION_EXECUTION.md`.

**Optional future enhancements (bonus, not blocking completion):** SEO improvements, automated test coverage expansion, and analytics integration. These may be picked up later at the project owner's discretion but are not required — the project is considered complete as of this phase.

---

## Open Architectural Concerns

| Issue | Severity | Status |
|-------|----------|--------|
| ~~Anonymous access to `[Authorize]` pages redirects to `/Account/Login`, which 404s~~ | ✅ Resolved 2026-07-27 | `LoginPath` configured in `Program.cs`; verified via Playwright |
| ~~`EmailSender` throws at startup if `SendGrid:ApiKey` is absent~~ | ✅ Resolved 2026-07-28 | Phase 2, Task 4 — graceful degradation implemented |
| ~~Contact form has no rate limiting~~ | ✅ Resolved 2026-07-28 | Phase 2, Task 3 — fixed-window 5 req/min limiter applied |
| ~~`Npgsql.EFCore.PostgreSQL.Design` v1.1.0 in csproj (2016 package, stale)~~ | ✅ Resolved 2026-07-28 | Phase 2, Task 0 — package removed |
| ~~`AllowedHosts: "*"` in `appsettings.json`~~ | ✅ Resolved 2026-07-28 | Phase 2, Task 5 — production domain placeholder set in `appsettings.Production.json` |
| ~~No structured logging~~ | ✅ Resolved 2026-07-28 | Phase 2, Task 1 — Serilog with rolling file sink |
| ~~No health check endpoint~~ | ✅ Resolved 2026-07-28 | Phase 2, Task 2 — `/health` returns 200 Healthy |
| Duplicate/mis-pathed `jquery.validate.unobtrusive.min.js` script tag in `_Layout.cshtml` 404s on every page load (harmless — correct copy loads via `_ValidationScriptsPartial` on form pages) | 🟢 Low | Not yet assigned — found during Phase 1.5 browser QA |
| `ResumeContent` stored as raw HTML — XSS risk if multi-user editing ever added | 🟡 Medium | Awareness only — not a current risk given single-admin design |
| No PostgreSQL backup strategy | 🟡 Medium | Phase 5, Task 1 — deferred from Phase 3 Task 15 |

---

## Current Status

**Phases 1 through 6 are complete and live in production, including the AI assistant, automated backups, monitoring, Nginx security hardening, persistent DataProtection keys, and zero-downtime certificate renewal. The project is complete as-is; SEO, expanded automated testing, and analytics remain available as optional future enhancements.**

| Phase | Git Reference | Status |
|-------|---------|--------|
| Phase 1 — Backend Stabilization | *(part of Phase 1.5 commit)* | ✅ Complete |
| Phase 1.5 — Dev Environment Containerization | commit `b65e015` | ✅ Complete |
| Phase 2 — Production Hardening | tag `phase-2-production-hardening` → commit `0b1eb67` | ✅ Complete |
| Phase 3 — DigitalOcean VPS Deployment | tag `phase-3-production-deployment` | ✅ Complete — live at https://codefolio2ai.com |
| Phase 4 — AI Assistant Integration | tag `phase-4-ai-assistant` | ✅ Complete — verified live at https://codefolio2ai.com on 2026-07-30 |
| Phase 5 — Production Operations | *(not yet tagged)* | ✅ Complete — backups, monitoring, disaster recovery, Nginx security headers, and the GitHub Actions CI/CD pipeline all verified live on 2026-07-30; domain email remains as optional follow-up (see Phase 5 section above) |
| Phase 6 — Production Refinement & Portfolio Optimization | *(not yet tagged)* | ✅ Complete — DataProtection key persistence and webroot Certbot renewal verified live on the production VPS on 2026-07-31 (see `PHASE_6_MANUAL_PRODUCTION_EXECUTION.md`); SEO, testing, and analytics remain as optional future enhancements |

**CodeFolio is live in production at https://codefolio2ai.com**, including the Claude-powered AI assistant, automated daily database backups, UptimeRobot monitoring, hardened Nginx security headers (HSTS, CSP, Permissions-Policy), and a fully automated GitHub Actions CI/CD pipeline (push to `main` → test → build → push to GHCR → SSH deploy → health-verified, with automatic rollback). Known non-blocking limitation: SendGrid email delivery blocked by account credit limit (contact form DB persistence is unaffected) — domain email is prepared but not yet cut over (see Phase 5's "Remaining Follow-Up").

Daily local dev workflow:

```bash
# Start containerized PostgreSQL (if not already running)
docker compose up -d

# Confirm it's healthy
docker compose ps

# Apply any new migrations
dotnet ef database update --project CodeFolio

# Start the app
dotnet run --project CodeFolio
```

---

*This roadmap is a living document. Update it as phases are completed or scope changes.*
