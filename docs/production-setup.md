# PAW API - Production Setup Guide

Quick checklist for deploying PAW API and Paw.Worker to production.

## Prerequisites

- **.NET 8 runtime** installed
- **SQL Server** database (accessible from deployment environment)
- **Polar AccessLink account** (for OAuth and webhooks)
- **Environment secrets management** (e.g., Azure Key Vault, AWS Secrets Manager, or server env vars)

## 1. Database Setup

### SQL Server Connection

1. Ensure SQL Server instance is reachable from production environment.
2. Create a database for PAW (e.g., `QEPTest` or similar).
3. Run Entity Framework migrations:
   ```bash
   dotnet ef database update --project Paw.Infrastructure --startup-project Paw.Api
   ```

### Connection String

Store the connection string securely (e.g., Azure Key Vault or environment variable):

```
Server=<your-server>;Database=<your-db>;User Id=<user>;Password=<password>;MultipleActiveResultSets=True;TrustServerCertificate=True;Encrypt=True;
```

## 2. Polar AccessLink Registration

### Register a Production Client

1. Go to [Polar Accesslink Admin](https://admin.polaraccesslink.com/)
2. Register a new **Client Application**
3. Configure:
   - **Redirect URI**: `https://yourdomain.com/qep/polar/callback`
   - **Webhook URL**: `https://yourdomain.com/webhooks/polar`
   - Note the **Client ID** and **Client Secret**

### Create Webhook Subscription

**Important:** The API has two endpoints for webhook management. Use these to set up Polar's webhook subscription.

#### Create/Setup Webhook

Once the API is deployed and configured with Polar credentials:

```bash
curl -X POST https://yourdomain.com/admin/polar/webhook/setup
```

This endpoint:
- Creates a new webhook subscription with Polar if none exists
- Reactivates the webhook if it's inactive
- Returns the `signatureSecretKey` (only on first creation)

**Expected Response (first creation):**
```json
{
  "message": "Webhook created successfully! IMPORTANT: Update appsettings with the signature_secret_key below.",
  "webhookId": "abdf33",
  "signatureSecretKey": "abe1f3ae-fd33-11e8-8eb2-f2801f1b9fd1",
  "events": ["EXERCISE"],
  "url": "https://yourdomain.com/webhooks/polar"
}
```

**IMPORTANT:** Save the `signatureSecretKey` immediately and store it in your secrets vault. Update `Polar__WebhookSignatureSecret` in your configuration.

**Expected Response (already exists and active):**
```json
{
  "message": "Webhook already exists and is active.",
  "webhookId": "abdf33"
}
```

#### Check Webhook Status

Verify the webhook is active and properly configured:

```bash
curl https://yourdomain.com/admin/polar/webhook/status
```

**Expected Response:**
```json
{
  "data": [
    {
      "id": "abdf33",
      "events": ["EXERCISE"],
      "url": "https://yourdomain.com/webhooks/polar",
      "active": true
    }
  ]
}
```

If `"active": false`, the webhook will be automatically reactivated the next time you call `/admin/polar/webhook/setup`.

### Verify Webhook Endpoint is Accessible

Polar will send a verification ping to your webhook endpoint. Ensure `https://yourdomain.com/webhooks/polar` is:
- Publicly accessible (no firewall blocking)
- Accepting POST requests
- Reachable from Polar's servers

Test with:
```bash
# Should return 200 OK with "Webhook verified" message
curl -X POST https://yourdomain.com/webhooks/polar \
  -H "Content-Type: application/json" \
  -d '{}'
```

---

## 3. Required Configuration

### Environment Variables or appsettings.Production.json

| Key | Source | Example |
|-----|--------|---------|
| `ConnectionStrings__DefaultConnection` | SQL Server instance | `Server=prod-sql.internal;Database=QEPTest;...` |
| `Polar__ClientId` | Polar Admin | `18dc6aa3-00d8-4fc7-9684-f057494924dd` |
| `Polar__ClientSecret` | Polar Admin | `92304b1f-96a6-4206-bd43-e0c745ca65d1` |
| `Polar__RedirectUri` | Your domain | `https://yourdomain.com/qep/polar/callback` |
| `Polar__WebhookUrl` | Your domain | `https://yourdomain.com/webhooks/polar` |
| `Polar__WebhookSignatureSecret` | Polar webhook setup response | `5fff048a-5a42-4654-9b7d-6109eac89d0a` |
| `Jwt__SigningKey` | Generated (min 32 chars) | `your-secure-random-key-at-least-32-characters` |
| `QepApiKey` | Generated secret | `prod-qep-api-key-random-secret` |
| `QepApiKeys__student` | Generated secret | `prod-qep-api-key-student-random` |
| `QepApiKeys__QepFaculty` | Generated secret | `prod-qep-api-key-faculty-random` |
| `QepApiKeys__QepAdministrator` | Generated secret | `prod-qep-api-key-admin-random` |
| `QepWebAppUrl` | Your domain | `https://qep.yourdomain.com` |
| `QepWebAppRedirectUrl` | QEP web app route | `https://qep.yourdomain.com/Qep/User/UserDetails` |
| `CorsOrigins__0` | Frontend URL 1 | `https://yourdomain.com` |
| `CorsOrigins__1` | Frontend URL 2 | `https://qep.yourdomain.com` |

### Generating Secrets

Use a strong random generator for API keys and JWT signing key:

```bash
# Linux/macOS
openssl rand -hex 32

# PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Maximum 256) }))
```

## 4. Configuration Methods

### Option A: Environment Variables (Recommended for containers)

Set environment variables for each required key:

```bash
export ConnectionStrings__DefaultConnection="..."
export Polar__ClientId="..."
export Polar__ClientSecret="..."
# etc.
```

### Option B: appsettings.Production.json (Recommended for IIS/Windows)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=prod-sql.internal;Database=QEPTest;User Id=...;Password=...;"
  },
  "Polar": {
    "ClientId": "18dc6aa3-00d8-4fc7-9684-f057494924dd",
    "ClientSecret": "92304b1f-96a6-4206-bd43-e0c745ca65d1",
    "RedirectUri": "https://yourdomain.com/qep/polar/callback",
    "WebhookUrl": "https://yourdomain.com/webhooks/polar",
    "WebhookSignatureSecret": "5fff048a-5a42-4654-9b7d-6109eac89d0a"
  },
  "Jwt": {
    "SigningKey": "your-secure-random-key-at-least-32-characters"
  },
  "QepApiKey": "prod-qep-api-key-random-secret",
  "QepApiKeys": {
    "student": "prod-qep-api-key-student-random",
    "QepFaculty": "prod-qep-api-key-faculty-random",
    "QepAdministrator": "prod-qep-api-key-admin-random"
  },
  "QepWebAppUrl": "https://qep.yourdomain.com",
  "QepWebAppRedirectUrl": "https://qep.yourdomain.com/Qep/User/UserDetails",
  "CorsOrigins": ["https://yourdomain.com", "https://qep.yourdomain.com"]
}
```

**WARNING:** Do not commit `appsettings.Production.json` with real secrets. Use deployment-time secret injection.

### Option C: Azure Key Vault

1. Create an Azure Key Vault instance.
2. Store each secret with keys matching the config structure (e.g., `Polar-ClientId`).
3. Configure app to load from Key Vault at startup.

## 5. Deploying Paw.Api

### Build for Production

```bash
dotnet build -c Release
```

### Run the API

```bash
# Windows (IIS)
# Publish to IIS and set app pool environment variables or use appsettings.Production.json

# Linux/Docker
dotnet Paw.Api.dll

# Set ASPNETCORE_ENVIRONMENT to load appsettings.Production.json
export ASPNETCORE_ENVIRONMENT=Production
dotnet Paw.Api.dll
```

### Verify API is Running

```bash
curl https://yourdomain.com/swagger
# Should return Swagger UI HTML
```

## 6. Deploying Paw.Worker

The background worker processes pending webhook events every 10 seconds.

### Run the Worker

```bash
export ASPNETCORE_ENVIRONMENT=Production
dotnet Paw.Worker.dll
```

### Worker Configuration

Same configuration keys as Paw.Api (reads from appsettings or environment variables).

Worker requires:
- `ConnectionStrings__DefaultConnection`
- `Polar__ClientId` and `Polar__ClientSecret` (for API calls)

### Monitoring Worker

Worker logs indicate:
- `"Processed {N} Polar webhook events"` — successful batch processing
- `"Error while processing Polar webhook events in worker"` — retry in 10 seconds

## 7. Health Checks

### API Health

```bash
# Should redirect to Swagger
curl -i https://yourdomain.com/
# Expected: 302 redirect

# Swagger docs available
curl -i https://yourdomain.com/swagger
# Expected: 200 OK
```

### Polar Webhook Configuration

Verify webhook is set up and active (run this after initial deployment):

```bash
curl https://yourdomain.com/admin/polar/webhook/setup
# Expected: { "message": "Webhook created successfully...", "webhookId": "...", "signatureSecretKey": "..." }
# OR: { "message": "Webhook already exists and is active.", "webhookId": "..." }
```

Check webhook status:

```bash
curl https://yourdomain.com/admin/polar/webhook/status
# Expected: { "data": [ { "id": "...", "active": true, "events": ["EXERCISE"], "url": "https://yourdomain.com/webhooks/polar" } ] }
```

Verify webhook endpoint is accessible:

```bash
curl -X POST https://yourdomain.com/webhooks/polar \
  -H "Content-Type: application/json" \
  -d '{}'
# Expected: { "message": "Webhook verified" }
```

### Worker Health

Check if worker is processing webhook events. Look for logs containing:
- `"Processed {N} Polar webhook events"` — healthy
- `"Error while processing Polar webhook events in worker"` — check worker logs

## 8. Monitoring & Logging

### Application Logs

Configure centralized logging (e.g., Application Insights, ELK, Splunk):

- API logs contain OAuth flows, sync operations, webhook events.
- Worker logs contain webhook processing status.

### Critical Events to Monitor

- **Webhook signature verification failures** ? May indicate configuration issue or attack.
- **Polar API errors** ? Check rate limiting, token expiry.
- **Database connection failures** ? Check connection string and network.
- **Worker backlog** ? If pending webhook count grows, worker may be slow.

## 9. Security Checklist

- [ ] API keys stored in secret vault, not in code or git
- [ ] Connection string uses encrypted password, not plain text
- [ ] HTTPS enforced on all endpoints
- [ ] CORS origins restricted to your domain(s)
- [ ] JWT signing key is cryptographically random (min 32 chars)
- [ ] Polar webhook signature verified
- [ ] Database backups configured
- [ ] API key rotation plan in place

## 10. Troubleshooting

### Webhook Not Created or Inactive

**Cause:** Polar credentials not configured, or webhook subscription failed.

**Fix:**
1. Verify `Polar__ClientId` and `Polar__ClientSecret` are correct (from Polar Admin).
2. Run webhook setup: `curl -X POST https://yourdomain.com/admin/polar/webhook/setup`
3. Check the response for `signatureSecretKey` (only returned on first creation).
4. Verify webhook status: `curl https://yourdomain.com/admin/polar/webhook/status`

### Webhook Events Not Received

**Cause:** Webhook URL misconfigured, endpoint unreachable, or signature verification failing.

**Fix:**
1. Verify webhook URL in Polar Admin matches exactly: `https://yourdomain.com/webhooks/polar`
2. Test endpoint is reachable: `curl -X POST https://yourdomain.com/webhooks/polar -H "Content-Type: application/json" -d '{}'`
3. Should return: `{ "message": "Webhook verified" }`
4. Check API logs for signature verification errors.
5. Ensure `Polar__WebhookSignatureSecret` matches the value from webhook setup response.

### OAuth Callback Returns 500

**Cause:** Configuration issue (missing `QepWebAppUrl` or `QepWebAppRedirectUrl`).

**Fix:** Verify these config keys match your QEP web app domain.

### Webhooks Not Processing

**Cause:** Worker not running or database connection issue.

**Fix:**
1. Verify worker is running: `ps aux | grep Paw.Worker.dll`
2. Check worker logs for connection errors.
3. Verify webhook events exist in database: `SELECT COUNT(*) FROM WebhookEvents WHERE Status = 'Pending'`
4. If backlog is growing, worker may be overloaded or hitting API rate limits.

### Polar API 401/403

**Cause:** Invalid or expired credentials.

**Fix:**
1. Verify `Polar__ClientId` and `Polar__ClientSecret` match Polar Admin.
2. Check if API credentials were rotated in Polar Admin.
3. Re-setup webhook if needed: `curl -X POST https://yourdomain.com/admin/polar/webhook/setup`

### Database Connection Failures

**Cause:** Connection string incorrect or SQL Server unreachable.

**Fix:**
1. Test connection string: `sqlcmd -S <server> -d <database> -U <username> -P <password>`
2. Verify firewall rules allow connection from deployment environment.
3. Check connection string syntax matches: `Server=<host>;Database=<db>;User Id=<user>;Password=<pass>;...`

## Support

- **Polar API Docs:** https://www.polar.com/en/developers
- **Project Repo:** https://gitlab.com/CIRC/sau/sau-physical-activity-website-device-hub
