# CodeFolio — Project Development Roadmap

> **Last Updated:** 2026-07-27  
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

## Phase 2 — Production Preparation

**Objective:** Make the application behave correctly and observably in a production environment before it touches a server.

**Status: Not started. Begins after Phase 1.5 is fully validated.**

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | 🔲 Add Serilog with rolling file sink | Replace default console-only logging; structured JSON output |
| 2 | 🔲 Add `/health` endpoint | `app.MapHealthChecks("/health")` — used by Nginx and uptime monitoring |
| 3 | 🔲 Add rate limiting to `ContactController` | ASP.NET Core built-in rate limiting; e.g. 5 submissions/minute per IP |
| 4 | 🔲 Make `EmailSender` degrade gracefully if API key is missing | Currently throws at construction, crashing the app on startup; should log a warning and no-op instead |
| 5 | 🔲 Lock `AllowedHosts` to production domain | Currently `"*"` in `appsettings.json` |
| 6 | 🔲 Create `appsettings.Production.json` template | No real secrets — documents required environment variable names |
| 7 | 🔲 Remove stale `using` in `EmailSender.cs` | `using Microsoft.VisualStudio.Web.CodeGenerators.Mvc...` — leftover from scaffolding, unused |
| 8 | 🔲 Remove `Npgsql.EntityFrameworkCore.PostgreSQL.Design` v1.1.0 | Version from 2016; superseded by the main Npgsql provider; can cause tooling confusion |
| 9 | 🔲 Publish production build and confirm output | `dotnet publish -c Release` — verify no dev-only assets in output |

---

## Phase 3 — DigitalOcean VPS Deployment

**Objective:** Deploy to a production VPS with HTTPS, a reverse proxy, persistent database storage, and a clean deployment workflow.

**Status: Not started. Begins after Phase 2 is complete.**

### Infrastructure Stack

```
[Internet :443/:80]
      │
   Nginx  (reverse proxy + TLS via Let's Encrypt / Certbot)
      │
   Kestrel  (ASP.NET Core, managed by systemd)
      │
   PostgreSQL  (Docker container on the same Droplet, named volume for persistence)
```

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | 🔲 Provision DigitalOcean Droplet | Ubuntu 24.04, $6–12/month, SSH key auth only, non-root deploy user |
| 2 | 🔲 Install .NET 9 runtime on Droplet | Runtime only, not SDK |
| 3 | 🔲 Install Docker + Docker Compose on Droplet | For PostgreSQL container |
| 4 | 🔲 Install Nginx on Droplet | Package manager install |
| 5 | 🔲 Create production `docker-compose.yml` | PostgreSQL with named volume; no exposed port to host network |
| 6 | 🔲 Run PostgreSQL container and apply migration | `dotnet ef database update` with production connection string |
| 7 | 🔲 Write `systemd` unit file for Kestrel | `ASPNETCORE_ENVIRONMENT=Production`; secrets as `Environment=` entries or loaded from `/etc/codefolio/.env` (chmod 600) |
| 8 | 🔲 Configure Nginx reverse proxy | `proxy_pass` to `127.0.0.1:5000`; `X-Forwarded-For` / `X-Forwarded-Proto` headers |
| 9 | 🔲 Obtain TLS certificate via Certbot | Let's Encrypt; auto-renewal via cron |
| 10 | 🔲 Configure UFW firewall | Allow only ports 22, 80, 443 |
| 11 | 🔲 Write and test deployment script | `dotnet publish` → `scp` artifact to Droplet → `systemctl restart codefolio` |
| 12 | 🔲 Verify full production smoke test | HTTPS, login, CRUD, contact form, health endpoint |
| 13 | 🔲 Document PostgreSQL backup strategy | `pg_dump` cron job; store to DigitalOcean Spaces or S3-compatible bucket |

---

## Phase 4 — Claude AI Assistant Integration

**Objective:** Integrate a Claude-powered portfolio assistant into the existing MVC application without modifying any existing controllers or views.

**Status: Not started. Begins after Phase 3 is stable in production.**

### Architecture

The AI layer is purely additive:

```
_Layout.cshtml  →  <partial name="_AiChatWidget" />
                        │
                   fetch POST /ai/chat
                        │
                   AiController  (new, standalone)
                        │
                   IAiService  (new interface)
                        │
                   AnthropicClient  (registered in Program.cs)
```

No existing controllers, views, or services are modified. If the AI endpoint fails, the rest of the portfolio is unaffected.

### Tasks

| # | Task | Notes |
|---|------|-------|
| 1 | 🔲 Add `Anthropic.SDK` NuGet package | Anthropic's official .NET SDK |
| 2 | 🔲 Register `AnthropicClient` in `Program.cs` | Reads API key from `Anthropic:ApiKey` environment config |
| 3 | 🔲 Create `IAiService` interface and `AnthropicAiService` implementation | Encapsulates SDK calls; injectable and mockable |
| 4 | 🔲 Create `AiController` with `POST /ai/chat` endpoint | Minimal API style; accepts `{message: string}`, returns `{reply: string}` |
| 5 | 🔲 Write system prompt grounding Claude in portfolio context | Name, skills, projects, career goals, boundaries (only answer portfolio-relevant questions) |
| 6 | 🔲 Add rate limiting to `/ai/chat` | 5–10 requests/minute per IP; stricter than contact form |
| 7 | 🔲 Log all AI requests | Message, response, token counts via Serilog — necessary for cost tracking |
| 8 | 🔲 Build `_AiChatWidget.cshtml` partial view | Floating chat button; panel with message thread; vanilla JS `fetch()` to `/ai/chat` |
| 9 | 🔲 Add `<partial name="_AiChatWidget" />` to `_Layout.cshtml` | Single line change; site-wide availability |
| 10 | 🔲 Set monthly spend cap in Anthropic dashboard | Do this before enabling in production |
| 11 | 🔲 Production smoke test of AI widget | End-to-end: widget loads, message sent, response received, rate limit enforced |

---

## Open Architectural Concerns (Carry-Forward from Initial Assessment)

These are known issues not yet addressed by any phase. They should be resolved before or during Phase 2.

| Issue | Severity | Phase to Address |
|-------|----------|-----------------|
| ~~Anonymous access to `[Authorize]` pages redirects to `/Account/Login`, which 404s~~ | ✅ Resolved 2026-07-27 | `LoginPath` configured in `Program.cs`; verified via Playwright |
| Duplicate/mis-pathed `jquery.validate.unobtrusive.min.js` script tag in `_Layout.cshtml` 404s on every page load (harmless — correct copy loads via `_ValidationScriptsPartial` on form pages) | 🟢 Low | Not yet assigned — found during Phase 1.5 browser QA |
| `EmailSender` throws at startup if `SendGrid:ApiKey` is absent entirely; when present but invalid, it logs a warning and the request still succeeds (confirmed gracefully non-fatal at the Contact form) | 🟠 High | Phase 2, Task 4 |
| Contact form has no rate limiting | 🟠 High | Phase 2, Task 3 |
| `ResumeContent` stored as raw HTML — XSS risk if multi-user editing ever added | 🟡 Medium | Awareness only for now |
| `Npgsql.EFCore.PostgreSQL.Design` v1.1.0 in csproj (2016 package, stale) | 🟡 Medium | Phase 2, Task 8 |
| `AllowedHosts: "*"` in `appsettings.json` | 🟡 Medium | Phase 2, Task 5 |
| No PostgreSQL backup strategy | 🟡 Medium | Phase 3, Task 13 |
| No structured logging | 🟡 Medium | Phase 2, Task 1 |
| No health check endpoint | 🟡 Medium | Phase 2, Task 2 |

---

## Recommended Next Action

**Phase 1 and Phase 1.5 are both fully complete and validated, including real browser-based QA.** No further work is required to close out these phases.

Awaiting explicit approval before Phase 2 — Production Preparation begins. See the Open Architectural Concerns table above for what Phase 2 should pick up first (`EmailSender` graceful-degradation, rate limiting, structured logging, health checks, etc.).

Daily local dev workflow going forward:

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
