# Phase 6 — Manual Production Execution Runbook

> **Status:** Instructions only. Nothing in this document has been executed against the production droplet. No VPS command below has been run on your behalf, and neither Phase 6 reliability item is actually live until you've performed these steps yourself and verified the result.
> **Purpose:** Step-by-step operational runbook for rolling out the two Phase 6 production-reliability changes — persistent DataProtection keys and webroot-mode Certbot renewal — with zero downtime.
> **References:** `Program.cs`, `docker-compose.production.yml`, `nginx/codefolio.conf`, `ROADMAP.md`, `CLAUDE.md`.

---

## How to read this document

| Label | Meaning |
|---|---|
| **✅ Repository-side — already done** | The code/config change already exists in this repo, committed. Nothing left to do here. |
| **🖥️ Manual VPS execution required** | You must SSH into the droplet and run these commands yourself. |

Replace `YOUR_DROPLET_IP` with the droplet's real public IP. All commands below run **on the server** (`ssh deploy@YOUR_DROPLET_IP`) unless explicitly marked as local.

**Important — what CI/CD does and does not do for this change:** pushing to `main` triggers `.github/workflows/deploy.yml`, which rebuilds the image and runs `docker compose ... up -d --no-deps codefolio-web` **using whatever `docker-compose.production.yml` already exists on the VPS**. The workflow does not copy the updated compose file to the server. That means:
- The new `Program.cs` (DataProtection code) ships automatically on the next push-to-main deploy.
- The new volume mounts in `docker-compose.production.yml` (`dataprotection-keys`, the `/var/www/certbot` bind mount) do **not** take effect until you manually update the compose file on the VPS and re-run `docker compose up -d` yourself, per 6.1.2 and 6.2.2 below.

Until you do that, the app will still run fine — DataProtection will just write keys to the container's writable layer instead of the named volume, which does not survive a full `up -d` recreation (only a plain restart).

---

==================================================
## Phase 6.1 — Persistent DataProtection Keys
==================================================

**Goal:** Stop every container restart/redeploy from generating a fresh DataProtection key ring, which silently invalidates all existing login cookies and forces every logged-in user (including you, the admin) to re-authenticate.

**✅ Repository-side — already done:**
- `Program.cs` now calls `AddDataProtection().SetApplicationName("CodeFolio").PersistKeysToFileSystem(new DirectoryInfo("/app/keys"))`, gated to `!builder.Environment.IsDevelopment()` so local `dotnet run` behavior is unchanged.
- `docker-compose.production.yml` adds a `dataprotection-keys` named Docker volume mounted at `/app/keys` on the `codefolio-web` service. It is a Docker-managed volume — never bind-mounted to a host path, never committed to source control, and not reachable from outside the container.

**🖥️ Manual VPS execution required:** 6.1.1 through 6.1.4.

### 6.1.1 Pull the updated repository files to the VPS

The VPS's `/home/deploy/codefolio/docker-compose.production.yml` is a separate copy from the one in this repo (see the note at the top of this document). Update it — via `git pull` if the server has its own clone, or `scp docker-compose.production.yml deploy@YOUR_DROPLET_IP:/home/deploy/codefolio/` from your local machine:

```bash
cd /home/deploy/codefolio
# confirm the file now contains "dataprotection-keys:/app/keys" under codefolio-web
grep -A1 "app-logs:/app/logs" docker-compose.production.yml
```

### 6.1.2 Recreate only the app container to pick up the new volume

```bash
docker compose -f docker-compose.production.yml --env-file .env.production up -d --no-deps codefolio-web
```

This is the same command the CI/CD pipeline already runs on every deploy — Nginx and Postgres are untouched.

### 6.1.3 Verify the key ring persists across a restart

```bash
docker exec codefolio_web ls -la /app/keys
# note the key filename, e.g. key-xxxxxxxx-....xml

docker restart codefolio_web
sleep 5
docker exec codefolio_web ls -la /app/keys
# confirm the SAME filename is still present — no new key file was generated
```

### 6.1.4 Verify existing authentication still works

Log in as the seeded admin at `https://codefolio2ai.com/Identity/Account/Login` **before** step 6.1.3's restart, then confirm the session cookie is still valid **after** the restart (no forced re-login). This is the actual behavior the fix targets.

---

==================================================
## Phase 6.2 — Certbot Webroot Renewal (Zero-Downtime)
==================================================

**Goal:** Stop certificate renewal from requiring Nginx to briefly stop (standalone mode rebinds port 80). Switch to webroot mode, which lets Nginx keep running and simply serve the ACME challenge file Certbot writes to disk.

**✅ Repository-side — already done:**
- `nginx/codefolio.conf` already serves `location /.well-known/acme-challenge/ { root /var/www/certbot; }` on the port-80 server block (was already in place from the original Certbot issuance in Phase 3 but unused until now).
- `docker-compose.production.yml`'s `nginx` service now bind-mounts the **host** directory `/var/www/certbot` directly (`/var/www/certbot:/var/www/certbot`) instead of a Docker-managed named volume. This is the change that makes webroot mode possible: the host's `certbot` process and the Nginx container must write to / read from the identical physical directory, which a named volume does not expose at a predictable host path.

**🖥️ Manual VPS execution required:** 6.2.1 through 6.2.6.

### 6.2.1 Create the host webroot directory

```bash
sudo mkdir -p /var/www/certbot
```

### 6.2.2 Update the compose file and recreate only the Nginx container

(Same file-sync note as 6.1.1 — make sure the VPS copy of `docker-compose.production.yml` matches this repo before continuing.)

```bash
cd /home/deploy/codefolio
docker compose -f docker-compose.production.yml --env-file .env.production up -d --no-deps nginx
```

Nginx is briefly recreated here (a few hundred ms, not a renewal event) — this is unrelated to the standalone-vs-webroot issue and does not require stopping the container first.

### 6.2.3 Confirm Nginx actually serves the challenge path

```bash
echo "webroot-test-ok" | sudo tee /var/www/certbot/test.txt
curl -s http://codefolio2ai.com/.well-known/acme-challenge/test.txt
# Expected output: webroot-test-ok
sudo rm /var/www/certbot/test.txt
```

If this doesn't return `webroot-test-ok`, stop here — do not proceed to 6.2.4 until it does (it means the bind mount or Nginx location block isn't wired correctly, and switching the renewal method now would break the next renewal).

### 6.2.4 Convert the renewal configuration from standalone to webroot

```bash
sudo cat /etc/letsencrypt/renewal/codefolio2ai.com.conf
```

Find the `[renewalparams]` section. Change:

```ini
authenticator = standalone
```

to:

```ini
authenticator = webroot
webroot_path = /var/www/certbot,
```

(Edit with `sudo nano` or `sudo vi` — do not run `certbot certonly` again here; that would reissue rather than reuse the existing certificate. Editing the renewal conf in place is Certbot's documented way to change the authenticator for future renewals only.)

### 6.2.5 Dry-run the renewal

```bash
sudo certbot renew --dry-run
```

Expected: `Congratulations, all simulated renewals succeeded`, and critically — no mention of stopping any service, since webroot mode never touches port 80 bindings. Nginx should remain `Up` in `docker compose ps` throughout.

### 6.2.6 Confirm the deploy hook still reloads Nginx after a real renewal

The existing hook at `/etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh` (from Phase 3) already runs `docker exec codefolio_nginx nginx -s reload` after any successful renewal — webroot mode doesn't change that; it only changes how the challenge is satisfied, not what happens afterward. No changes needed here, just confirm the file still exists and is executable:

```bash
ls -la /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
```

---

## Post-execution validation checklist

Run these after completing both sections above:

```bash
# On the VPS
docker compose -f docker-compose.production.yml --env-file .env.production config   # validates the compose file itself
nginx -t 2>&1 || docker exec codefolio_nginx nginx -t                                # validate config from inside the running container
sudo certbot renew --dry-run
docker compose -f docker-compose.production.yml --env-file .env.production ps       # all three containers Up
```

```bash
# From anywhere with internet access
curl -s https://codefolio2ai.com/health        # expect: Healthy
curl -sI https://codefolio2ai.com/ | head -1    # expect: HTTP/2 200
```

Also manually verify in a browser:
- Homepage loads
- Admin login still works
- No unexpected re-authentication after the container restart in 6.1.3

Once both sections are executed and verified, update `ROADMAP.md`'s Phase 6 table to mark them ✅ with the real dates and results — the same convention used for every prior phase in this repo.
