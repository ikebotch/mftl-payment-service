-- MftlPaymentService database schema
-- Flow modeled: initiate collection -> complete collection -> check status -> optional disbursement

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS payments;

-- Keep updated_at in sync automatically.
CREATE OR REPLACE FUNCTION payments.set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$;

-- Main transaction record for a payment/disbursement lifecycle.
CREATE TABLE IF NOT EXISTS payments.transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider TEXT NOT NULL CHECK (provider IN ('moolre', 'paystack', 'stripe')),
    flow_type TEXT NOT NULL CHECK (flow_type IN ('collection', 'disbursement')),
    state TEXT NOT NULL CHECK (state IN ('initiated', 'pending', 'completed', 'failed', 'cancelled')),
    amount NUMERIC(18,2) NOT NULL CHECK (amount > 0),
    currency CHAR(3) NOT NULL,
    phone_number TEXT NOT NULL,
    network TEXT NOT NULL,
    user_reference TEXT NOT NULL,
    merchant_reference TEXT NOT NULL,
    provider_reference TEXT,
    provider_status TEXT,
    bank_branch TEXT,
    failure_reason TEXT,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_transactions_provider_reference
    ON payments.transactions (provider, provider_reference)
    WHERE provider_reference IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_transactions_merchant_reference
    ON payments.transactions (merchant_reference);

CREATE INDEX IF NOT EXISTS ix_transactions_user_reference
    ON payments.transactions (user_reference);

CREATE INDEX IF NOT EXISTS ix_transactions_state_created_at
    ON payments.transactions (state, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_transactions_provider_created_at
    ON payments.transactions (provider, created_at DESC);

CREATE TRIGGER trg_transactions_updated_at
BEFORE UPDATE ON payments.transactions
FOR EACH ROW
EXECUTE FUNCTION payments.set_updated_at();

-- Step-level audit trail for each flow operation.
CREATE TABLE IF NOT EXISTS payments.transaction_events (
    id BIGSERIAL PRIMARY KEY,
    transaction_id UUID NOT NULL REFERENCES payments.transactions(id) ON DELETE CASCADE,
    step TEXT NOT NULL CHECK (step IN ('initiate_collection', 'complete_collection', 'check_status', 'disbursement')),
    step_status TEXT NOT NULL CHECK (step_status IN ('success', 'failed')),
    request_payload JSONB,
    response_payload JSONB,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_transaction_events_transaction_id_created_at
    ON payments.transaction_events (transaction_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_transaction_events_step_created_at
    ON payments.transaction_events (step, created_at DESC);
