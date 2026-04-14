-- Migration 001: Governance tables (PROMPT-01)
-- Base tables: users, projects, adrs, quality_gate_checks, audit_logs

-- ── users ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS users (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    external_id   VARCHAR(200) NOT NULL UNIQUE,
    email         VARCHAR(300),
    name          VARCHAR(200),
    role          VARCHAR(15) NOT NULL DEFAULT 'SENIOR_DEV',
    provider      VARCHAR(20) NOT NULL DEFAULT 'local',
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login_at TIMESTAMPTZ
);

-- Insert a dev user for testing
INSERT INTO users (id, external_id, email, name, role, provider)
VALUES (
    '00000000-0000-0000-0000-000000000099',
    'dev-user-local',
    'dev@example.com',
    'Dev User',
    'ARCHITECT',
    'local'
) ON CONFLICT (external_id) DO NOTHING;

-- ── projects ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS projects (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(200) NOT NULL UNIQUE,
    description     TEXT,
    git_repo_url    VARCHAR(500),
    owner_id        UUID NOT NULL REFERENCES users(id),
    devops_org      VARCHAR(200),
    devops_project  VARCHAR(200),
    devops_area     VARCHAR(300),
    devops_pat_enc  BYTEA,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── adrs ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS adrs (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id  UUID NOT NULL REFERENCES projects(id),
    number      INTEGER NOT NULL,
    title       VARCHAR(300) NOT NULL,
    status      VARCHAR(20) NOT NULL DEFAULT 'PROPOSED',
    content     TEXT NOT NULL,
    created_by  UUID NOT NULL REFERENCES users(id),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_adr_project_number UNIQUE (project_id, number)
);

-- ── quality_gate_checks ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS quality_gate_checks (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id   UUID NOT NULL REFERENCES projects(id),
    gate_number  INTEGER NOT NULL CHECK (gate_number BETWEEN 1 AND 3),
    name         VARCHAR(200) NOT NULL,
    validations  JSONB NOT NULL,
    blocking     BOOLEAN NOT NULL DEFAULT true,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_gate_project_number UNIQUE (project_id, gate_number)
);

-- ── audit_logs ───────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS audit_logs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id      UUID REFERENCES projects(id),
    entity_type     VARCHAR(50) NOT NULL,
    entity_id       UUID NOT NULL,
    action          VARCHAR(50) NOT NULL,
    actor_id        UUID NOT NULL REFERENCES users(id),
    details         JSONB,
    correlation_id  UUID NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_audit_entity ON audit_logs(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_audit_project ON audit_logs(project_id);
CREATE INDEX IF NOT EXISTS idx_audit_correlation ON audit_logs(correlation_id);
CREATE INDEX IF NOT EXISTS idx_audit_created ON audit_logs(created_at);
