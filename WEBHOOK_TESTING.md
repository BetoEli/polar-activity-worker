# Polar Webhook MVP Testing Guide

Guide for setting up and testing the Polar webhook integration.

## Prerequisites

1. **ngrok running** - Your ngrok tunnel should be active and pointing to localhost:5293
2. **Database running** - MySQL should be running (via `make docker-up`)
3. **Migrations applied** - Run `make ef-update-db` to create the WebhookEvents table

## Step 1: Start the API

```bash
make run-api
```

The API should start on `http://localhost:5293`

## Step 2: Setup the Webhook

Call the webhook setup endpoint to create a webhook with Polar:

```bash
curl -X POST http://localhost:5293/admin/polar/webhook/setup
```

**Expected Response:**
```json
{
  "message": "Webhook created successfully! IMPORTANT: Update appsettings with the signature_secret_key below.",
  "webhookId": "abdf33",
  "signatureSecretKey": "abe1f3ae-fd33-11e8-8eb2-f2801f1b9fd1",
  "events": ["EXERCISE"],
  "url": "https://dodecahedral-maxima-semiboiled.ngrok-free.dev/webhooks/polar"
}
```

**IMPORTANT:** Copy the `signatureSecretKey` from the response!

## Step 3: Update Configuration

Update `Paw.Api/appsettings.Development.json` with the signature secret:

```json
"Polar": {
  "WebhookSignatureSecret": "abe1f3ae-fd33-11e8-8eb2-f2801f1b9fd1"
}
```

## Step 4: Verify Webhook Status

Check that the webhook is active:

```bash
curl http://localhost:5293/admin/polar/webhook/status
```

**Expected Response:**
```json
{
  "data": [
    {
      "id": "abdf33",
      "events": ["EXERCISE"],
      "url": "https://dodecahedral-maxima-semiboiled.ngrok-free.dev/webhooks/polar",
      "active": true
    }
  ]
}
```

## Step 5: Test Webhook Endpoint

### Test Ping (Verification)

When Polar creates a webhook, it sends a ping with an empty body:

```bash
curl -X POST http://localhost:5293/webhooks/polar \
  -H "Content-Type: application/json" \
  -d '{}'
```

**Expected Response:**
```json
{
  "message": "Webhook verified"
}
```

### Test Exercise Event

Simulate a real exercise webhook event:

```bash
curl -X POST http://localhost:5293/webhooks/polar \
  -H "Content-Type: application/json" \
  -d '{
    "event": "EXERCISE",
    "user_id": 2278512,
    "entity_id": "aQlC83",
    "timestamp": "2025-11-20T14:22:24Z",
    "url": "https://www.polaraccesslink.com/v3/exercises/aQlC83"
  }'
```

**Expected Response:**
```json
{
  "message": "Webhook received and stored"
}
```

## Step 6: Verify Database

Check that the webhook event was stored in the database:


You should see:
- `Provider`: 1 (Polar)
- `EventType`: "EXERCISE"
- `ExternalUserId`: 2278512
- `EntityId`: "aQlC83"
- `Status`: "Pending"
- `RawPayload`: The full JSON payload

## Real-World Flow

Once a user connects their Polar account:

1. **User uploads workout** to Polar Flow
2. **Polar sends webhook** to ngrok URL
3. **API receives** the webhook and stores it in `WebhookEvents`
4. **Background worker** (to be implemented) processes pending events:
   - Finds the corresponding `DeviceAccount` using `ExternalUserId`
   - Calls Polar API to fetch exercise details using `EntityId`
   - Stores the activity in the `Activities` table
   - Updates webhook event status to "Completed"

## Troubleshooting

### Webhook Already Exists

If you get a 409 Conflict when creating the webhook, it already exists. Check status:

```bash
curl http://localhost:5293/admin/polar/webhook/status
```

### Webhook Inactive

If the webhook shows `"active": false`, reactivate it:

```bash
curl -X POST http://localhost:5293/admin/polar/webhook/setup
```

This will automatically activate it if inactive.

### Signature Verification Fails

Ensure:
1. You copied the correct `signature_secret_key` from the creation response
2. You updated `appsettings.Development.json`
3. You restarted the API after updating the config

### No Events Received

Check:
1. ngrok tunnel is running
2. The webhook URL in Polar matches ngrok URL
3. Check API logs for incoming requests

## Next Steps

1. **Implement background processor** to fetch exercise details when webhook arrives
2. **Add retry logic** for failed webhook processing
3. **Add monitoring** to track webhook health

## Testing with Real Data

To test with actual Polar data:

1. Complete the OAuth flow to connect a Polar account
2. Upload a workout to Polar Flow
3. Watch the API logs for incoming webhook events
4. Verify the event is stored in the database
5. Manually process it by calling the sync endpoint (once implemented)