# CodeFolio

An ASP.NET Core 9 MVC portfolio application using Razor views, EF Core with PostgreSQL, and ASP.NET Core Identity.

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
     }
   }
   ```

   `SendGrid:ApiKey` can remain as a placeholder during local development — the app degrades gracefully (contact form submissions are saved to the database, email delivery is skipped with a log warning).

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
