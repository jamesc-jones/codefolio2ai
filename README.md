# CodeFolio

An ASP.NET Core 9 MVC portfolio application using Razor views, EF Core with PostgreSQL, and ASP.NET Core Identity. Live at **https://codefolio2ai.com**.

## About

CodeFolio is a full-stack portfolio platform built end-to-end — from a raw ASP.NET Core MVC scaffold through production hardening, containerized deployment, and AI integration. It demonstrates production software development practices across the full lifecycle: secure configuration management, Docker Compose orchestration, Nginx reverse proxy with TLS, and a Claude-powered AI assistant that answers visitor questions about the portfolio owner's experience and projects.

## AI Assistant — Phase 4

A Claude-powered chat assistant is live on every page of the portfolio. Visitors can ask natural-language questions about skills, projects, experience, and architecture decisions, and receive context-aware answers grounded in the portfolio's actual content.

### Architecture

```
Visitor (browser)
    │
Chat Widget  (_ChatWidget.cshtml + chat.js)
    │  vanilla JS fetch, IIFE-scoped, handles 429/503/network errors
    │
POST /api/ai/chat  (AiController — attribute-routed, [ApiController])
    │  Rate limited: 5 requests/minute/IP  →  HTTP 429 on limit
    │
IClaudeService / ClaudeService  (registered singleton)
    │  Graceful degradation: returns HTTP 503 if API key absent
    │
Dynamic System Prompt  (IMemoryCache — built from ResumeSections + Projects at startup)
    │
Anthropic.SDK  →  Anthropic Messages API (claude-sonnet-4-5)
    │
Response  (token usage logged via Serilog)
```

The AI layer is entirely additive. No existing controller, view, service, or database table was modified. If the AI endpoint fails, the portfolio continues working without interruption.

### Security

- **API key isolation:** `Anthropic__ApiKey` is injected via environment variable at container startup (`docker-compose.production.yml` + `.env.production` on the server). The key is never present in source code, Docker images, or response headers.
- **Rate limiting:** A dedicated `"ai-chat"` fixed-window policy (5 req/min/IP) is enforced server-side via ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` — the same infrastructure used for the contact form, extended with a second policy.
- **XSS prevention:** All AI response text is rendered via JavaScript's `textContent` (not `innerHTML`), preventing model-generated HTML from executing in the browser.
- **Graceful degradation:** `ClaudeService` logs a startup warning and returns HTTP 503 if the API key is absent or invalid — no exception reaches the host process.

### Key Technical Decisions

**Dynamic context over static prompts.** The system prompt is built from live database content (`ResumeSections`, `Projects`) at application startup and cached in `IMemoryCache`. This means the AI's knowledge of the portfolio stays in sync with the admin's content — no manual prompt edits needed when the portfolio is updated.

**Service abstraction.** The Anthropic SDK is encapsulated behind `IClaudeService`, keeping `AiController` decoupled from any specific AI provider. Swapping Claude for a different model requires changing only `ClaudeService`, not the controller or widget.

**Additive deployment.** Deployed via a container-only restart (`docker compose up -d --no-deps codefolio-web`) — Nginx and PostgreSQL kept running throughout.

**Token observability.** Input and output token counts are logged per request via Serilog, providing cost visibility without requiring external monitoring tooling.

---

## Development Setup

### Requirements

- .NET 9 SDK
- Docker Desktop

### Setup

1. Clone the repository.

2. Copy the environment template and fill in local values:

   ```
   cp .env.example .env
   ```

3. Configure `CodeFolio/appsettings.Development.json` (gitignored, create if it doesn't exist) with a connection string matching your `.env` Postgres credentials, plus a local `Seed:AdminPassword`:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Port=5433;Database=CodeFolioDB;Username=<POSTGRES_USER from .env>;Password=<POSTGRES_PASSWORD from .env>"
     },
     "Seed": {
       "AdminPassword": "<a local dev password>"
     },
     "SendGrid": {
       "ApiKey": "CONFIGURE_SENDGRID_LOCALLY",
       "FromEmail": "your-email@example.com",
       "FromName": "CodeFolio"
     },
     "Anthropic": {
       "ApiKey": "<your Anthropic API key>",
       "Model": "claude-sonnet-4-5",
       "MaxTokens": 1024
     }
   }
   ```

   Both `SendGrid:ApiKey` and `Anthropic:ApiKey` can remain as placeholders during local development — each service degrades gracefully if the key is absent (a startup warning is logged and the feature returns an appropriate error response without crashing the application).

4. Start PostgreSQL:

   ```
   docker compose up -d
   ```

5. Apply the database migration:

   ```
   dotnet ef database update --project CodeFolio
   ```

6. Run the application:

   ```
   dotnet run --project CodeFolio
   ```

The app is native (not containerized) — only PostgreSQL runs in Docker. The seeded admin account is `admin@example.com`, using whatever password you set in `Seed:AdminPassword`.

## Troubleshooting

- PostgreSQL runs on `localhost:5433` (not the default 5432), to avoid conflicting with any natively-installed local Postgres.
- The Docker volume (`codefolio_pgdata`) persists database data across `docker compose down` / `docker compose up -d` cycles.
- `docker compose down -v` deletes the local database volume — all local data is lost and the next `docker compose up -d` starts from an empty database (re-run the migration afterward).
