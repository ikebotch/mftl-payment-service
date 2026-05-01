# MFTL Payment Service

## Provider Mapping

Collections should continue to initiate payments through `mftl-payment-service`; provider-specific checkout and webhook logic belongs here.

Current mapping:

- `momo`, `moolre` -> Moolre
- `bank`, `bank_debit`, `direct-debit`, `direct_debit`, `gocardless` -> GoCardless
- `mollie` -> Mollie
- `stripe` -> Stripe
- `paystack` -> Paystack
- `card` -> Mollie only when `Payments:CardProvider=Mollie` and `PaymentProviders:Mollie:Enabled=true`
- `card` -> Stripe only when `Payments:CardProvider=Stripe` or `Payments:AllowStripeCardFallback=true`

Unknown payment methods should be rejected by Collections instead of silently falling back to Stripe.

## GoCardless Sandbox Setup

GoCardless is a Phase 2 provider for hosted bank-payment flows. Provider webhook handling stays inside `mftl-payment-service`; Collections only receives the existing signed internal callback payload.

Safe local configuration example:

```json
{
  "PaymentProviders": {
    "GoCardless": {
      "Enabled": true,
      "Environment": "Sandbox",
      "AccessToken": "replace-with-sandbox-token",
      "WebhookSecret": "replace-with-dashboard-webhook-secret",
      "RedirectBaseUrl": "https://localhost:5173/payments/gocardless/return",
      "WebhookPath": "/callback/transactions/gocardless"
    }
  }
}
```

Do not commit real access tokens or webhook secrets.

To test the sandbox flow:

1. Create a GoCardless sandbox access token.
2. Configure the webhook endpoint in the GoCardless dashboard to point at `/callback/transactions/gocardless`.
3. Set the dashboard webhook secret in `PaymentProviders:GoCardless:WebhookSecret`.
4. Start `mftl-payment-service`.
5. Initiate a payment with `provider: "GoCardless"`, `currency: "GBP"` or `"EUR"`, and the usual Collections `tenantId`, `contributionId`, and `externalReference`.
6. Open the returned checkout URL and complete the hosted Billing Request Flow.
7. Confirm the GoCardless webhook marks the payment terminal in payment-service and creates one signed callback delivery for Collections.

The initial implementation supports one-off hosted Billing Request Flow payments. Future recurring Direct Debit support should reuse the same provider boundary and add mandate/subscription-specific mapping in payment-service only.

## Mollie Sandbox Setup

Mollie is the Phase 2 hosted card checkout provider. The initial implementation uses the Mollie Payments API with `method: "creditcard"` and verifies classic webhooks by fetching the payment from Mollie before applying status.

Safe local configuration example:

```json
{
  "Payments": {
    "CardProvider": "Mollie",
    "AllowStripeCardFallback": false
  },
  "PaymentProviders": {
    "Mollie": {
      "Enabled": true,
      "Environment": "Test",
      "ApiKey": "replace-with-mollie-test-api-key",
      "RedirectBaseUrl": "https://localhost:5173/payments/mollie/return",
      "WebhookBaseUrl": "https://example-ngrok-domain.ngrok-free.app",
      "WebhookPath": "/callback/transactions/mollie",
      "WebhookVerificationMode": "FetchPayment"
    }
  }
}
```

Do not commit real Mollie API keys. Put local values in `PaymentService/appsettings.Development.json`, user secrets, or environment variables.

Hosted checkout flow:

1. Configure a Mollie test API key.
2. Set `PaymentProviders:Mollie:WebhookBaseUrl` to a public HTTPS tunnel such as ngrok.
3. Start `mftl-payment-service`.
4. Initiate a `card` payment with `Payments:CardProvider=Mollie`, or initiate directly with provider `Mollie`.
5. Open the returned checkout URL and complete a Mollie test card payment.
6. Mollie posts the payment id to `/callback/transactions/mollie`.
7. Payment service fetches `/v2/payments/{id}`, validates amount, currency, and metadata, then creates one signed callback delivery to Collections for terminal statuses.

Webhook test flow:

1. Run `ngrok http <payment-service-port>`.
2. Set `PaymentProviders:Mollie:WebhookBaseUrl` to the ngrok HTTPS URL.
3. Initiate a test card payment and complete, fail, cancel, or let it expire in Mollie.
4. Confirm `paid` produces one `PaymentSucceeded` callback and `failed`, `canceled`, or `expired` produces one non-success callback.
5. Re-post the same webhook id to confirm duplicate webhooks do not create duplicate callback deliveries.
