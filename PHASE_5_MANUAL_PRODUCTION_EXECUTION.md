# Phase 5 — Manual Production Execution Runbook

> **Status:** Instructions only. Nothing in this document has been executed. No VPS command below has been run by anyone on your behalf, and no Phase 5 task is complete until you've performed it yourself and verified the result.
> **Purpose:** Step-by-step operational runbook for completing the remaining Phase 5 tasks on the DigitalOcean droplet, in the order that minimizes risk.
> **References:** `PHASE_5_PRODUCTION_HARDENING.md` (the source tutorial these steps are drawn from), `CLAUDE.md`, `ROADMAP.md`, `docker-compose.production.yml`, `nginx/codefolio.conf`.

---

## How to read this document

Every sub-phase below is labeled with exactly one of these three states:

| Label | Meaning |
|---|---|
| **✅ Repository-side — already done** | The file/script/workflow already exists in this repo, committed. Nothing left to do here. |
| **🖥️ Manual VPS execution required** | You must SSH into the droplet and run these commands yourself. Not done by anyone automatically. |
| **🌐 External account setup required** | You must create an account with a third-party service (UptimeRobot, an email provider) — not created automatically. |

Replace `YOUR_DROPLET_IP` everywhere it appears with your droplet's actual public IP address.

---

==================================================
## Phase 5.1 — PostgreSQL Backup Installation
==================================================

**Goal:** Protect production PostgreSQL data before making any further infrastructure changes. Do this first — everything else in Phase 5 becomes safer once a tested backup exists.

**✅ Repository-side — already done:** The backup script content below is the exact, finalized version from `PHASE_5_PRODUCTION_HARDENING.md` §5.1b. Nothing to change in the repo for this phase — the script lives only on the server, never in source control.

**🖥️ Manual VPS execution required:** everything in 5.1.1 through 5.1.8.

**Prerequisites:**
- SSH access to the DigitalOcean droplet
- The `deploy` user (not root)
- Docker running on the droplet
- All three production containers (`codefolio_web`, `codefolio_postgres_prod`, `codefolio_nginx`) currently running

---

### 5.1.1 Connect to the VPS

```bash
ssh deploy@YOUR_DROPLET_IP
```

Replace `YOUR_DROPLET_IP` with the droplet's real public IP. All remaining commands in this section run **on the server**, not your local machine, unless explicitly marked otherwise.

---

### 5.1.2 Verify the production environment

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml ps
```

Confirm all three show `Up`:
- `codefolio_postgres_prod` — Up (healthy)
- `codefolio_web` — Up
- `codefolio_nginx` — Up

If any container is not running, resolve that first (see Phase 5.3 Scenario A below) before proceeding — backing up a database whose container is unhealthy may capture a corrupt or incomplete dump.

---

### 5.1.3 Create the backup directory

```bash
sudo mkdir -p /opt/backups/codefolio
sudo chown deploy:deploy /opt/backups/codefolio
chmod 700 /opt/backups/codefolio
```

**Verify:**

```bash
ls -la /opt/backups/
```

Expected: `drwx------  deploy deploy  ...  codefolio`

---

### 5.1.4 Create the backup script

```bash
nano /home/deploy/backup-db.sh
```

Paste exactly (this is the finalized version from `PHASE_5_PRODUCTION_HARDENING.md` §5.1b):

```bash
#!/usr/bin/env bash
# /home/deploy/backup-db.sh
set -euo pipefail

source /home/deploy/codefolio/.env.production

BACKUP_DIR="/opt/backups/codefolio"
CONTAINER="codefolio_postgres_prod"
TIMESTAMP=$(date +%Y_%m_%d_%H%M%S)
FILENAME="codefolio_${TIMESTAMP}.sql.gz"
BACKUP_PATH="${BACKUP_DIR}/${FILENAME}"
RETENTION_DAYS=14

echo "[$(date -Iseconds)] Starting backup: ${BACKUP_PATH}"

docker exec "${CONTAINER}" \
    pg_dump -U "${POSTGRES_USER}" --no-password "${POSTGRES_DB}" \
    | gzip -9 > "${BACKUP_PATH}"

BACKUP_SIZE=$(du -sh "${BACKUP_PATH}" | cut -f1)
echo "[$(date -Iseconds)] Backup complete: ${BACKUP_PATH} (${BACKUP_SIZE})"

if [[ ! -s "${BACKUP_PATH}" ]]; then
    echo "[$(date -Iseconds)] ERROR: Backup file is empty. Aborting." >&2
    rm -f "${BACKUP_PATH}"
    exit 1
fi

find "${BACKUP_DIR}" -name "*.sql.gz" -mtime "+${RETENTION_DAYS}" -delete
echo "[$(date -Iseconds)] Pruned backups older than ${RETENTION_DAYS} days."
echo "[$(date -Iseconds)] Backup job complete."
```

This satisfies every stated requirement: `set -euo pipefail`, `pg_dump` piped through `gzip`, the `codefolio_YYYY_MM_DD_HHMMSS.sql.gz` timestamp format, 14-day retention via `find -mtime +14 -delete`, and an explicit empty-file check that deletes and fails loudly rather than leaving a silently broken backup on disk.

---

### 5.1.5 Test the backup manually

```bash
chmod 700 /home/deploy/backup-db.sh
/home/deploy/backup-db.sh
ls -lh /opt/backups/codefolio/
```

**Expected:** a file named like `codefolio_2026_07_30_150532.sql.gz` exists with a non-zero size, and the script printed `Backup job complete.` with no errors.

---

### 5.1.6 Validate backup contents

```bash
zcat /opt/backups/codefolio/codefolio_*.sql.gz | head -20
```

**Expected:** PostgreSQL dump header lines starting with `--`, e.g.:

```
--
-- PostgreSQL database dump
--
-- Dumped from database version ...
-- Dumped by pg_dump version ...
```

If instead you see binary garbage or an empty result, the backup is not valid — do not proceed to cron scheduling until this step passes.

---

### 5.1.7 Restore verification test

**This test is fully isolated from production data.** It creates a temporary, throwaway database inside the same Postgres container, restores into *that*, verifies it, and drops it. At no point does this touch the live `codefolio` database or any table a real user's data lives in.

```bash
source /home/deploy/codefolio/.env.production

# Create the restore test database
docker exec -i codefolio_postgres_prod \
    createdb -U "${POSTGRES_USER}" codefolio_restore_test

# Restore the dump into it
zcat /opt/backups/codefolio/codefolio_*.sql.gz \
    | docker exec -i codefolio_postgres_prod \
      psql -U "${POSTGRES_USER}" -d codefolio_restore_test

# Verify tables
docker exec codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d codefolio_restore_test -c '\dt'
```

**Expected:** 12 tables listed (Identity tables + `Projects`, `BlogPosts`, `ResumeSections`, `ContactMessages`).

**Clean up the temporary database:**

```bash
docker exec codefolio_postgres_prod \
    dropdb -U "${POSTGRES_USER}" codefolio_restore_test
```

---

### 5.1.8 Configure cron

```bash
crontab -e
```

Add (runs daily at 3:00 AM):

```cron
0 3 * * * /home/deploy/backup-db.sh >> /home/deploy/backup.log 2>&1
```

**Verify the cron entry is registered:**

```bash
crontab -l
```

Expected: the line above appears in the output.

---

### 5.1 Completion Checklist

```
[ ] /opt/backups/codefolio/ exists, chmod 700, owned by deploy
[ ] /home/deploy/backup-db.sh exists, chmod 700
[ ] Manual run (5.1.5) produced a non-empty .sql.gz file
[ ] zcat (5.1.6) showed valid PostgreSQL dump header lines
[ ] Restore test (5.1.7) showed 12 tables in codefolio_restore_test
[ ] Temporary restore-test database was dropped
[ ] crontab -l shows the 3 AM backup entry
```

---

==================================================
## Phase 5.5 — Nginx Security Hardening
==================================================

**Goal:** Apply the HSTS, Content-Security-Policy, and Permissions-Policy headers already prepared in `nginx/codefolio.conf`.

**✅ Repository-side — already done:** `nginx/codefolio.conf` in this repo already contains the updated headers and was syntax-validated locally via a throwaway `nginx:alpine` container (`nginx -t` → `syntax is ok` / `test is successful`). That local validation is **not** the same as validating on the live server — 5.5.3 below still requires running `nginx -t` inside the real `codefolio_nginx` container.

**🖥️ Manual VPS execution required:** everything in 5.5.1 through 5.5.5.

**⚠️ Never run `nginx -s reload` without `nginx -t` passing first.** A syntax error in a config Nginx has already loaded does not take the site down by itself — Nginx keeps serving the last-known-good config until a reload is requested. But reloading with a broken config causes Nginx to refuse the reload, and depending on the exact failure mode, can leave the server without a valid config to fall back to on the next restart. Always test before reloading, never after.

---

### 5.5.1 Back up the currently-live nginx configuration

**Do this on the server, not locally.** The local repo's `nginx/codefolio.conf` is already the *new* version — running `cp` on it locally would just duplicate the new file, giving you nothing to roll back to. The file worth preserving is whatever is *currently live* on the droplet, before it gets overwritten in the next step.

```bash
ssh deploy@YOUR_DROPLET_IP
cp /home/deploy/codefolio/nginx/codefolio.conf /home/deploy/codefolio/nginx/codefolio.conf.backup
```

**Verify:**

```bash
ls -la /home/deploy/codefolio/nginx/
```

Expected: both `codefolio.conf` and `codefolio.conf.backup` exist, and `codefolio.conf.backup` is the *old* config (no `Content-Security-Policy` line yet, HSTS commented out).

---

### 5.5.2 Copy the updated config to the server

**Run this from your local machine**, not the SSH session:

```bash
scp nginx/codefolio.conf deploy@YOUR_DROPLET_IP:/home/deploy/codefolio/nginx/codefolio.conf
```

---

### 5.5.3 Validate inside the Nginx container

**Back on the SSH session:**

```bash
docker exec codefolio_nginx nginx -t
```

**Expected:**

```
nginx: the configuration file /etc/nginx/nginx.conf syntax is ok
nginx: configuration file /etc/nginx/nginx.conf test is successful
```

**If this returns any error instead:** do not proceed to 5.5.4. Restore the backup immediately:

```bash
cp /home/deploy/codefolio/nginx/codefolio.conf.backup /home/deploy/codefolio/nginx/codefolio.conf
docker exec codefolio_nginx nginx -t
# Confirm this passes before moving on — the site is still running the old config throughout
```

Then fix the issue in the local repo file, re-run 5.5.2, and re-test.

---

### 5.5.4 Reload Nginx

**Only run this after 5.5.3 printed "syntax is ok" / "test is successful."**

```bash
docker exec codefolio_nginx nginx -s reload
```

Expected: no output (silent success). This applies the new config without dropping any existing connections and without restarting the container.

---

### 5.5.5 Verify the headers

```bash
curl -I https://codefolio2ai.com
```

Check the response for all five headers:
- `strict-transport-security: max-age=31536000; includeSubDomains`
- `content-security-policy: default-src 'self'; ...`
- `permissions-policy: camera=(), microphone=(), ...`
- `x-frame-options: SAMEORIGIN`
- `x-content-type-options: nosniff`

**Then load the site in an actual browser** and check DevTools → Console for any `Content Security Policy` violation messages. If any resource is blocked, the console message names the exact directive and source to add — see `PHASE_5_PRODUCTION_HARDENING.md` §5.5e for common fixes. Given the CSP was built from the actual resource hosts in use (`cdn.jsdelivr.net` for Bootstrap, `cdnjs.cloudflare.com` for Font Awesome), no violations are expected, but verify rather than assume.

---

### 5.5 Completion Checklist

```
[ ] Live server's old nginx/codefolio.conf backed up as codefolio.conf.backup
[ ] Updated nginx/codefolio.conf copied to the server
[ ] docker exec codefolio_nginx nginx -t returned "syntax is ok" / "test is successful"
[ ] docker exec codefolio_nginx nginx -s reload completed silently
[ ] curl -I shows all 5 security headers
[ ] Browser DevTools console shows no CSP violations on the homepage and the AI chat widget
```

---

==================================================
## Phase 5.2 — Production Monitoring
==================================================

**Goal:** Detect downtime or certificate expiry before users report it — within 5 minutes, not after a visitor emails you.

**🌐 External account setup required:** an UptimeRobot account. Nothing has been created on your behalf.

---

### 5.2.1 Create an UptimeRobot account

1. Go to https://uptimerobot.com → Sign Up (free tier: 50 monitors, 5-minute polling)
2. Verify your email
3. My Settings → Alert Contacts → Add Alert Contact → Email → your email address

---

### 5.2.2 Create the monitors

**Monitor 1 — CodeFolio Health**

| Field | Value |
|---|---|
| Monitor Type | HTTP(s) |
| Friendly Name | `CodeFolio Health` |
| URL | `https://codefolio2ai.com/health` |
| Monitoring Interval | 5 minutes |
| Keyword monitoring | enabled — keyword `Healthy` |
| Alert Contacts | your email |

**Monitor 2 — CodeFolio Homepage**

| Field | Value |
|---|---|
| Monitor Type | HTTP(s) |
| Friendly Name | `CodeFolio Homepage` |
| URL | `https://codefolio2ai.com` |
| Monitoring Interval | 5 minutes |
| Alert Contacts | your email |

**Monitor 3 — SSL Certificate**

| Field | Value |
|---|---|
| Monitor Type | SSL Certificate |
| Friendly Name | `CodeFolio SSL Certificate` |
| Domain | `codefolio2ai.com` |
| Alert when expiring in | fewer than 30 days |

Let's Encrypt certificates auto-renew at under 30 days remaining — an alert here means the automatic renewal failed and needs manual attention.

---

### 5.2.3 Verify monitoring works

Wait 5–10 minutes for the first checks to run, then confirm all three monitors show **Up** / green.

**Optional but recommended — test an actual alert:**

```bash
# On the droplet — temporarily pauses the app without affecting DB or Nginx
docker pause codefolio_web
# Wait 5-10 minutes for UptimeRobot to detect the failure and email you
docker unpause codefolio_web
curl -s https://codefolio2ai.com/health
# Expected: Healthy (confirms recovery)
```

---

### 5.2 Completion Checklist

```
[ ] UptimeRobot account created, alert email configured
[ ] Monitor 1 (Health, keyword "Healthy") created and shows Up
[ ] Monitor 2 (Homepage) created and shows Up
[ ] Monitor 3 (SSL Certificate, <30 day alert) created
[ ] (Optional) Alert email received during a real downtime test
```

---

==================================================
## Phase 5.3 — Disaster Recovery Validation
==================================================

**Perform only Scenario A for now.** This is the lowest-risk recovery test in the runbook — no data is ever at risk, since nothing here touches Postgres or the `pgdata` volume. **Do not perform database destruction testing yet** — that's Scenario B in `PHASE_5_PRODUCTION_HARDENING.md` §5.3, and should only be attempted once a verified backup exists (Phase 5.1 above) and ideally against a non-production test setup first.

**🖥️ Manual VPS execution required.**

**Scenario:** Application (web) container failure — the most common, most recoverable failure mode.

---

### 5.3.1 Check current status

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml ps
```

Confirm all three containers currently show `Up` before intentionally breaking anything.

---

### 5.3.2 Stop the application container

```bash
docker stop codefolio_web
```

---

### 5.3.3 Verify the failure

```bash
curl -i https://codefolio2ai.com/health
```

**Expected:** a connection error, a 502/503 from Nginx, or a timeout — confirming the site is actually down and Nginx has nothing healthy to proxy to. This step matters: if `/health` somehow still returns 200 here, something is wrong with the test itself (e.g., a stale cached response), not a real recovery.

---

### 5.3.4 Recover

```bash
docker compose -f docker-compose.production.yml --env-file .env.production up -d --no-deps codefolio-web
```

---

### 5.3.5 Verify recovery

```bash
curl -s https://codefolio2ai.com/health
```

**Expected:** `Healthy`

---

### 5.3.6 Full application-level browser verification

A restarted container being "Up" isn't the same as the application actually working end-to-end. Verify each of the following in a real browser against `https://codefolio2ai.com`:

| Check | Expected result |
|---|---|
| Login | `/Identity/Account/Login` loads; seeded admin credentials succeed; "Hello, Admin!" renders |
| Projects | `/Project` returns 200 and lists existing projects |
| Blog | `/BlogPost` returns 200 and lists existing posts |
| Contact | `/Contact` form loads; a real test submission persists (spot-check via `psql ... SELECT COUNT(*) FROM "ContactMessages"`) |
| AI Assistant | The chat widget opens, a message sends, and a real Claude-generated reply renders |

Only consider Scenario A fully validated once all five checks pass, not just the `/health` curl.

---

### 5.3 Completion Checklist

```
[ ] docker stop codefolio_web executed
[ ] Failure confirmed via curl (non-200 / connection error)
[ ] Container recovered via --no-deps codefolio-web
[ ] /health returned Healthy after recovery
[ ] Login verified in browser
[ ] Projects page verified
[ ] Blog page verified
[ ] Contact form submission verified (persisted to DB)
[ ] AI assistant verified end-to-end
```

---

==================================================
## Phase 5.4 — CI/CD Cutover
==================================================

**This is the highest-risk task in Phase 5** — it's the only one that changes how deployment itself works. Everything above this point is additive or reversible with a simple restart; this one changes the mechanism you'd rely on if something later goes wrong.

**✅ Repository-side — already done:** `.github/workflows/deploy.yml` exists in the repo and is YAML-valid. It is **not active** — pushing to `main` right now would run the build job successfully (it only needs the automatically-provided `GITHUB_TOKEN`), but the deploy job would fail immediately, since the required secrets don't exist yet and `docker-compose.production.yml` still points at a locally-loaded image, not GHCR.

**Before starting, verify:**
- [ ] You've decided to use GHCR (the workflow is written for it — switching registries would mean rewriting it)
- [ ] You're ready to create GitHub repository secrets
- [ ] You have the droplet's SSH private key available locally (the one `deploy`'s `authorized_keys` accepts)
- [ ] You've read `.github/workflows/deploy.yml` in full and understand what it does
- [ ] You have a rollback plan (below) in mind before the first real run

**Current deployment (still the authoritative method until this is validated):**
```
docker build → docker save → scp → docker load → docker compose up -d --no-deps codefolio-web
```

**Future deployment (once validated):**
```
git push origin main → GitHub Actions builds → pushes to GHCR → SSHes to VPS → pulls image → restarts web container → health check
```

**Do not remove the current method until the future one has been proven to work at least once.**

---

### 5.4.1 Create GitHub repository secrets

Repository → Settings → Secrets and variables → Actions → New repository secret:

| Secret name | Value |
|---|---|
| `VPS_HOST` | The droplet's public IP address |
| `VPS_SSH_KEY` | The full contents of the **private** key `deploy`'s `authorized_keys` accepts (`cat ~/.ssh/id_ed25519` or equivalent, on your **local** machine — never the server) |

**Note on `VPS_USERNAME`:** the actual workflow file hardcodes `username: deploy` rather than reading it from a secret — this project has always used exactly one deploy user (`deploy`), so there's nothing to parameterize. Add a `VPS_USERNAME` secret (and update the workflow to reference `${{ secrets.VPS_USERNAME }}`) only if you anticipate the deploy username ever changing; it isn't required for this to work as-is.

---

### 5.4.2 Configure GHCR authentication on the VPS

1. GitHub → Settings → Developer settings → Personal access tokens (classic) → Generate new token → scope: `read:packages` only, name it something identifiable, set an expiration
2. On the droplet:

```bash
ssh deploy@YOUR_DROPLET_IP
echo "YOUR_GITHUB_PAT" | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
```

Expected: `Login Succeeded`. This persists in `/home/deploy/.docker/config.json` across reboots — a one-time setup.

**Never paste the PAT directly into a command that would appear in shell history unencrypted for long** — the `--password-stdin` form above avoids putting it in `docker login`'s argument list.

---

### 5.4.3 Update docker-compose.production.yml — only after 5.4.1 and 5.4.2 are done

**Not yet applied — do this last, deliberately.** Changing the image reference before GHCR auth and secrets exist would break the *current, working* manual deploy method the moment anyone next runs it, since `docker load` populates the local image cache under `codefolio:latest`, not `ghcr.io/.../codefolio:latest`.

When ready:

```yaml
  codefolio-web:
    image: ghcr.io/YOUR_GITHUB_USERNAME/codefolio:latest
```

Commit this change only once you're prepared to fully cut over.

---

### 5.4.4 Run the first workflow manually

GitHub → Actions tab → select "Build and Deploy to Production" → Run workflow (uses the `workflow_dispatch` trigger already in the file, so you don't have to risk a real `git push` for the first attempt).

Watch both jobs:
- `build-and-push` — builds the image, pushes to GHCR
- `deploy` — SSHes in, tags the currently-running image as `:previous` (so a real rollback target exists if this fails), pulls the new image, restarts only `codefolio-web`, checks `/health`

---

### 5.4.5 Verify

```bash
curl -s https://codefolio2ai.com/health
```
Then in a browser: homepage loads, and confirm the AI assistant still works (a chat round-trip is also an implicit database-connectivity check, since it depends on `AppDbContext` for the dynamic system prompt).

**Database persistence check** (confirms the restart didn't somehow affect Postgres, which it shouldn't since `--no-deps` only touches the web container):

```bash
docker exec codefolio_postgres_prod \
    psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -c '\dt'
# Expected: same 12 tables as before
```

---

### 5.4.6 Test rollback

Intentionally verify the rollback path works before you need it for real. Easiest method — re-run a previous successful workflow from the Actions UI. Manual alternative (see `PHASE_5_PRODUCTION_HARDENING.md` §5.4e / this file's Phase 5.3 pattern):

```bash
docker images ghcr.io/YOUR_USERNAME/codefolio --format "table {{.Tag}}\t{{.CreatedAt}}"
docker tag ghcr.io/YOUR_USERNAME/codefolio:GOOD_SHA ghcr.io/YOUR_USERNAME/codefolio:latest
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production up -d --no-deps codefolio-web
```

---

### 5.4 Completion Checklist

```
[ ] VPS_HOST and VPS_SSH_KEY secrets created in GitHub
[ ] GHCR docker login succeeded on the VPS
[ ] docker-compose.production.yml updated to the GHCR image reference
[ ] First manual workflow run completed green (both jobs)
[ ] /health returned Healthy after the automated deploy
[ ] Homepage and AI assistant verified working
[ ] Database table count unchanged (12 tables)
[ ] Rollback tested and confirmed working
```

---

==================================================
## Phase 5.6 — Domain Email Setup
==================================================

**Keep this last** — it's independent of every other Phase 5 task and touches live DNS, which has its own propagation delay and risk profile separate from the application itself.

**🌐 External account setup + live DNS changes required.**

**Goal:** Replace the SendGrid account currently blocked by its credit limit with a working `contact@codefolio2ai.com` mailbox and authenticated sending domain.

---

### 5.6.1 Choose a provider

- Google Workspace
- Microsoft 365
- Zoho Mail (used as the worked example in `PHASE_5_PRODUCTION_HARDENING.md` §5.6)

---

### 5.6.2 Configure DNS records

Whichever provider you choose will give you exact values for:
- **MX** — routes incoming mail to the provider
- **SPF** — must include both the provider *and* `sendgrid.net`, since SendGrid remains the outbound sender for the contact form
- **DKIM** — cryptographically signs outbound mail
- **DMARC** — tells receiving servers what to do with mail that fails SPF/DKIM

See `PHASE_5_PRODUCTION_HARDENING.md` §5.6 for the exact `dig` verification commands and expected record values per provider.

---

### 5.6.3 Update application email configuration

- `.env.production`: update `SendGrid__FromEmail` to `contact@codefolio2ai.com`
- `appsettings.Production.json`: update the `FromEmail` placeholder to match, commit the change
- SendGrid dashboard: complete domain authentication (adds CNAME records pointing back to SendGrid)
- Resolve the existing SendGrid "Maximum credits exceeded" account limit — upgrade the plan or confirm the new billing status

---

### 5.6.4 Test

1. Send a real email to `contact@codefolio2ai.com` from any external account — confirm it arrives in the provider's webmail
2. Submit the live contact form at `https://codefolio2ai.com/Contact`
3. Confirm the submission both **persists to the database** (`ContactMessages` table) and **delivers an email**, closing out the known Phase 2/3 limitation

---

### 5.6 Completion Checklist

```
[ ] Email provider account created
[ ] MX record added and verified (dig MX codefolio2ai.com)
[ ] SPF record added and verified (includes provider AND sendgrid.net)
[ ] DKIM record added and verified
[ ] DMARC record added (p=quarantine or stricter)
[ ] Test email received at contact@codefolio2ai.com
[ ] SendGrid domain authentication CNAMEs verified
[ ] .env.production / appsettings.Production.json FromEmail updated
[ ] SendGrid credit limit resolved
[ ] Contact form submission delivers email AND persists to DB
```

---

==================================================
## Phase 5 Completion Criteria
==================================================

Phase 5 is complete only when every item below is true — not before:

```
[ ] PostgreSQL backups running daily
[ ] Restore test completed
[ ] Nginx security headers active
[ ] Monitoring alerts configured
[ ] Disaster recovery scenario tested
[ ] CI/CD deployment validated
[ ] Rollback tested
[ ] Domain email working
```

As of this document's creation, **none of these are checked.** Repository-side preparation (the backup script content, the DR runbook, the CI/CD workflow file, and the updated Nginx config) is committed — see `PHASE_5_PRODUCTION_HARDENING.md` and this document's per-section "✅ Repository-side" notes for exactly what that does and doesn't cover.
