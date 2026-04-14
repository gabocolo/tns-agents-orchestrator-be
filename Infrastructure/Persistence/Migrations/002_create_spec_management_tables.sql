-- Migration 002: Spec Management + Agent Orchestration (PROMPT-02)
-- Prerequisite: 001_create_governance_tables.sql (projects, adrs, quality_gate_checks, audit_logs, users)

-- ── specifications ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS specifications (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id          UUID NOT NULL REFERENCES projects(id),
    parent_spec_id      UUID REFERENCES specifications(id),
    level               VARCHAR(2) NOT NULL CHECK (level IN ('L1','L2','L3')),
    title               VARCHAR(300) NOT NULL,
    version             VARCHAR(20) NOT NULL,
    status              VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    content             JSONB NOT NULL,
    data_classification JSONB,
    approved_by         UUID REFERENCES users(id),
    approved_at         TIMESTAMPTZ,
    model               VARCHAR(50),
    tokens_used         INTEGER,
    created_by          UUID NOT NULL REFERENCES users(id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_spec_project_title_level_version
        UNIQUE (project_id, title, level, version),
    CONSTRAINT chk_parent_required
        CHECK (
            (level = 'L1' AND parent_spec_id IS NULL) OR
            (level IN ('L2','L3') AND parent_spec_id IS NOT NULL)
        ),
    CONSTRAINT chk_data_classification_l1
        CHECK (
            level != 'L1' OR data_classification IS NOT NULL
        )
);

CREATE INDEX IF NOT EXISTS idx_specs_project ON specifications(project_id);
CREATE INDEX IF NOT EXISTS idx_specs_parent ON specifications(parent_spec_id);
CREATE INDEX IF NOT EXISTS idx_specs_status ON specifications(status);
CREATE INDEX IF NOT EXISTS idx_specs_level ON specifications(level);
CREATE INDEX IF NOT EXISTS idx_specs_project_status ON specifications(project_id, status);
CREATE INDEX IF NOT EXISTS idx_specs_project_level ON specifications(project_id, level);
CREATE INDEX IF NOT EXISTS idx_specs_created_at ON specifications(created_at);
CREATE INDEX IF NOT EXISTS idx_specs_parent_level ON specifications(parent_spec_id, level);

-- ── spec_generation_jobs ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS spec_generation_jobs (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id          UUID NOT NULL REFERENCES projects(id),
    level               VARCHAR(2) NOT NULL CHECK (level IN ('L1','L2','L3')),
    parent_spec_id      UUID REFERENCES specifications(id),
    sanitized_input     TEXT NOT NULL,
    additional_context  TEXT,
    status              VARCHAR(20) NOT NULL DEFAULT 'QUEUED',
    result_spec_id      UUID REFERENCES specifications(id),
    error               TEXT,
    retry_count         INTEGER NOT NULL DEFAULT 0,
    created_by          UUID NOT NULL REFERENCES users(id),
    correlation_id      UUID NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at        TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_spec_jobs_status ON spec_generation_jobs(status);
CREATE INDEX IF NOT EXISTS idx_spec_jobs_project ON spec_generation_jobs(project_id);
CREATE INDEX IF NOT EXISTS idx_spec_jobs_correlation ON spec_generation_jobs(correlation_id);
