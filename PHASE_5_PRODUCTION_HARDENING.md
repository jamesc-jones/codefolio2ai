# Phase 5 — Production Hardening Tutorial

> **Status:** Repository-side preparation complete for 5.1–5.6 (scripts, configs, workflow file, and documentation). VPS execution, external account creation (UptimeRobot, GHCR PAT, email provider), and DNS changes are still pending — each section below is clearly marked with what's done here vs. what requires you to act on the actual server or an external service.  
> **Created:** 2026-07-30 · **Updated:** 2026-07-30  
> **Prerequisites:** Phases 1–4 complete. Application live at https://codefolio2ai.com  
> **Execution environment:** Claude CLI — implement each sub-phase independently; each is self-contained and can be done in any order after 5.1

---

## Production Infrastructure Reference

Before starting, confirm this matches your current state:

```
Host:         DigitalOcean Droplet (Ubuntu 24.04 LTS)
Deploy user:  deploy
App dir:      /home/deploy/codefolio/
Compose file: docker-compose.production.yml
Env file:     .env.production  (chmod 600)
Nginx config: nginx/codefolio.conf

Containers:
  codefolio_nginx          (nginx:alpine)
  codefolio_web            (codefolio:latest)
  codefolio_postgres_prod  (postgres:17-alpine)

Docker network:  codefolio_codefolio-net
Named volumes:   codefolio_pgdata, codefolio_app-logs, codefolio_certbot-webroot

Domain:      codefolio2ai.com / www.codefolio2ai.com
Health URL:  https://codefolio2ai.com/health
```

Verify before proceeding:
```bash
ssh deploy@YOUR_DROPLET_IP
docker compose -f /home/deploy/codefolio/docker-compose.production.yml ps
curl -s https://codefolio2ai.com/health
# Both should show healthy / Healthy
```

---

## Phase 5.1 — Automated Database Backups

**Goal:** Daily automated `pg_dump` of the production database, compressed, stored locally with 14-day retention, and optionally encrypted and uploaded off-site.

**Risk level:** No-risk — read-only operation against a running database. Does not affect the app.

**Repository-side status:** Nothing to change in the repo for this phase — the backup script lives only on the server, not in source control. The steps below are verified correct against the current `docker-compose.production.yml` (container name `codefolio_postgres_prod`, env vars from `.env.production`) and match the requested naming (`codefolio_YYYY_MM_DD_HHMMSS.sql.gz`) and 14-day retention.

> ### ▶ Run manually on production VPS
> Nothing here has been executed. Steps 5.1a → 5.1c → 5.1f → 5.1g below are additive only (new directory, new script, new cron lines, a throwaway test database) — none of them touch the live `codefolio` database or restart any container. Run them in order, then paste back the output of 5.1c and 5.1g so this phase can be marked verified. Steps 5.1d/5.1e (off-site upload) require creating an external cloud storage account and are optional — skip them for now unless you want off-site backups today.

---

### 5.1a — Create the Backup Directory

```bash
ssh deploy@YOUR_DROPLET_IP

sudo mkdir -p /opt/backups/codefolio
sudo chown deploy:deploy /opt/backups/codefolio
chmod 700 /opt/backups/codefolio

# Verify
ls -la /opt/backups/
# Expected: drwx------ 2 deploy deploy ... codefolio
```

---

### 5.1b — Create the Backup Script

```bash
nano /home/deploy/backup-db.sh
```

Paste the following (replace `ENCRYPTION_PASSPHRASE` placeholder in the optional section):

```bash
#!/usr/bin/env bash
# /home/deploy/backup-db.sh
# Backs up the production CodeFolio PostgreSQL database.
# Usage: ./backup-db.sh [--upload]
# Cron: 0 3 * * * /home/deploy/backup-db.sh --upload >> /home/deploy/backup.log 2>&1

set -euo pipefail

# ── Load production secrets ──────────────────────────────────────────────────
# shellcheck source=/dev/null
source /home/deploy/codefolio/.env.production

# ── Configuration ────────────────────────────────────────────────────────────
BACKUP_DIR="/opt/backups/codefolio"
CONTAINER="codefolio_postgres_prod"
TIMESTAMP=$(date +%Y_%m_%d_%H%M%S)
FILENAME="codefolio_${TIMESTAMP}.sql.gz"
BACKUP_PATH="${BACKUP_DIR}/${FILENAME}"
RETENTION_DAYS=14
UPLOAD=${1:-}   # pass --upload to trigger cloud upload

# ── Backup ───────────────────────────────────────────────────────────────────
echo "[$(date -Iseconds)] Starting backup: ${BACKUP_PATH}"

docker exec "${CONTAINER}" \
    pg_dump -U "${POSTGRES_USER}" --no-password "${POSTGRES_DB}" \
    | gzip -9 > "${BACKUP_PATH}"

BACKUP_SIZE=$(du -sh "${BACKUP_PATH}" | cut -f1)
echo "[$(date -Iseconds)] Backup complete: ${BACKUP_PATH} (${BACKUP_SIZE})"

# ── Verify the backup is non-empty ───────────────────────────────────────────
if [[ ! -s "${BACKUP_PATH}" ]]; then
    echo "[$(date -Iseconds)] ERROR: Backup file is empty. Aborting." >&2
    rm -f "${BACKUP_PATH}"
    exit 1
fi

# ── Prune old backups ────────────────────────────────────────────────────────
find "${BACKUP_DIR}" -name "*.sql.gz" -mtime "+${RETENTION_DAYS}" -delete
echo "[$(date -Iseconds)] Pruned backups older than ${RETENTION_DAYS} days."

# ── Optional: upload to cloud storage ────────────────────────────────────────
# Uncomment and configure ONE of the options below.
# See Phase 5.1d and 5.1e for setup instructions.

if [[ "${UPLOAD}" == "--upload" ]]; then

    # ── Option A: DigitalOcean Spaces (via s3cmd) ────────────────────────────
    # Requires: s3cmd installed and ~/.s3cfg configured (see 5.1d)
    #
    # ENCRYPTED_PATH="${BACKUP_PATH}.enc"
    # openssl enc -aes-256-cbc -pbkdf2 -iter 100000 \
    #     -pass env:BACKUP_ENCRYPTION_KEY \
    #     -in "${BACKUP_PATH}" -out "${ENCRYPTED_PATH}"
    # s3cmd put "${ENCRYPTED_PATH}" "s3://your-spaces-bucket/codefolio-backups/"
    # rm -f "${ENCRYPTED_PATH}"
    # echo "[$(date -Iseconds)] Uploaded encrypted backup to DO Spaces."

    # ── Option B: Backblaze B2 (via b2 CLI) ──────────────────────────────────
    # Requires: b2 CLI installed and authorized (see 5.1e)
    #
    # ENCRYPTED_PATH="${BACKUP_PATH}.enc"
    # openssl enc -aes-256-cbc -pbkdf2 -iter 100000 \
    #     -pass env:BACKUP_ENCRYPTION_KEY \
    #     -in "${BACKUP_PATH}" -out "${ENCRYPTED_PATH}"
    # b2 upload-file your-bucket-name "${ENCRYPTED_PATH}" "codefolio-backups/${FILENAME}.enc"
    # rm -f "${ENCRYPTED_PATH}"
    # echo "[$(date -Iseconds)] Uploaded encrypted backup to Backblaze B2."

    echo "[$(date -Iseconds)] Upload flag passed but no provider is configured. Edit backup-db.sh to enable."
fi

echo "[$(date -Iseconds)] Backup job complete."
```

Make it executable:

```bash
chmod 700 /home/deploy/backup-db.sh
```

---

### 5.1c — Test the Backup Script Manually

Always test before scheduling:

```bash
/home/deploy/backup-db.sh
# Expected output:
# [2026-07-30T03:00:01+00:00] Starting backup: /opt/backups/codefolio/codefolio_2026_07_30_030001.sql.gz
# [2026-07-30T03:00:02+00:00] Backup complete: /opt/backups/codefolio/codefolio_2026_07_30_030001.sql.gz (48K)
# [2026-07-30T03:00:02+00:00] Pruned backups older than 14 days.
# [2026-07-30T03:00:02+00:00] Backup job complete.

# Verify the file exists and is readable
ls -lh /opt/backups/codefolio/
zcat /opt/backups/codefolio/codefolio_*.sql.gz | head -5
# Expected: PostgreSQL dump header lines starting with "--"
```

---

### 5.1d — (Optional) Upload to DigitalOcean Spaces

DigitalOcean Spaces is S3-compatible and integrates cleanly with an existing DigitalOcean account ($5/month minimum, 250 GB included).

**Create a Space:**

1. DigitalOcean dashboard → Spaces → Create a Space
2. Name it `codefolio-backups`, choose the same region as your Droplet
3. Settings → Manage Keys → Generate new key → copy the Access Key and Secret Key

**Install s3cmd:**

```bash
sudo apt install -y s3cmd
s3cmd --configure
# Enter: Access Key, Secret Key
# Default Region: us-east-1 (regardless of actual region)
# S3 Endpoint: [your-region].digitaloceanspaces.com  (e.g., tor1.digitaloceanspaces.com)
# DNS-style bucket+hostname: %(bucket)s.[your-region].digitaloceanspaces.com
# Use HTTPS: Yes
# Test access? Yes
```

**Add encryption key to `.env.production`:**

```bash
nano /home/deploy/codefolio/.env.production
```

Add:
```dotenv
BACKUP_ENCRYPTION_KEY=<strong-random-passphrase-min-32-chars>
```

**Test upload manually:**

```bash
# Source the env file to get the encryption key
source /home/deploy/codefolio/.env.production

TEST_FILE="/opt/backups/codefolio/$(ls /opt/backups/codefolio/ | tail -1)"
ENCRYPTED="${TEST_FILE}.enc"

openssl enc -aes-256-cbc -pbkdf2 -iter 100000 \
    -pass env:BACKUP_ENCRYPTION_KEY \
    -in "${TEST_FILE}" -out "${ENCRYPTED}"

s3cmd put "${ENCRYPTED}" "s3://codefolio-backups/codefolio-backups/"
# Expected: upload: ... -> s3://codefolio-backups/... [1 of 1]

rm -f "${ENCRYPTED}"
```

**Uncomment Option A in the backup script** after confirming the test upload works.

---

### 5.1e — (Optional) Upload to Backblaze B2

Backblaze B2 is free for the first 10 GB and costs $0.006/GB after that — negligible for database dumps.

```bash
pip3 install --break-system-packages b2

# Authorize
b2 authorize-account YOUR_APP_KEY_ID YOUR_APP_KEY

# Create bucket (do this once)
b2 create-bucket codefolio-backups allPrivate
```

Add `BACKUP_ENCRYPTION_KEY` to `.env.production` (same as 5.1d if using both).

**Uncomment Option B in the backup script** after testing manually:

```bash
source /home/deploy/codefolio/.env.production
LATEST="/opt/backups/codefolio/$(ls /opt/backups/codefolio/ | tail -1)"
openssl enc -aes-256-cbc -pbkdf2 -iter 100000 \
    -pass env:BACKUP_ENCRYPTION_KEY \
    -in "${LATEST}" -out "${LATEST}.enc"
b2 upload-file codefolio-backups "${LATEST}.enc" "codefolio-backups/test-upload.enc"
rm -f "${LATEST}.enc"
```

---

### 5.1f — Schedule with Cron

```bash
crontab -e
```

Add (3:00 AM daily):

```cron
# CodeFolio database backup — runs at 3 AM daily
0 3 * * * /home/deploy/backup-db.sh --upload >> /home/deploy/backup.log 2>&1

# Weekly cleanup of old log lines from backup.log (keep last 1000 lines)
0 4 * * 0 tail -1000 /home/deploy/backup.log > /home/deploy/backup.log.tmp && mv /home/deploy/backup.log.tmp /home/deploy/backup.log
```

Verify the cron is registered:

```bash
crontab -l
# Should show the two new lines
```

**Simulate the cron run** (without waiting for 3 AM):

```bash
/home/deploy/backup-db.sh --upload
cat /home/deploy/backup.log
```

---

### 5.1g — Verify Decrypt (Critical)

A backup you cannot restore is not a backup. Run this now — before you ever need it under pressure.

```bash
source /home/deploy/codefolio/.env.production
BACKUP_FILE="/opt/backups/codefolio/$(ls /opt/backups/codefolio/ | tail -1)"

# Decrypt and inspect (does not restore — just checks the file is valid)
openssl enc -d -aes-256-cbc -pbkdf2 -iter 100000 \
    -pass env:BACKUP_ENCRYPTION_KEY \
    -in "${BACKUP_FILE}.enc" \
    | zcat | head -10
# Expected: PostgreSQL dump header lines (if using encryption)
# Or just: zcat "${BACKUP_FILE}" | head -10  (if not encrypting)
```

---

## Phase 5.2 — Monitoring Alerts

**Goal:** Get notified within 5 minutes if the site goes down or the SSL certificate is approaching expiry. No code changes required.

**Status: requires an external account (UptimeRobot) — not created automatically.** The instructions below are ready to follow whenever you want to set this up; nothing has been signed up for on your behalf.

---

### 5.2a — Create UptimeRobot Account

1. Go to https://uptimerobot.com → Sign Up (free tier supports 50 monitors, 5-minute polling)
2. Verify your email
3. Add a notification contact: My Settings → Alert Contacts → Add Alert Contact → Email → your email address

---

### 5.2b — Monitor: Health Endpoint

This is your primary availability signal — it tests that the ASP.NET Core app, its dependencies, and Nginx are all responding correctly.

1. Dashboard → Add New Monitor
2. Monitor Type: **HTTP(s)**
3. Friendly Name: `CodeFolio — Health Check`
4. URL: `https://codefolio2ai.com/health`
5. Monitoring Interval: **5 minutes** (minimum on free tier)
6. Monitor Timeout: 30 seconds
7. Alert Contacts: your email contact
8. Under "Advanced Settings":
   - Alert when down for: **1 occurrence** (alert on first failure — don't wait for confirmation)
   - Keyword: enable keyword monitoring → search for keyword `Healthy` (ensures response body is correct, not just HTTP 200)
9. Create Monitor

---

### 5.2c — Monitor: Homepage Availability

A separate monitor on the root URL catches Nginx issues that the `/health` endpoint might not surface (e.g., a bad Nginx config reload after a cert renewal).

1. Add New Monitor
2. Type: HTTP(s)
3. Name: `CodeFolio — Homepage`
4. URL: `https://codefolio2ai.com`
5. Interval: 5 minutes
6. Alert Contacts: your email
7. Create Monitor

---

### 5.2d — Monitor: SSL Certificate Expiry

Let's Encrypt certs expire every 90 days. Auto-renewal is configured, but monitoring is an independent safety net.

1. Add New Monitor
2. Type: **SSL Certificate**  
   *(UptimeRobot → Add Monitor → Monitor Type → SSL)*
3. Name: `CodeFolio — SSL Certificate`
4. Domain Name: `codefolio2ai.com`
5. Alert when certificate expires in less than: **30 days**  
   (Let's Encrypt auto-renews at <30 days — an alert here means renewal failed)
6. Create Monitor

---

### 5.2e — Verify Monitoring is Working

After creating all three monitors, wait 5–10 minutes for the first check to run, then confirm:
- All three monitors show **Up** / green status
- The health monitor's "Response Details" shows keyword `Healthy` matched

**Test an alert (optional but recommended):** Stop the web container for 2 minutes, confirm an email arrives, then restart.

```bash
# On the Droplet — temporarily pause the app (does NOT affect DB or Nginx)
docker pause codefolio_web
# Wait 5–10 minutes for UptimeRobot to detect the failure and email you
docker unpause codefolio_web
# Verify health is restored
curl -s https://codefolio2ai.com/health
```

---

### 5.2f — Polling Interval Recommendation

| Monitor | Recommended Interval | Why |
|---|---|---|
| Health check | 5 minutes | Minimum for free tier; sufficient for a portfolio |
| Homepage | 5 minutes | Secondary signal — same interval |
| SSL cert | Daily (built-in) | UptimeRobot SSL monitors check daily by default |

For a production app with business SLA requirements, upgrade to UptimeRobot Pro (1-minute polling) or use a paid alternative (Better Uptime, Checkly). For a portfolio, 5-minute polling is appropriate.

---

## Phase 5.3 — Disaster Recovery Runbook

**Goal:** Know exactly what to do if a container fails, the database is lost or corrupted, or the entire Droplet is gone. Practice this before you need it under pressure.

Three scenarios are covered: (A) the application container fails but the Droplet and database are fine, (B) the database container itself is lost or corrupted, and (C) the entire Droplet is gone.

---

### Scenario A — Application (Web) Container Failure

Use this when `codefolio_web` is crash-looping, unresponsive, or `/health` is failing, but Postgres and Nginx are otherwise healthy. This is the most common and lowest-risk failure mode — no data is at risk, and most of the time a restart resolves it.

**Step 1: Confirm the problem and check logs**

```bash
ssh deploy@YOUR_DROPLET_IP

docker compose -f /home/deploy/codefolio/docker-compose.production.yml ps
# codefolio_web shows Exited, Restarting, or unhealthy

docker compose -f /home/deploy/codefolio/docker-compose.production.yml logs codefolio-web --tail=100
# Look for the actual exception/error near the end of the log
```

**Step 2: Restart just the web container (does not affect Nginx or Postgres)**

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production up -d --no-deps codefolio-web
```

**Step 3: Verify**

```bash
docker compose -f docker-compose.production.yml ps
curl -s https://codefolio2ai.com/health
# Expected: Healthy
```

**If the restart doesn't resolve it:** the logs from Step 1 should point to the actual cause — commonly a bad config value, a missing/rotated secret in `.env.production`, or the previous deploy's image being broken. If the image itself is suspect, redeploy the last known-good image (see Phase 5.4e rollback procedure) rather than repeatedly restarting a broken one.

---

### Scenario B — Database Container Failure (Droplet Intact)

Use this when `codefolio_postgres_prod` is dead, corrupted, or the `pgdata` volume is damaged, but the Droplet is still running.

**Step 1: Confirm the problem**

```bash
ssh deploy@YOUR_DROPLET_IP

docker compose -f /home/deploy/codefolio/docker-compose.production.yml ps
# postgres container shows Exited or Restarting
# OR
docker exec codefolio_postgres_prod psql -U ${POSTGRES_USER} -d ${POSTGRES_DB} -c '\dt'
# Returns connection error
```

**Step 2: Stop the web container (prevents further failed DB connection attempts)**

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml stop codefolio-web
```

**Step 3: Identify the most recent backup**

```bash
ls -lt /opt/backups/codefolio/
# Note the filename at the top — most recent backup
BACKUP_FILE="/opt/backups/codefolio/FILENAME_HERE.sql.gz"
```

**Step 4: Destroy and recreate the postgres container with a fresh volume**

⚠️ This permanently deletes the current pgdata volume. Only do this if you are certain the data is unrecoverable.

```bash
docker compose -f docker-compose.production.yml stop postgres
docker compose -f docker-compose.production.yml rm -f postgres
docker volume rm codefolio_pgdata
```

**Step 5: Start a fresh postgres container**

```bash
docker compose -f docker-compose.production.yml --env-file .env.production up -d postgres

# Wait for healthy
docker compose -f docker-compose.production.yml ps
# postgres should show: Up (healthy)
```

**Step 6: Restore the backup**

```bash
source /home/deploy/codefolio/.env.production

# If backups are encrypted, decrypt first:
openssl enc -d -aes-256-cbc -pbkdf2 -iter 100000 \
    -pass env:BACKUP_ENCRYPTION_KEY \
    -in "${BACKUP_FILE}.enc" -out /tmp/restore.sql.gz

# Restore (uncompressed stream piped into psql):
zcat /tmp/restore.sql.gz \
    | docker exec -i codefolio_postgres_prod \
      psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}"

# Or if not encrypted:
zcat "${BACKUP_FILE}" \
    | docker exec -i codefolio_postgres_prod \
      psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}"
```

**Step 7: Verify the restored database**

```bash
docker exec codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -c '\dt'
# Expected: 12 tables listed

docker exec codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" \
    -c 'SELECT COUNT(*) FROM "AspNetUsers";'
# Expected: 1 (admin user)

docker exec codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" \
    -c 'SELECT COUNT(*) FROM "ResumeSections";'
# Expected: 7 (or however many exist at backup time)
```

**Step 8: Restart the web container**

```bash
docker compose -f docker-compose.production.yml --env-file .env.production up -d codefolio-web

# Monitor startup logs
docker compose -f docker-compose.production.yml logs codefolio-web --tail=30 -f

curl -s https://codefolio2ai.com/health
# Expected: Healthy
```

Clean up temporary file:
```bash
rm -f /tmp/restore.sql.gz
```

---

### Scenario C — Complete VPS Loss (Rebuild from Scratch)

Use this when the Droplet is gone (hardware failure, accidental deletion, account issue). You need an off-site backup for this scenario (Phase 5.1d or 5.1e must be configured).

**Step 1: Provision a new Droplet**

Follow Phase 3, Tasks 6–8 exactly:
- Ubuntu 24.04 LTS, same region as before
- Same SSH key
- Note the new IP address

**Step 2: Point DNS to the new Droplet IP**

In your DNS provider:
```
A  @    <NEW_DROPLET_IP>  TTL 300
A  www  <NEW_DROPLET_IP>  TTL 300
```

⚠️ DNS propagation takes 5–30 minutes. Do not proceed to Certbot until DNS resolves to the new IP.

**Step 3: Run initial server hardening (Phase 3, Tasks 7–8)**

```bash
# Create deploy user, disable root SSH, configure UFW, install fail2ban, install Docker
# (Follow Phase 3 tasks exactly — same commands)
```

**Step 4: Deploy the stack from scratch (Phase 3, Tasks 10–11)**

```bash
# As deploy user on the new Droplet:
mkdir -p /home/deploy/codefolio/nginx

# Transfer deployment files from your local machine:
scp docker-compose.production.yml deploy@NEW_IP:/home/deploy/codefolio/
scp nginx/codefolio.conf deploy@NEW_IP:/home/deploy/codefolio/nginx/

# Transfer the Docker image:
scp codefolio-latest.tar.gz deploy@NEW_IP:/home/deploy/
docker load < /home/deploy/codefolio-latest.tar.gz

# Create .env.production on the new server (copy from a secure backup of your secrets):
nano /home/deploy/codefolio/.env.production
chmod 600 /home/deploy/codefolio/.env.production
```

**Step 5: Start Postgres only (no web container yet)**

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production up -d postgres
docker compose -f docker-compose.production.yml ps
# Wait for postgres to show: Up (healthy)
```

**Step 6: Download the backup from cloud storage and restore**

```bash
source /home/deploy/codefolio/.env.production

# DigitalOcean Spaces:
s3cmd get s3://codefolio-backups/codefolio-backups/LATEST_BACKUP.sql.gz.enc /tmp/restore.sql.gz.enc

# Decrypt and restore:
openssl enc -d -aes-256-cbc -pbkdf2 -iter 100000 \
    -pass env:BACKUP_ENCRYPTION_KEY \
    -in /tmp/restore.sql.gz.enc | zcat \
    | docker exec -i codefolio_postgres_prod \
      psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}"

rm -f /tmp/restore.sql.gz.enc
```

Verify (Step 7 from Scenario B), then run the full Post-Recovery Verification Checklist above.

**Step 7: Obtain new TLS certificate**

```bash
# Confirm DNS has propagated first:
dig +short codefolio2ai.com
# Must return NEW_DROPLET_IP

# Stop nginx (not running yet — this is a fresh deploy, start HTTP first):
# Follow Phase 3 Task 12 exactly.
sudo certbot certonly --standalone -d codefolio2ai.com -d www.codefolio2ai.com \
    --email YOUR_EMAIL --agree-tos --no-eff-email
```

**Step 8: Start the full stack**

```bash
docker compose -f docker-compose.production.yml --env-file .env.production up -d
docker compose -f docker-compose.production.yml ps
curl -s https://codefolio2ai.com/health
```

**Target RTO:** Under 2 hours with a recent off-site backup and this procedure followed step-by-step.

---

### Post-Recovery Verification Checklist (all scenarios)

After any recovery (A, B, or C), confirm the application is fully functional — not just that containers are running:

```bash
# 1. Health endpoint
curl -s https://codefolio2ai.com/health
# Expected: Healthy

# 2. Authentication — log in as the seeded admin via browser at
#    https://codefolio2ai.com/Identity/Account/Login
#    Expected: login succeeds, "Hello, Admin!" and Role: Admin render

# 3. Projects page
curl -s -o /dev/null -w "%{http_code}\n" https://codefolio2ai.com/Project
# Expected: 200

# 4. Blog page
curl -s -o /dev/null -w "%{http_code}\n" https://codefolio2ai.com/BlogPost
# Expected: 200

# 5. Contact form — submit a real test message via browser at
#    https://codefolio2ai.com/Contact, then confirm it persisted:
docker exec codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" \
    -c 'SELECT COUNT(*) FROM "ContactMessages";'
# Expected: count includes the test submission

# 6. AI assistant
curl -s -X POST https://codefolio2ai.com/api/ai/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"What technologies does this developer use?"}'
# Expected: {"success":true,"reply":"...","error":null}
```

Only consider recovery complete once all six checks pass.

---

## Phase 5.4 — CI/CD Pipeline (GitHub Actions)

**Goal:** Push to `main` → Docker image is automatically built → deployed to the Droplet. Manual `scp` and `docker load` steps are eliminated.

**Strategy:** Use GitHub Container Registry (GHCR) — free, integrated with GitHub Actions, no additional account needed.

**Repository-side status:** `.github/workflows/deploy.yml` now has three jobs — `test` (restores, builds, and runs the new `CodeFolio.Tests` project; `build-and-push` and `deploy` will not run if this fails), `build-and-push` (unchanged, includes the `:previous`-tag rollback fix from the original draft), and `deploy` (updated to `docker compose pull` + `up -d`, matching the GHCR image reference below). `docker-compose.production.yml` has also already been updated in the repo to `image: ghcr.io/jamesc-jones/codefolio:latest`. **None of this is active on the live server yet** — pushing to `main` right now would run `test` and `build-and-push` successfully, but the `deploy` job would fail, since `VPS_HOST`/`VPS_SSH_KEY` secrets don't exist yet and the VPS isn't authenticated to GHCR. Do not `scp` the updated `docker-compose.production.yml` to the server, and do not remove the existing manual `build → save → scp → load → compose up` deployment method, until this has been validated end-to-end.

---

### 5.4.0 — Test Gate (already created)

A minimal xUnit project, `CodeFolio.Tests/`, now exists and is registered in `CodeFolio.sln`. It contains two controller-level unit tests, run against EF Core's in-memory provider (no real Postgres needed in CI):

- `ContactControllerTests.Index_Post_WithValidMessage_RedirectsToThankYouAndPersists` — posts a valid `ContactMessage`, asserts a redirect to `ThankYou` and that the message persisted to the in-memory `AppDbContext`
- `ProjectControllerTests.Index_Get_ReturnsViewResult_WithProjectList` — seeds one project, asserts `Index()` returns a `ViewResult` with that project in its model

Both were run locally (`dotnet test CodeFolio.Tests/CodeFolio.Tests.csproj`) and pass. One real gotcha surfaced and was fixed during this: instantiating `ContactController` directly (outside the real MVC pipeline) leaves `TempData` null, since nothing has wired up an `ITempDataProvider` — the controller's `TempData["SentAt"] = ...` line threw a `NullReferenceException` until the test explicitly assigned `controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider())` with a no-op fake provider (`CodeFolio.Tests/Fakes/NullTempDataProvider.cs`). This is a common, well-known unit-testing gotcha for controllers that touch `TempData` — not a defect in `ContactController` itself.

The workflow's new `test` job (`dotnet restore` → `dotnet build` → `dotnet test`) runs first and gates everything downstream: `build-and-push` declares `needs: test`, so a test failure stops the pipeline before an image is ever built or pushed.

---

### 5.4a — The Docker Image Reference (already updated)

`docker-compose.production.yml` in the repo already reads:

```yaml
  codefolio-web:
    image: ghcr.io/jamesc-jones/codefolio:latest
```

**This has not been copied to the live server.** Applying it there before the GHCR login (5.4.2 below) and workflow secrets exist would break the *currently working* manual `build → save → scp → load → compose up` deployment method, since `docker load` populates the local image cache under `codefolio:latest`, not `ghcr.io/jamesc-jones/codefolio:latest`. Only `scp` this file to the server once GHCR login and the workflow secrets are in place and you're ready to cut over.

Replace `YOUR_GITHUB_USERNAME` with your actual GitHub username (lowercase).

Commit this change:

```bash
git add docker-compose.production.yml
git commit -m "chore: point production image to GHCR for CI/CD"
```

**On the Droplet** — configure Docker to authenticate with GHCR using a GitHub Personal Access Token:

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Generate new token → name it `codefolio-ghcr-pull` → expiration 1 year → scopes: `read:packages` only
3. Copy the token

```bash
ssh deploy@YOUR_DROPLET_IP

echo "YOUR_GITHUB_PAT" | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
# Expected: Login Succeeded

# Docker saves credentials to /home/deploy/.docker/config.json
# This persists across reboots — you only need to do this once
```

---

### 5.4b — Add GitHub Repository Secrets

In your GitHub repository → Settings → Secrets and variables → Actions → New repository secret:

| Secret name | Value |
|---|---|
| `VPS_HOST` | Your Droplet's public IP address |
| `VPS_SSH_KEY` | Contents of your **private** SSH key that `deploy` user accepts |

To get your private key:
```bash
# On your LOCAL machine (not the server)
cat ~/.ssh/id_ed25519   # or id_rsa — whichever key matches authorized_keys on the server
```

Paste the entire contents (including `-----BEGIN...` and `-----END...` lines) as the secret value.

---

### 5.4c — The GitHub Actions Workflow (already created)

`.github/workflows/deploy.yml` already exists in the repository with the content below — nothing to create here. This is what's on disk now:

```yaml
name: Build, Test, and Deploy to Production

# Requires GitHub repository secrets before the deploy job can succeed:
#   VPS_HOST     - the droplet's public IP address
#   VPS_SSH_KEY  - private key that the VPS "deploy" user's authorized_keys accepts
# The deploy job hardcodes the SSH username as "deploy" (this project's one
# established convention) rather than reading it from a secret.
# See PHASE_5_MANUAL_PRODUCTION_EXECUTION.md Phase 5.4 for full setup steps.

on:
  push:
    branches:
      - main
  workflow_dispatch:   # allows manual trigger from the GitHub Actions UI

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository_owner }}/codefolio

jobs:
  test:
    name: Build and Test
    runs-on: ubuntu-latest

    steps:
      - name: Checkout source
        uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore
        run: dotnet restore CodeFolio.sln

      - name: Build
        run: dotnet build CodeFolio.sln --no-restore --configuration Release

      - name: Test
        run: dotnet test CodeFolio.Tests/CodeFolio.Tests.csproj --no-build --configuration Release --logger "console;verbosity=normal"

  build-and-push:
    name: Build Docker Image
    runs-on: ubuntu-latest
    needs: test   # will not run if the test job fails
    permissions:
      contents: read
      packages: write   # required to push to GHCR

    steps:
      - name: Checkout source
        uses: actions/checkout@v4

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract image metadata (tags and labels)
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=sha,prefix=,suffix=,format=short
            type=raw,value=latest,enable={{is_default_branch}}

      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: ./CodeFolio
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

  deploy:
    name: Deploy to Production VPS
    runs-on: ubuntu-latest
    needs: build-and-push   # transitively requires test to have passed too
    environment: production   # optional: configure required reviewers in GitHub Environments

    steps:
      - name: Deploy via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.VPS_HOST }}
          username: deploy
          key: ${{ secrets.VPS_SSH_KEY }}
          script: |
            set -euo pipefail

            IMAGE="ghcr.io/${{ github.repository_owner }}/codefolio"

            # Preserve a real rollback target: tag whatever is currently running
            # as ":previous" BEFORE pulling the new image over ":latest".
            CURRENT_ID=$(docker inspect --format='{{.Image}}' codefolio_web 2>/dev/null || echo "")
            if [[ -n "${CURRENT_ID}" ]]; then
              docker tag "${CURRENT_ID}" "${IMAGE}:previous" || true
            fi

            cd /home/deploy/codefolio
            docker compose -f docker-compose.production.yml --env-file .env.production pull codefolio-web

            # Restart only the web container — Nginx and Postgres stay running.
            # up -d is idempotent: safe to re-run even if nothing changed.
            docker compose -f docker-compose.production.yml --env-file .env.production \
              up -d --no-deps codefolio-web

            # Wait for the container to start and verify health
            sleep 10
            HEALTH=$(curl -sf https://codefolio2ai.com/health || echo "FAILED")
            echo "Health check result: ${HEALTH}"

            if [[ "${HEALTH}" != "Healthy" ]]; then
              echo "ERROR: Health check failed after deploy. Rolling back..."
              if docker image inspect "${IMAGE}:previous" > /dev/null 2>&1; then
                docker tag "${IMAGE}:previous" "${IMAGE}:latest"
                docker compose -f docker-compose.production.yml --env-file .env.production \
                  up -d --no-deps codefolio-web
              else
                echo "No :previous image available — manual rollback required (see Phase 5.4e)."
              fi
              exit 1
            fi

            echo "Deploy successful. Health: ${HEALTH}"

            # Clean up dangling images to free disk space
            docker image prune -f
```

This file is already committed once you push (see the Documentation Updates section) — no separate commit step needed here:

```bash
git log --oneline -- .github/workflows/deploy.yml
```

---

### 5.4d — Verify the First Automated Deploy

1. Go to your repository → Actions tab
2. You should see the workflow triggered by your push to `main`
3. Watch the `build-and-push` job build the Docker image and push to GHCR
4. Watch the `deploy` job SSH into the Droplet and run the deploy steps
5. Confirm the workflow shows green (all steps passed)

Verify on the server:

```bash
ssh deploy@YOUR_DROPLET_IP
docker compose -f /home/deploy/codefolio/docker-compose.production.yml ps
curl -s https://codefolio2ai.com/health
```

---

### 5.4e — Rollback Procedure

If a deployment breaks production:

**Immediate rollback — trigger from GitHub Actions UI:**

1. GitHub → Actions → Select the last known-good workflow run → Re-run all jobs
2. This rebuilds and deploys from the commit that previously succeeded

**Manual rollback from the Droplet:**

```bash
ssh deploy@YOUR_DROPLET_IP

# List recent images
docker images ghcr.io/YOUR_USERNAME/codefolio --format "table {{.Tag}}\t{{.CreatedAt}}"

# Tag a known-good SHA as latest and redeploy
docker tag ghcr.io/YOUR_USERNAME/codefolio:GOOD_SHA ghcr.io/YOUR_USERNAME/codefolio:latest
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production \
  up -d --no-deps codefolio-web
```

---

### 5.4f — Protect the `main` Branch (Recommended)

To prevent accidental direct pushes triggering deploys without review:

GitHub → Repository Settings → Branches → Add branch protection rule → Branch name: `main`
- ✅ Require a pull request before merging
- ✅ Require status checks to pass before merging → add `Build Docker Image`
- ✅ Require branches to be up to date before merging

---

## Phase 5.5 — Security Headers

**Goal:** Add three missing HTTP security headers to the Nginx configuration. This requires editing `nginx/codefolio.conf` locally, deploying it to the server, and reloading Nginx.

**⚠️ Warning:** Only one file changes — `nginx/codefolio.conf`. The Nginx config must be tested before reload. Incorrect config syntax causes Nginx to refuse to reload (it stays on the old config), but an invalid `docker exec nginx -t` catch prevents any downtime.

---

### 5.5a — Understanding the Three Missing Headers

**`Strict-Transport-Security` (HSTS)**  
Tells browsers to only ever connect to this domain over HTTPS for a specified period. Once set, even if a user types `http://` the browser converts it to `https://` locally without a server round-trip. Was previously commented out in the Nginx config; the repository-side change below enables it. Applying this to the live server is still a manual VPS step (5.5c).

Value: `max-age=31536000; includeSubDomains`  
Meaning: "Use only HTTPS for the next 365 days, and apply this rule to all subdomains too."  
**Do not add `preload`** until you have verified the site will remain HTTPS permanently — preloading submits your domain to a browser-maintained list that is very difficult to remove from.

**`Content-Security-Policy` (CSP) — compatibility risks and how they were checked**

CSP is the primary defense against cross-site scripting (XSS): even if an attacker injects a `<script>` tag, CSP prevents the browser from executing scripts or loading resources from unauthorized sources. A CSP that's too strict silently breaks pages — every actual resource the site loads was verified against the codebase before writing the policy below, rather than guessed:

- **Razor views / Bootstrap / Font Awesome:** `_Layout.cshtml` loads the Bootstrap JS bundle from `cdn.jsdelivr.net` and the Font Awesome stylesheet + webfonts from `cdnjs.cloudflare.com`. Both are explicitly allow-listed in `script-src` / `style-src` / `font-src` respectively — a naive `'self'`-only policy would have silently broken every icon on the site and the mobile nav toggle.
- **No inline `<script>` blocks exist anywhere in the app** (only an empty `<script type="importmap">` tag and external `<script src="...">` references), so `'unsafe-inline'` is deliberately **omitted** from `script-src` — tightening the policy beyond what's minimally required, with zero functional cost.
- **AI chat widget (`_ChatWidget.cshtml`):** embeds a real inline `<style>` block for its scoped CSS, which is why `style-src` still needs `'unsafe-inline'`. `chat.js` itself is an external file and makes same-origin `fetch()` calls only (to `/api/ai/chat`), covered by `connect-src 'self'` — no additional `connect-src` entries are needed since the Anthropic API call happens server-side, never from the browser.
- **Images:** every `<img>` tag in the app serves from `wwwroot/img/` (local files, even though several were originally sourced from Unsplash) — the Unsplash/GitHub links elsewhere on the site are plain `<a href>` navigation, which CSP doesn't restrict. `img-src 'self' data:` is sufficient; no external image host is needed.

A stricter CSP using nonces or hashes for the widget's inline style is possible but requires ASP.NET Core middleware integration and is outside Phase 5 scope.

**`Permissions-Policy`**  
Explicitly disables browser features this application has no reason to use (camera, microphone, geolocation, payment). This limits the blast radius if a dependency is ever compromised.

---

### 5.5b — nginx/codefolio.conf (already updated locally)

`nginx/codefolio.conf` in the repository has already been updated with the block below — its syntax was validated locally via `docker run --rm -v "$(pwd)/nginx/codefolio.conf:/etc/nginx/conf.d/codefolio.conf:ro" nginx:alpine nginx -t`, which returned `syntax is ok` / `test is successful`. **This local validation is not the same as validating on the live server** — 5.5c below still requires running `nginx -t` inside the actual `codefolio_nginx` container against the real bind-mounted file before any reload.

Open `CodeFolio/../nginx/codefolio.conf` locally (at the solution root). Replace the existing `# Security headers` block in the HTTPS server with the complete updated version below.

**Full updated `nginx/codefolio.conf`** (this is what's actually on disk in the repo now):

```nginx
# Redirect HTTP to HTTPS
server {
    listen 80;
    server_name codefolio2ai.com www.codefolio2ai.com;

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
    server_name codefolio2ai.com www.codefolio2ai.com;

    ssl_certificate     /etc/letsencrypt/live/codefolio2ai.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/codefolio2ai.com/privkey.pem;

    # Strong TLS settings
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 1d;

    # ── Security headers ────────────────────────────────────────────────────

    # HSTS: browsers use HTTPS-only for 1 year, including subdomains.
    # Do not add 'preload' until HTTPS has been unconditionally stable for a long period —
    # preload submits the domain to a browser-maintained list that is very hard to remove from.
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

    # Prevents clickjacking — page cannot be embedded in an <iframe> on another origin.
    add_header X-Frame-Options "SAMEORIGIN" always;

    # Prevents MIME-type sniffing — browser must use the declared Content-Type.
    add_header X-Content-Type-Options "nosniff" always;

    # Controls how much referrer information is sent when following links.
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    # Content Security Policy — sources verified against actual site usage:
    # - script-src: cdn.jsdelivr.net serves the Bootstrap JS bundle; no inline <script> blocks
    #   with real code exist anywhere in the app, so 'unsafe-inline' is deliberately omitted.
    # - style-src/font-src: cdnjs.cloudflare.com serves the Font Awesome stylesheet and webfonts.
    # - style-src keeps 'unsafe-inline' because _ChatWidget.cshtml embeds a real inline <style> block.
    # - img-src: all images are served locally from wwwroot/img — no external image hosts needed.
    # - connect-src 'self': the AI chat widget and contact form only ever call same-origin endpoints;
    #   the Anthropic API call happens server-side, never from the browser.
    add_header Content-Security-Policy "default-src 'self'; script-src 'self' https://cdn.jsdelivr.net; style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; font-src 'self' https://cdnjs.cloudflare.com; img-src 'self' data:; connect-src 'self'; frame-src 'none'; frame-ancestors 'self'; base-uri 'self'; form-action 'self'; object-src 'none';" always;

    # Permissions Policy — explicitly disable browser features this app has no reason to use.
    add_header Permissions-Policy "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()" always;

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

Note the CSP is written as a single-line header value (not the multi-line string in the original draft) — `add_header` values containing literal newlines produce a malformed HTTP header, since HTTP header values cannot legally contain unescaped line breaks.

---

### 5.5c — Deploy the Updated Nginx Config

The Nginx config is bind-mounted into the container from `./nginx/codefolio.conf`. Updating it requires copying the file to the server and reloading Nginx inside the container — **not** restarting the container.

**From your local machine:**

```bash
scp nginx/codefolio.conf deploy@YOUR_DROPLET_IP:/home/deploy/codefolio/nginx/codefolio.conf
```

**On the Droplet — test syntax before reloading:**

```bash
ssh deploy@YOUR_DROPLET_IP

# Test the new config (reads from the bind-mounted path inside the container)
docker exec codefolio_nginx nginx -t
# Expected output:
#   nginx: the configuration file /etc/nginx/nginx.conf syntax is ok
#   nginx: configuration file /etc/nginx/nginx.conf test is successful

# Reload Nginx (applies the new config without dropping connections)
docker exec codefolio_nginx nginx -s reload
# Expected: no output (silent success)
```

**⚠️ If `nginx -t` returns an error:** The config has a syntax problem. Do NOT run `nginx -s reload`. Fix the config file locally, `scp` it again, and re-run `nginx -t` until it passes.

Commit the updated config:

```bash
git add nginx/codefolio.conf
git commit -m "security: enable HSTS, add CSP and Permissions-Policy headers"
git push origin main
```

---

### 5.5d — Verify the Headers

```bash
curl -sI https://codefolio2ai.com | grep -E "strict|content-security|permissions|x-frame|x-content"
```

Expected output (order may vary):

```
strict-transport-security: max-age=31536000; includeSubDomains
x-frame-options: SAMEORIGIN
x-content-type-options: nosniff
referrer-policy: strict-origin-when-cross-origin
content-security-policy: default-src 'self'; ...
permissions-policy: camera=(), microphone=(), ...
```

**Automated scan:**

Run your domain through https://securityheaders.com — target grade **A** (all major headers present). An A+ requires HSTS preload, which is optional.

---

### 5.5e — CSP Troubleshooting

If any page features break after adding the CSP:

1. Open browser DevTools → Console — look for `Content Security Policy` violation messages
2. Each violation message names the blocked resource and the failing directive
3. Adjust the relevant directive in `codefolio.conf` to whitelist the source
4. Re-deploy and re-test

Common fixes needed:
- External CDN fonts: add the CDN domain to `font-src`
- Google Analytics (if added later): add `https://www.googletagmanager.com` to `script-src` and `connect-src`
- Gravatar images (if used in Identity): add `https://www.gravatar.com` to `img-src`

---

## Phase 5.6 — Domain Email Setup

**Goal:** Send and receive email at `contact@codefolio2ai.com`. Update the contact form and SendGrid to send from this address. Configure DNS records to authenticate the sending domain and prevent spoofing.

**Status: requires an external email provider account and live DNS changes — neither is done automatically.** The instructions below are ready to follow; no account has been created and no DNS record has been modified.

---

### 5.6a — Choose an Email Provider

Three options, in order of recommendation:

**Option A: Zoho Mail (Recommended — free for 1 user)**  
- Free tier: 1 user, 5 GB storage, `contact@codefolio2ai.com`
- Includes webmail, mobile app, IMAP/SMTP access
- Free: https://zoho.com/mail → Get Started Free → Add Domain

**Option B: Google Workspace ($6 USD/month)**  
- Professional option if you want Gmail interface and Google Workspace tools
- Best choice if you already use Google products
- Better deliverability reputation than Zoho
- Setup: https://workspace.google.com

**Option C: Cloudflare Email Routing (Free — forwarding only)**  
- Forwards `contact@codefolio2ai.com` to an existing Gmail/other address
- Cannot send FROM the domain address — only receive
- Good if you only need to receive form submissions, not send from the domain
- Requires Cloudflare DNS (your domain must use Cloudflare nameservers)

The tutorial below uses **Zoho Mail** (Option A). Google Workspace setup is nearly identical — Zoho-specific steps are labeled.

---

### 5.6b — Add the Domain to Your Email Provider

**For Zoho Mail:**

1. Sign up at https://zoho.com/mail → Get Started Free
2. Select "Add your existing domain" → enter `codefolio2ai.com`
3. Create your mailbox: `contact` → password → Create Account
4. Zoho will present you with DNS records to add (MX, SPF, DKIM)
5. Keep this browser tab open — you'll add the records in the next step

---

### 5.6c — Configure DNS Records

Add these records in your DNS provider (DigitalOcean DNS or wherever `codefolio2ai.com` is managed).

**MX Record** (routes inbound email to Zoho)

| Type | Name | Value | Priority | TTL |
|---|---|---|---|---|
| MX | `@` | `mx.zoho.com` | 10 | 300 |
| MX | `@` | `mx2.zoho.com` | 20 | 300 |
| MX | `@` | `mx3.zoho.com` | 50 | 300 |

*For Google Workspace, replace with the MX records Google provides — they will look like `ASPMX.L.GOOGLE.COM` etc.*

**SPF Record** (authorizes Zoho servers to send email on behalf of your domain)

| Type | Name | Value | TTL |
|---|---|---|---|
| TXT | `@` | `v=spf1 include:zoho.com ~all` | 300 |

*For Google Workspace:* `v=spf1 include:_spf.google.com ~all`

⚠️ If you are also sending via SendGrid (contact form emails), the SPF record must include both:  
`v=spf1 include:zoho.com include:sendgrid.net ~all`  
An SPF record can only exist once — combine all senders into a single TXT record.

**DKIM Record** (cryptographically signs outgoing email so recipients can verify authenticity)

Zoho will generate a DKIM key and provide a DNS record like this:

| Type | Name | Value |
|---|---|---|
| TXT | `zoho._domainkey` | `v=DKIM1; k=rsa; p=<long-public-key>` |

Copy the exact record Zoho provides — the public key is unique to your account.

**DMARC Record** (policy for what to do with emails that fail SPF or DKIM checks)

| Type | Name | Value | TTL |
|---|---|---|---|
| TXT | `_dmarc` | `v=DMARC1; p=quarantine; rua=mailto:contact@codefolio2ai.com; pct=100` | 300 |

Explanation of the value:
- `p=quarantine` — emails that fail DMARC go to spam (not rejected outright). Start with `quarantine`, not `reject`, until you're sure SPF and DKIM are working.
- `rua=mailto:contact@codefolio2ai.com` — aggregate DMARC reports are sent here (weekly digest of what emails passed/failed)
- `pct=100` — apply this policy to 100% of emails

After 30 days of seeing no failures in your DMARC reports, change `p=quarantine` to `p=reject`.

---

### 5.6d — Verify DNS Records Are Live

DNS propagation takes 5–60 minutes. Verify with:

```bash
# MX records
dig MX codefolio2ai.com +short
# Expected: 10 mx.zoho.com., 20 mx2.zoho.com., 50 mx3.zoho.com.

# SPF record
dig TXT codefolio2ai.com +short | grep spf
# Expected: "v=spf1 include:zoho.com include:sendgrid.net ~all"

# DKIM record
dig TXT zoho._domainkey.codefolio2ai.com +short
# Expected: "v=DKIM1; k=rsa; p=..."

# DMARC record
dig TXT _dmarc.codefolio2ai.com +short
# Expected: "v=DMARC1; p=quarantine; ..."
```

Once MX records are live, test inbound email:

```bash
# Send a test email to contact@codefolio2ai.com from any email account
# Verify it appears in Zoho Mail webmail
```

---

### 5.6e — Authenticate Your Sending Domain in SendGrid

For the contact form to send emails FROM `contact@codefolio2ai.com` (rather than a generic SendGrid address), SendGrid needs to authenticate your domain.

1. SendGrid Dashboard → Settings → Sender Authentication → Authenticate Your Domain
2. Enter domain: `codefolio2ai.com`
3. SendGrid generates three CNAME records — add them all to your DNS

Example CNAME records SendGrid provides:

| Type | Name | Value |
|---|---|---|
| CNAME | `em1234.codefolio2ai.com` | `u1234567.wl.sendgrid.net` |
| CNAME | `s1._domainkey.codefolio2ai.com` | `s1.domainkey.u1234567.wl.sendgrid.net` |
| CNAME | `s2._domainkey.codefolio2ai.com` | `s2.domainkey.u1234567.wl.sendgrid.net` |

*(The actual values are unique to your SendGrid account — copy from the SendGrid dashboard.)*

After adding the records, return to SendGrid and click "Verify". Verification may take up to 1 hour.

---

### 5.6f — Update Application Configuration

Once SendGrid domain authentication is verified, update the `FromEmail` to use your new domain address.

**On the production server** (`/home/deploy/codefolio/.env.production`):

```dotenv
SENDGRID_FROM_EMAIL=contact@codefolio2ai.com
SENDGRID_FROM_NAME=CodeFolio
```

Restart the web container to pick up the new value:

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production \
  up -d --no-deps codefolio-web
```

**Also update `appsettings.Production.json`** in source:

```json
"SendGrid": {
  "ApiKey": "",
  "FromEmail": "contact@codefolio2ai.com",
  "FromName": "CodeFolio"
}
```

Commit:

```bash
git add CodeFolio/appsettings.Production.json
git commit -m "config: update SendGrid FromEmail to contact@codefolio2ai.com"
```

---

### 5.6g — Resolve SendGrid Credit Limit

The current "Maximum credits exceeded" error on the SendGrid account must be resolved before email delivery will work.

**Option 1: Upgrade the SendGrid plan**  
SendGrid's free tier allows 100 emails/day. If the account has exceeded lifetime trial limits:  
SendGrid Dashboard → Settings → Plan and Billing Details → upgrade to Essentials ($19.95/month for 50,000 emails/month)

**Option 2: Use Resend instead of SendGrid** (recommended for a fresh start)  
[Resend](https://resend.com) is a modern SendGrid alternative with a free tier of 3,000 emails/month, a cleaner API, and better developer experience. If switching:

1. Sign up at https://resend.com → Add domain → follow their DNS verification steps
2. Generate an API key
3. Install `Resend` NuGet package: `dotnet add package Resend`
4. Update `EmailSender.cs` to use the Resend client instead of SendGrid client
5. Update `appsettings.json` with a `Resend:ApiKey` placeholder

The `IEmailSender` interface doesn't change — only the `EmailSender` implementation class needs updating, so the contact form and all other callers are unaffected.

---

## Phase 5 Validation Checklist

```
Phase 5.1 — Automated Backups
[ ] /opt/backups/codefolio/ directory created (chmod 700, owned by deploy)
[ ] /home/deploy/backup-db.sh created (chmod 700)
[ ] Manual test: script runs without errors and produces a .sql.gz file
[ ] Manual test: zcat of the backup file produces valid SQL header output
[ ] Cron job registered (crontab -l shows the 3 AM entry)
[ ] (If cloud upload) s3cmd or b2 configured and test upload succeeded
[ ] (If cloud upload) BACKUP_ENCRYPTION_KEY added to .env.production
[ ] Decrypt test passed (can decrypt and read the backup without errors)

Phase 5.2 — Monitoring
[ ] UptimeRobot account created
[ ] Monitor 1: /health endpoint (with keyword "Healthy")
[ ] Monitor 2: homepage
[ ] Monitor 3: SSL certificate expiry (alerts at <30 days)
[ ] All three monitors show green / Up
[ ] Alert email received during downtime test (docker pause codefolio_web)

Phase 5.3 — Disaster Recovery
[ ] Scenario A (app container failure) read and understood
[ ] Scenario B (database container failure) tested: stopped postgres, restored from backup, restarted web
[ ] 12 tables verified present in restored database
[ ] Off-site backup retrieval tested (can download and decrypt a backup from cloud)
[ ] Post-Recovery Verification Checklist (health/auth/projects/blog/contact/AI) run after a test recovery
[ ] Estimated RTO documented (target: under 2 hours)

Phase 5.4 — CI/CD Pipeline
[ ] docker-compose.production.yml updated: image points to ghcr.io/USERNAME/codefolio:latest
[ ] GitHub PAT created (read:packages scope) and docker login to GHCR on Droplet done
[ ] VPS_HOST and VPS_SSH_KEY secrets added to GitHub repository
[ ] .github/workflows/deploy.yml created and committed
[ ] First automated deploy triggered by push to main
[ ] GitHub Actions workflow completed green (all steps passed)
[ ] curl https://codefolio2ai.com/health returned Healthy after automated deploy
[ ] Manual rollback procedure tested (re-tagged a previous image and redeployed)

Phase 5.5 — Security Headers
[ ] nginx/codefolio.conf updated with HSTS, CSP, and Permissions-Policy
[ ] docker exec codefolio_nginx nginx -t returned "syntax is ok"
[ ] docker exec codefolio_nginx nginx -s reload completed silently
[ ] curl -sI https://codefolio2ai.com shows all 6 security headers
[ ] securityheaders.com scan returns grade A or higher
[ ] No CSP violations in browser DevTools console on any page

Phase 5.6 — Domain Email
[ ] Email provider account created (Zoho Mail or Google Workspace)
[ ] MX records added and verified (dig MX codefolio2ai.com shows provider records)
[ ] SPF record added and verified (includes both Zoho/Google AND sendgrid.net)
[ ] DKIM record added and verified
[ ] DMARC record added (p=quarantine)
[ ] Test email received at contact@codefolio2ai.com
[ ] SendGrid domain authentication CNAMEs added and verified in SendGrid dashboard
[ ] .env.production SENDGRID_FROM_EMAIL updated to contact@codefolio2ai.com
[ ] appsettings.Production.json FromEmail updated and committed
[ ] SendGrid credit limit resolved (account upgraded or provider switched)
[ ] Contact form submission delivers email to inbox
```

---

## Post-Phase-5 Git Tag

Once all checklist items pass:

```bash
git add .
git commit -m "feat: Phase 5 production hardening complete"
git tag -a phase-5-production-hardening \
  -m "Phase 5 complete: backups, monitoring, CI/CD, security headers, domain email"
git push origin main --tags
```

---

*Each Phase 5 sub-section is independent. Implement them in the order that matters most to you — 5.1 (backups) and 5.2 (monitoring) are highest priority since data loss and undetected outages are the most likely failure modes.*
