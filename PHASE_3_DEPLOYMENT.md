# Phase 3 — DigitalOcean VPS Deployment Tutorial

> **Status:** ✅ COMPLETE — Executed and verified in production. Tasks 1–13 complete. Tasks 14–15 deferred to Phase 5. Live at https://codefolio2ai.com  
> **Completed:** July 29, 2026 — 3:13 PM  
> **Created:** 2026-07-28
> **Prerequisites:** Phase 2 complete, git tag `phase-2-production-hardening` → commit `0b1eb67`

---

## Architecture Decision: Full Docker Compose vs. Kestrel + systemd

The ROADMAP's original Phase 3 design ran the ASP.NET Core app natively via `systemd` + Kestrel, with only PostgreSQL in Docker. This tutorial uses **full Docker Compose** instead — nginx, the ASP.NET Core app, and PostgreSQL all run as containers orchestrated by a single `docker-compose.production.yml`.

**Why this is the right call:**

- **Environment parity:** the production app runs in the same containerized .NET runtime used to build the image — no host .NET SDK needed, no runtime drift.
- **Simpler deployment:** updating the app is `docker compose pull && docker compose up -d` rather than a multi-step copy + `systemctl restart`.
- **Explicit dependency ordering:** Docker Compose's `depends_on: condition: service_healthy` ensures the app container waits for Postgres to be ready before attempting to connect — eliminating a class of startup-race failures common in systemd multi-unit configs.
- **Portable:** the entire stack can be replicated on another Droplet or moved off DigitalOcean with no host-level changes.

**Trade-off acknowledged:** you lose systemd's journal integration and direct host-level service inspection. Mitigation: Serilog's file sink (mounted to the host via a Docker volume) and `docker compose logs` give equivalent observability.

**Final production architecture:**

```
[Internet: :80 / :443]
          │
      [ Nginx ]   ← Docker container, ports 80+443 bound to host
          │          handles TLS termination + reverse proxy
          │
  [ codefolio-web ]  ← Docker container, port 8080 internal only
          │            ASP.NET Core 9 / Kestrel
          │
    [ postgres ]  ← Docker container, no host port exposed
                    named volume for persistence
```

All three containers are on a private Docker bridge network (`codefolio-net`). Only Nginx's ports touch the host.

---

## Pre-Deployment Checklist

Before you SSH into a Droplet for the first time, confirm every item on this list:

- [ ] `git tag` output includes `phase-2-production-hardening` (your baseline)
- [ ] `dotnet publish -c Release` exits 0 locally (verify `publish-output/` is in root `.gitignore`)
- [ ] `appsettings.Development.json` is **not** in `git ls-files` output
- [ ] `appsettings.Production.json` **is** in `git ls-files` output (placeholder-only)
- [ ] You have or can create a DigitalOcean account
- [ ] You have a domain name you control (required for Let's Encrypt HTTPS)
- [ ] You have your SendGrid API key and a verified sender email ready
- [ ] You have chosen and stored a strong production admin password (`Seed:AdminPassword`)
- [ ] You have a strong PostgreSQL password ready for production (different from dev)

---

## Task 1 — Application Code Change: Forward Headers

**Why:** When Kestrel sits behind Nginx, all requests arrive with `RemoteIpAddress` = `127.0.0.1` (the Nginx container) rather than the real client IP. This breaks rate-limiting-by-IP, HTTPS detection (`Request.IsHttps`), and any logging that records client addresses. `UseForwardedHeaders` reads the `X-Forwarded-For` and `X-Forwarded-Proto` headers that Nginx injects and restores the original values.

**This is the only application code change required for Phase 3.**

Open `CodeFolio/Program.cs`. Add this block **before `app.UseHttpsRedirection()`**:

```csharp
// Trust forwarded headers from Nginx reverse proxy
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

No additional package is needed — `Microsoft.AspNetCore.HttpOverrides` is part of the ASP.NET Core framework.

**Verification:** After adding this, build locally (`dotnet build`) and confirm 0 errors. No runtime test needed at this point — the real test happens in Task 13.

---

## Task 2 — Create the Dockerfile

Create `CodeFolio/Dockerfile` (inside the project directory, not the solution root — this matters for the `COPY` paths):

```dockerfile
# ─── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore dependencies first (layer cache: only re-runs on .csproj change)
COPY ["CodeFolio.csproj", "CodeFolio/"]
RUN dotnet restore "CodeFolio/CodeFolio.csproj"

# Copy source and publish
COPY . CodeFolio/
WORKDIR "/src/CodeFolio"
RUN dotnet publish "CodeFolio.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ─── Stage 2: Runtime image ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create a non-root user to run the process
RUN addgroup --system codefolio && adduser --system --ingroup codefolio codefolio

# Create logs directory with correct ownership
RUN mkdir -p /app/logs && chown codefolio:codefolio /app/logs

COPY --from=build /app/publish .
RUN chown -R codefolio:codefolio /app

USER codefolio

# Kestrel listens on 8080 inside the container (Nginx proxies to it)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CodeFolio.dll"]
```

**Notes:**
- The multi-stage build keeps the final image small — only the ASP.NET runtime, not the SDK.
- Running as a non-root user (`codefolio`) is a security baseline for production containers.
- Port `8080` is internal only — Nginx handles external 80/443.
- Serilog's `logs/` path resolves to `/app/logs` inside the container. The production Docker Compose mounts this to the host so logs survive container restarts.

**Verify locally (optional but recommended):**

```bash
# From the CodeFolio/ project directory (where Dockerfile lives)
docker build -t codefolio:test .
docker run --rm -e ASPNETCORE_ENVIRONMENT=Development codefolio:test
# Should see Serilog startup output — Ctrl+C to stop
```

---

## Task 3 — Create the Nginx Configuration

Create `nginx/codefolio.conf` at the **solution root** (alongside `docker-compose.yml`). This directory structure keeps all deployment config together:

```
codefolio2ai/
├── nginx/
│   └── codefolio.conf        ← created here
├── docker-compose.yml        ← existing (dev)
├── docker-compose.production.yml  ← created in Task 4
└── ...
```

**`nginx/codefolio.conf`** — HTTP only first (HTTPS added in Task 12 after cert is issued):

```nginx
server {
    listen 80;
    server_name yourdomain.com www.yourdomain.com;

    # Let's Encrypt ACME challenge (needed during cert issuance in Task 12)
    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    # Redirect all other HTTP traffic to HTTPS (uncomment AFTER cert is issued)
    # return 301 https://$host$request_uri;

    # Proxy to the ASP.NET Core container (HTTP-only during initial setup)
    location / {
        proxy_pass         http://codefolio-web:8080;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection keep-alive;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

Replace `yourdomain.com` with your actual domain throughout. The HTTPS `server` block is added in Task 12 — don't add it yet.

---

## Task 4 — Create the Production Docker Compose

Create `docker-compose.production.yml` at the solution root. This is separate from `docker-compose.yml` (which remains dev-only / PostgreSQL-only):

```yaml
# Production stack: Nginx + ASP.NET Core app + PostgreSQL
# Usage: docker compose -f docker-compose.production.yml --env-file .env.production up -d

services:

  nginx:
    image: nginx:alpine
    container_name: codefolio_nginx
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx/codefolio.conf:/etc/nginx/conf.d/codefolio.conf:ro
      - /etc/letsencrypt:/etc/letsencrypt:ro
      - certbot-webroot:/var/www/certbot
    depends_on:
      - codefolio-web
    networks:
      - codefolio-net
    restart: unless-stopped

  codefolio-web:
    image: codefolio:latest
    container_name: codefolio_web
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
      - SendGrid__ApiKey=${SENDGRID_API_KEY}
      - SendGrid__FromEmail=${SENDGRID_FROM_EMAIL}
      - SendGrid__FromName=${SENDGRID_FROM_NAME}
      - Seed__AdminPassword=${SEED_ADMIN_PASSWORD}
    volumes:
      - app-logs:/app/logs
    depends_on:
      postgres:
        condition: service_healthy
    networks:
      - codefolio-net
    restart: unless-stopped

  postgres:
    image: postgres:17-alpine
    container_name: codefolio_postgres_prod
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 5s
      timeout: 5s
      retries: 10
    networks:
      - codefolio-net
    restart: unless-stopped
    # No ports: block — postgres is not accessible from outside Docker network

networks:
  codefolio-net:
    driver: bridge

volumes:
  pgdata:
  app-logs:
  certbot-webroot:
```

**Key design decisions in this file:**

- `postgres` has no `ports:` mapping — it's completely isolated from the host network. Only `codefolio-web` can reach it (via the `codefolio-net` bridge network).
- The app's `ConnectionStrings__DefaultConnection` uses `Host=postgres` — the Docker service name, which Docker DNS resolves to the container IP inside `codefolio-net`. This is different from dev (which uses `localhost:5433`).
- `depends_on: condition: service_healthy` means the app container only starts after Postgres passes its healthcheck.
- `app-logs` volume persists Serilog's rolling log files across app container restarts.
- All secrets come from environment variables (`${VAR}`), sourced from `.env.production` (created on the server — never committed).

---

## Task 5 — Add Production Gitignore Entries

Add this entry to the root `.gitignore` (it likely doesn't exist yet) — `.env.production` must never be committed since it contains real secrets. (The Dockerfile itself should be committed; it is not a secret.) Verify the root `.gitignore` already includes `.env` and `*.env.local`, then add:

```gitignore
.env.production
```

While you're here, commit the new files created in Tasks 2–4:

```bash
git add CodeFolio/Dockerfile nginx/codefolio.conf docker-compose.production.yml
git add .gitignore
git commit -m "feat: add Dockerfile, nginx config, and production Docker Compose for Phase 3"
```

---

## Task 6 — Provision the DigitalOcean Droplet

**Specifications:**
- **Image:** Ubuntu 24.04 LTS (not 22.04 — 24.04 has longer support)
- **Size:** Basic → Regular → $6/month (1 vCPU, 1 GB RAM) is sufficient for a portfolio site. $12/month (2 GB RAM) gives more headroom.
- **Region:** Choose closest to your expected audience
- **Authentication:** SSH Key — upload your public key during provisioning (do **not** use password auth)
- **Hostname:** `codefolio-prod` (or similar)
- **No additional options needed** (backups, monitoring — these can be added later)

**After provisioning, note the Droplet's public IPv4 address.** You will need it for DNS (Task 9) and SSH.

**First SSH connection (as root):**

```bash
ssh root@YOUR_DROPLET_IP
```

---

## Task 7 — Initial Server Hardening

Run these commands on the Droplet as root. Do them **before** installing anything else.

### 7a. Create a non-root deploy user

```bash
adduser deploy
usermod -aG sudo deploy
```

Copy your SSH key to the new user (so you can log in without a password):

```bash
mkdir -p /home/deploy/.ssh
cp ~/.ssh/authorized_keys /home/deploy/.ssh/
chown -R deploy:deploy /home/deploy/.ssh
chmod 700 /home/deploy/.ssh
chmod 600 /home/deploy/.ssh/authorized_keys
```

Verify you can SSH as `deploy` in a **new terminal** before proceeding:

```bash
# From your local machine — new terminal window
ssh deploy@YOUR_DROPLET_IP
```

Once confirmed, all remaining steps run as `deploy` (use `sudo` where needed).

### 7b. Disable root SSH login

```bash
sudo sed -i 's/^PermitRootLogin yes/PermitRootLogin no/' /etc/ssh/sshd_config
sudo systemctl restart sshd
```

### 7c. Configure UFW firewall

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp    # SSH
sudo ufw allow 80/tcp    # HTTP (Nginx)
sudo ufw allow 443/tcp   # HTTPS (Nginx)
sudo ufw --force enable
sudo ufw status verbose
```

Expected output: three ALLOW rules for 22, 80, 443.

### 7d. Install and configure fail2ban

```bash
sudo apt update
sudo apt install -y fail2ban

# Create a local override (don't edit /etc/fail2ban/jail.conf directly)
sudo tee /etc/fail2ban/jail.local <<'EOF'
[DEFAULT]
bantime  = 3600
findtime = 600
maxretry = 5

[sshd]
enabled = true
EOF

sudo systemctl enable fail2ban
sudo systemctl start fail2ban
```

Verify: `sudo fail2ban-client status sshd` should show `Currently banned: 0` (and the jail as active).

---

## Task 8 — Install Docker and Docker Compose

Run as `deploy` on the Droplet:

```bash
# Install prerequisites
sudo apt update
sudo apt install -y ca-certificates curl gnupg

# Add Docker's official GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Add Docker repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker Engine + Compose plugin
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Add deploy user to docker group (avoids needing sudo for docker commands)
sudo usermod -aG docker deploy

# Log out and back in for group membership to take effect
exit
```

SSH back in as `deploy`, then verify:

```bash
docker --version        # Docker version 27.x or later
docker compose version  # Docker Compose version v2.x or later
docker run hello-world  # Should pull and run without sudo
```

---

## Task 9 — Configure DNS

In your domain registrar or DNS provider, create:

| Type | Name | Value | TTL |
|------|------|-------|-----|
| A | `@` (root) | `YOUR_DROPLET_IP` | 300 |
| A | `www` | `YOUR_DROPLET_IP` | 300 |

DNS propagation typically takes 5–30 minutes for TTL 300, up to several hours for higher TTLs.

**Verify propagation before proceeding to Task 12:**

```bash
# From your local machine or the Droplet
dig +short yourdomain.com
dig +short www.yourdomain.com
# Both should return YOUR_DROPLET_IP
```

Also verify HTTP reaches the Droplet (once Nginx is running):

```bash
curl -I http://yourdomain.com
```

---

## Task 10 — Deploy the Application (HTTP first)

Build and ship the Docker image. **From your local machine** (where the source code lives):

### 10a. Build the image

```bash
# From the solution root (codefolio2ai/)
docker build -t codefolio:latest ./CodeFolio
```

### 10b. Save and transfer the image

The simplest approach for a single-server portfolio deployment: save the image as a tarball and `scp` it to the Droplet. (Docker Hub or DigitalOcean Container Registry are better for team projects — overkill here.)

```bash
docker save codefolio:latest | gzip > codefolio-latest.tar.gz
scp codefolio-latest.tar.gz deploy@YOUR_DROPLET_IP:/home/deploy/
```

### 10c. Load the image on the Droplet

```bash
# On the Droplet
ssh deploy@YOUR_DROPLET_IP
docker load < /home/deploy/codefolio-latest.tar.gz
docker images | grep codefolio   # Should show codefolio:latest
```

### 10d. Copy deployment files to the Droplet

From your local machine, copy the deployment configuration:

```bash
# Create the deployment directory on the server
ssh deploy@YOUR_DROPLET_IP "mkdir -p /home/deploy/codefolio/nginx"

# Copy files
scp docker-compose.production.yml deploy@YOUR_DROPLET_IP:/home/deploy/codefolio/
scp nginx/codefolio.conf deploy@YOUR_DROPLET_IP:/home/deploy/codefolio/nginx/
```

### 10e. Create the production secrets file on the server

**On the Droplet** — this file is never copied from your local machine or committed to git:

```bash
nano /home/deploy/codefolio/.env.production
```

Populate it with your real production values:

```dotenv
# PostgreSQL
POSTGRES_DB=CodeFolioDB
POSTGRES_USER=codefolio_prod
POSTGRES_PASSWORD=<strong-random-password-different-from-dev>

# SendGrid
SENDGRID_API_KEY=SG.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
SENDGRID_FROM_EMAIL=your-verified-sender@yourdomain.com
SENDGRID_FROM_NAME=CodeFolio

# Admin seed
SEED_ADMIN_PASSWORD=<strong-random-admin-password>
```

Set restrictive permissions:

```bash
chmod 600 /home/deploy/codefolio/.env.production
```

**Verify permissions:** `ls -la /home/deploy/codefolio/.env.production` should show `-rw-------`.

### 10f. Also update appsettings.Production.json AllowedHosts

In your local source, open `CodeFolio/appsettings.Production.json` and update `AllowedHosts` to your real domain:

```json
"AllowedHosts": "yourdomain.com;www.yourdomain.com"
```

Commit this change:

```bash
git add CodeFolio/appsettings.Production.json
git commit -m "config: set production AllowedHosts to real domain"
```

Then rebuild the image (so the updated `appsettings.Production.json` is baked in) and re-transfer it.

### 10g. Start the stack (HTTP only, no HTTPS yet)

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production up -d
```

Verify all three containers are running:

```bash
docker compose -f docker-compose.production.yml ps
```

Expected:
```
NAME                      SERVICE        STATUS    PORTS
codefolio_nginx           nginx          running   0.0.0.0:80->80/tcp, 0.0.0.0:443->443/tcp
codefolio_web             codefolio-web  running
codefolio_postgres_prod   postgres       running
```

Check the app logs for startup errors:

```bash
docker compose -f docker-compose.production.yml logs codefolio-web --tail=50
```

Look for Serilog startup output. A clean start will show database connectivity and the `DbInitializer` seeding logs.

**Quick smoke test:**

```bash
curl -s http://yourdomain.com/health
# Expected: Healthy
```

---

## Task 11 — Apply Database Migrations

The `codefolio-web` container runs the compiled app but does **not** automatically apply EF Core migrations on startup. You need to run `dotnet ef database update` pointing at the production database from inside the container.

**Option A: Run migrations from the app container** (easiest — no .NET SDK on server needed):

The production image is a runtime image (no SDK). Install the EF Core tools in a temporary container using the SDK image:

```bash
docker run --rm \
  --network codefolio_codefolio-net \
  -e ConnectionStrings__DefaultConnection="Host=postgres;Port=5432;Database=CodeFolioDB;Username=codefolio_prod;Password=YOUR_POSTGRES_PASSWORD" \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  bash -c "dotnet tool install -g dotnet-ef && \
           export PATH=\"\$PATH:/root/.dotnet/tools\" && \
           dotnet ef database update \
             --project /src/CodeFolio \
             --connection 'Host=postgres;Port=5432;Database=CodeFolioDB;Username=codefolio_prod;Password=YOUR_POSTGRES_PASSWORD'"
```

> **Note:** This option requires your source to also be on the server — not ideal. Use Option B.

**Option B: Apply migrations via a one-off SDK container on the server** (recommended), using the already-running Postgres container on the same Docker network:

```bash
# On the Droplet — copy just the project files needed for migration
cd /home/deploy
# Transfer source (migration files + csproj) from local machine:
```

```bash
# From local machine — create a minimal tarball of migration-relevant files
tar -czf migrations.tar.gz \
  CodeFolio/Migrations/ \
  CodeFolio/CodeFolio.csproj \
  CodeFolio/Data/ \
  CodeFolio/Models/
scp migrations.tar.gz deploy@YOUR_DROPLET_IP:/home/deploy/
```

```bash
# On the Droplet
mkdir -p /home/deploy/migration-src
tar -xzf /home/deploy/migrations.tar.gz -C /home/deploy/migration-src

docker run --rm \
  --network codefolio_codefolio-net \
  -v /home/deploy/migration-src:/src \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  bash -c "dotnet tool install -g dotnet-ef && \
           export PATH=\"\$PATH:/root/.dotnet/tools\" && \
           dotnet ef database update \
             --project CodeFolio \
             --connection 'Host=postgres;Port=5432;Database=CodeFolioDB;Username=codefolio_prod;Password=YOUR_POSTGRES_PASSWORD'"
```

**Verification:**

```bash
# Check that all tables exist
docker exec codefolio_postgres_prod \
  psql -U codefolio_prod -d CodeFolioDB -c '\dt'
```

Expected: 11 tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserRoles`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`, `BlogPosts`, `ContactMessages`, `Projects`, `ResumeSections`).

After migrations are applied, restart the app container so `DbInitializer` seeds the admin user and resume sections:

```bash
docker compose -f docker-compose.production.yml restart codefolio-web
docker compose -f docker-compose.production.yml logs codefolio-web --tail=30
```

The logs should show `SeedAdmin` inserting the admin user on first run, and `SeedResumeSections` inserting 7 rows.

---

## Task 12 — Obtain HTTPS Certificate via Certbot

**Prerequisite:** DNS must be propagated (Task 9 verified) and the Nginx container must be running and serving port 80 (Task 10 verified).

### 12a. Install Certbot on the Droplet

```bash
sudo apt install -y certbot
```

### 12b. Stop Nginx temporarily for standalone cert issuance

Certbot's standalone mode binds to port 80 directly. Stop Nginx first:

```bash
docker compose -f docker-compose.production.yml stop nginx
```

### 12c. Issue the certificate

```bash
sudo certbot certonly --standalone \
  -d yourdomain.com \
  -d www.yourdomain.com \
  --email your@email.com \
  --agree-tos \
  --no-eff-email
```

Certbot will verify domain ownership via an HTTP challenge (it briefly serves on port 80). On success, certificates are written to:
- `/etc/letsencrypt/live/yourdomain.com/fullchain.pem`
- `/etc/letsencrypt/live/yourdomain.com/privkey.pem`

### 12d. Update Nginx config to enable HTTPS

On the Droplet, edit `/home/deploy/codefolio/nginx/codefolio.conf` (or push the updated file from local):

Replace the entire file with:

```nginx
# Redirect HTTP to HTTPS
server {
    listen 80;
    server_name yourdomain.com www.yourdomain.com;

    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    location / {
        return 301 https://$host$request_uri;
    }
}

# HTTPS server
server {
    listen 443 ssl;
    http2 on;
    server_name yourdomain.com www.yourdomain.com;

    ssl_certificate     /etc/letsencrypt/live/yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/yourdomain.com/privkey.pem;

    # Strong TLS settings
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 1d;

    # HSTS (6 months — enable once HTTPS is stable)
    # add_header Strict-Transport-Security "max-age=15768000" always;

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    location / {
        proxy_pass         http://codefolio-web:8080;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection keep-alive;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 90s;
    }
}
```

### 12e. Start Nginx with the updated config

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml start nginx

# Test Nginx config before reloading
docker exec codefolio_nginx nginx -t
# Expected: "syntax is ok" and "test is successful"

docker exec codefolio_nginx nginx -s reload
```

### 12f. Verify HTTPS works

```bash
curl -I https://yourdomain.com
# Expected: HTTP/2 200 (or 301 from www)

curl -I http://yourdomain.com
# Expected: HTTP/1.1 301 Moved Permanently (redirects to https://)

curl -s https://yourdomain.com/health
# Expected: Healthy
```

### 12g. Set up automatic certificate renewal

Let's Encrypt certificates expire after 90 days. Certbot can renew them automatically, but because Nginx is in a Docker container (not a host service), you need to reload Nginx after renewal.

Create a renewal hook:

```bash
sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy
sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh <<'EOF'
#!/bin/bash
cd /home/deploy/codefolio
docker exec codefolio_nginx nginx -s reload
EOF
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
```

Add a cron job for renewal (Certbot only renews when < 30 days remain, so running twice daily is safe):

```bash
sudo crontab -e
```

Add:
```
0 2,14 * * * certbot renew --quiet
```

Test the renewal process (dry run):

```bash
sudo certbot renew --dry-run
# Expected: "Congratulations, all simulated renewals succeeded"
```

---

## Task 13 — Production Smoke Test

Run this full checklist from a browser in a private/incognito window (to avoid cached sessions):

### Connectivity & Security

- [ ] `https://yourdomain.com` loads without certificate warning
- [ ] `http://yourdomain.com` redirects to `https://` (not served directly)
- [ ] `https://www.yourdomain.com` loads (or redirects to `https://yourdomain.com`)
- [ ] Browser shows valid padlock; cert issued by "Let's Encrypt" for your domain

### Health & Observability

- [ ] `GET https://yourdomain.com/health` returns HTTP 200 with body `Healthy`
- [ ] Serilog logs exist on the Droplet: `docker compose -f docker-compose.production.yml exec codefolio-web ls /app/logs/` shows today's log file
- [ ] Serilog log file contains the admin seed and resume section seed entries from startup

### Public Pages

- [ ] Homepage (`/`) loads with correct layout and no broken images
- [ ] Projects list (`/Project`) loads
- [ ] Blog list (`/BlogPost`) loads  
- [ ] Resume page (`/Resume`) loads with seeded sections
- [ ] Contact page (`/Contact`) loads with the form

### Authentication

- [ ] Navigate to `/Project/Create` → redirects to `/Identity/Account/Login` (not `/Account/Login`)
- [ ] Login as `admin@example.com` with your production `Seed:AdminPassword` → succeeds
- [ ] After login: "Hello, Admin!" renders, role shows as "Admin", Logout visible

### Content Management (Admin CRUD)

- [ ] Create a test Project → appears in `/Project` list
- [ ] Edit the test Project → changes persist
- [ ] Delete the test Project → removed from list

### Contact Form

- [ ] Submit contact form with valid data → redirects to Thank You page
- [ ] Check the database: `docker exec codefolio_postgres_prod psql -U codefolio_prod -d CodeFolioDB -c "SELECT * FROM \"ContactMessages\" ORDER BY \"SentAt\" DESC LIMIT 1;"`
- [ ] Row is present. If SendGrid is configured, check your inbox.

### Rate Limiting

- [ ] Submit the contact form 6+ times within one minute → 6th request returns HTTP 429 (Too Many Requests)

### Security Headers

- [ ] Run your domain through [securityheaders.com](https://securityheaders.com) — verify `X-Frame-Options`, `X-Content-Type-Options`, and `Referrer-Policy` are present

---

## Task 14 — Deployment Update Workflow

For all future deploys (bug fixes, new features):

### 14a. Local: build, test, commit

```bash
# Build and test locally
dotnet build
dotnet run --project CodeFolio   # manual smoke test

# Commit changes
git add .
git commit -m "feat: ..."
git push origin main
```

### 14b. Local: build new image and transfer

```bash
# Build with a version tag and also tag as latest
docker build -t codefolio:$(git rev-parse --short HEAD) -t codefolio:latest ./CodeFolio

# Save and transfer
docker save codefolio:latest | gzip > codefolio-latest.tar.gz
scp codefolio-latest.tar.gz deploy@YOUR_DROPLET_IP:/home/deploy/
```

### 14c. Server: load and redeploy

```bash
ssh deploy@YOUR_DROPLET_IP

cd /home/deploy
docker load < codefolio-latest.tar.gz

cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml up -d --no-deps codefolio-web
# --no-deps: only restarts the app container, not nginx or postgres
```

Verify:

```bash
docker compose -f docker-compose.production.yml ps
docker compose -f docker-compose.production.yml logs codefolio-web --tail=20
curl -s https://yourdomain.com/health
```

### When migrations are involved

If the deploy includes new EF Core migrations, run the migration container (Task 11 method) **before** `docker compose up -d`. Order:

1. Load new image
2. Run migration container (pointing at production Postgres)
3. `docker compose up -d --no-deps codefolio-web`

### Rollback

Keep the previous image tagged:

```bash
# Tag as codefolio:previous (or use a specific git hash instead)
docker build \
  -t codefolio:$(git rev-parse --short HEAD) \
  -t codefolio:previous \
  ./CodeFolio
```

To roll back:

```bash
docker tag codefolio:PREVIOUS_GIT_HASH codefolio:latest
docker compose -f docker-compose.production.yml up -d --no-deps codefolio-web
```

---

## Task 15 — PostgreSQL Backup Strategy

PostgreSQL data lives in the `pgdata` Docker volume. Volume-level backups are possible but fragile — `pg_dump` is the reliable, tested approach.

### 15a. Create the backup script

On the Droplet:

```bash
sudo mkdir -p /opt/backups/codefolio
sudo chown deploy:deploy /opt/backups/codefolio

nano /home/deploy/backup-db.sh
```

```bash
#!/bin/bash
set -euo pipefail

# Load secrets
source /home/deploy/codefolio/.env.production

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="/opt/backups/codefolio/codefolio_${TIMESTAMP}.sql.gz"

echo "[$(date)] Starting backup: ${BACKUP_FILE}"

docker exec codefolio_postgres_prod \
  pg_dump -U "${POSTGRES_USER}" "${POSTGRES_DB}" \
  | gzip > "${BACKUP_FILE}"

echo "[$(date)] Backup complete: ${BACKUP_FILE}"

# Delete backups older than 14 days
find /opt/backups/codefolio -name "*.sql.gz" -mtime +14 -delete
echo "[$(date)] Old backups pruned."
```

```bash
chmod +x /home/deploy/backup-db.sh

# Test it manually first
/home/deploy/backup-db.sh
ls -lh /opt/backups/codefolio/
```

### 15b. Schedule the backup via cron

```bash
crontab -e
```

Add (runs at 3:00 AM daily):

```
0 3 * * * /home/deploy/backup-db.sh >> /home/deploy/backup.log 2>&1
```

### 15c. Off-site backup via DigitalOcean Spaces (optional but recommended)

DigitalOcean Spaces is S3-compatible. Install `s3cmd`:

```bash
sudo apt install -y s3cmd
s3cmd --configure   # enter your Spaces access key, secret, region endpoint
```

Add to `backup-db.sh` after the `gzip` line:

```bash
s3cmd put "${BACKUP_FILE}" s3://your-spaces-bucket/codefolio-backups/
```

### 15d. Test restore procedure

A backup that's never been tested is not a backup. At least once, verify you can restore:

```bash
# List backups
ls -lh /opt/backups/codefolio/

# Test restore into a temporary database
docker exec -i codefolio_postgres_prod \
  createdb -U "${POSTGRES_USER}" codefolio_restore_test

zcat /opt/backups/codefolio/BACKUP_FILE.sql.gz \
  | docker exec -i codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d codefolio_restore_test

# Verify tables exist in the restored DB
docker exec codefolio_postgres_prod \
  psql -U "${POSTGRES_USER}" -d codefolio_restore_test -c '\dt'

# Clean up
docker exec codefolio_postgres_prod \
  dropdb -U "${POSTGRES_USER}" codefolio_restore_test
```

---

## Architectural Decisions — Resolved

These questions were open during tutorial authoring. Decisions made during execution:

1. **Container registry vs. `scp`:** Used `docker save / scp` — correct choice for a solo portfolio. No registry needed.
2. **Zero-downtime deploys:** Acceptable brief gap for a portfolio deployment. Documented in Task 14 workflow.
3. **Log aggregation:** Serilog rolling file sink to `app-logs` named volume is sufficient for single-server deployment. No external log service needed at this scale.
4. **HSTS:** Left commented out in Nginx config intentionally. Enable after HTTPS has been stable for at least 7 days.
5. **DigitalOcean Managed Database:** Self-managed Postgres container is the right call for a portfolio project. Documented backup strategy in Task 15 / Phase 5.

---

## ✅ Phase 3 — Production Deployment COMPLETE

**Completed:** July 29, 2026 — 3:13 PM  
**Production URL:** https://codefolio2ai.com  
**Git tag:** `phase-3-production-deployment`  
**Commit message:** `docs: close out Phase 3 production deployment`

---

### Deployment Architecture

```
[Internet: :80 / :443]
          │
      [ Nginx ]           container: codefolio_nginx
          │               TLS termination + reverse proxy
          │               Let's Encrypt cert (valid through 2026-10-26)
          │
  [ codefolio-web ]       container: codefolio_web
          │               ASP.NET Core 9 MVC — Kestrel on port 8080 (internal)
          │
    [ postgres ]          container: codefolio_postgres_prod
                          PostgreSQL 17 — no host port, named volume: codefolio_pgdata

All containers on private bridge: codefolio_codefolio-net
Host: DigitalOcean Droplet (Ubuntu 24.04 LTS, Toronto / TOR1, 1 vCPU / 1 GB RAM)
```

---

### SSL / HTTPS Verification

| Check | Result |
|---|---|
| Certificate authority | Let's Encrypt |
| Domains covered | `codefolio2ai.com`, `www.codefolio2ai.com` |
| Certificate validity | 2026-07-28 through 2026-10-26 (90-day cycle) |
| Auto-renewal | Cron + `/etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh` — `certbot renew --dry-run` succeeded |
| HTTP → HTTPS redirect | `301 Moved Permanently` confirmed |
| Protocol | HTTP/2 on HTTPS |

---

### Docker Services Summary

| Container | Image | Status | Notes |
|---|---|---|---|
| `codefolio_nginx` | `nginx:alpine` | Running | Ports 80, 443 bound to host |
| `codefolio_web` | `codefolio:latest` | Running | Port 8080 internal only |
| `codefolio_postgres_prod` | `postgres:17-alpine` | Running | No host port; `codefolio_pgdata` volume |

---

### Database Persistence Confirmation

- Database: `CodeFolioDB`, user: `codefolio_prod`
- `InitialCreate` EF Core migration applied via a disposable SDK container on the `codefolio_codefolio-net` Docker network
  - **Migration note:** required the full source tree (not just `Migrations/` + `Data/` + `Models/`) because `CodeFolio.csproj` is a single-project executable, not a class library — the entire project must compile as a unit
- Admin user and 7 `ResumeSection` rows seeded by `DbInitializer` on first successful start
- 12 tables confirmed present in production (`\dt`)
- **Data survived:** both a `docker compose restart` and a full `docker compose down → up -d` container recreation

---

### Production Smoke Test Results

| Test | Result | Notes |
|---|---|---|
| HTTPS loads without cert warning | ✅ Pass | Valid Let's Encrypt cert |
| HTTP redirects to HTTPS | ✅ Pass | `301 Moved Permanently` |
| Homepage renders | ✅ Pass | Nav, assets, footer all correct |
| Projects page | ✅ Pass | Loads; zero records expected (no seeded content) |
| Blog page | ✅ Pass | Loads; zero records expected |
| Resume page | ✅ Pass | 7 seeded sections render |
| Contact form — validation | ✅ Pass | Client-side required-field enforcement |
| Contact form — submission | ✅ Pass | Row persisted to `ContactMessages` |
| `/health` endpoint | ✅ Pass | Returns `Healthy` (HTTP 200) |
| Admin login | ✅ Pass | `admin@example.com` + production password succeeds |
| Authorization enforcement | ✅ Pass | Anonymous requests to protected routes redirect to `/Identity/Account/Login` |
| Container restart persistence | ✅ Pass | `docker compose restart` — all data intact |
| Full container recreation | ✅ Pass | `down → up -d` — all 12 tables and seeded data intact |

---

### Known Non-Blocking Limitations

| Item | Severity | Classification |
|---|---|---|
| SendGrid email delivery rejected ("Maximum credits exceeded") | Non-blocking | External account-plan limit — not an application defect. `EmailSender` degrades gracefully per Phase 2 hardening; contact submissions persist regardless. | 
| `jquery-validation-unobtrusive` console 404 | Non-blocking | Pre-existing (since Phase 1); harmless; documented in `CLAUDE.md` Known Issues |
| No sample Project/BlogPost content | Non-blocking | Expected — `DbInitializer` does not seed portfolio content |
| Tasks 14–15 (deployment workflow + backup) not yet tested | Follow-up | Deferred to Phase 5. Do not rely on this deployment long-term until backup is in place. |

---

### What Remains — Phase 5 Scope

Tasks 14 (deployment update workflow) and 15 (PostgreSQL backup strategy) from this tutorial are documented but not yet executed. They move into **Phase 5 — Production Operations** alongside monitoring, CI/CD, and security header hardening. See `ROADMAP.md`.

---

*Tutorial steps 1–13 retained above as the reference procedure for future redeployments, updates, or a second environment.*
