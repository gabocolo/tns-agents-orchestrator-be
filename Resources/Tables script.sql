CREATE TABLE Proposals (
    Id              UNIQUEIDENTIFIER  PRIMARY KEY,
    Name            NVARCHAR(200)     NOT NULL,
    ProjectName     NVARCHAR(200)     NOT NULL,
    Status          NVARCHAR(50)      NOT NULL DEFAULT 'Draft',
    SessionId       UNIQUEIDENTIFIER  NOT NULL,
    CreatedByUserId NVARCHAR(200)     NOT NULL,
    Tags            NVARCHAR(500)     NOT NULL DEFAULT '',
    CreatedAt       DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
     
    INDEX IX_Proposals_Status        (Status),
    INDEX IX_Proposals_CreatedByUser (CreatedByUserId)
);
     
CREATE TABLE ProposalIterations (
    ProposalId    UNIQUEIDENTIFIER  NOT NULL REFERENCES Proposals(Id) ON DELETE CASCADE,
    Version       INT               NOT NULL,
    Content       NVARCHAR(MAX)     NOT NULL,
    Components    NVARCHAR(MAX)     NOT NULL DEFAULT '',
    TeamSize      INT               NOT NULL DEFAULT 0,
    DurationWeeks INT               NOT NULL DEFAULT 0,
    BudgetUsd     DECIMAL(18,2)     NOT NULL DEFAULT 0,
    RiskLevel     NVARCHAR(20)      NOT NULL DEFAULT 'Medium',
    CreatedAt     DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
     
    PRIMARY KEY (ProposalId, Version),
    INDEX IX_ProposalIterations_ProposalId (ProposalId)
);
     
CREATE TABLE ProposalComments (
    Id               UNIQUEIDENTIFIER  PRIMARY KEY,
    ProposalId       UNIQUEIDENTIFIER  NOT NULL REFERENCES Proposals(Id) ON DELETE CASCADE,
    AuthorId         NVARCHAR(200)     NOT NULL,
    AuthorName       NVARCHAR(200)     NOT NULL,
    AuthorRole       NVARCHAR(50)      NOT NULL,
    Body             NVARCHAR(MAX)     NOT NULL,
    IterationVersion INT               NOT NULL DEFAULT 1,
    CreatedAt        DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
    ResolvedAt       DATETIME2         NULL,
     
    INDEX IX_ProposalComments_ProposalId (ProposalId)
);
     
CREATE TABLE ProposalApprovalSteps (
    Id         UNIQUEIDENTIFIER  PRIMARY KEY,
    ProposalId UNIQUEIDENTIFIER  NOT NULL REFERENCES Proposals(Id) ON DELETE CASCADE,
    Role       NVARCHAR(50)      NOT NULL,
    UserId     NVARCHAR(200)     NOT NULL,
    UserName   NVARCHAR(200)     NOT NULL,
    Status     NVARCHAR(50)      NOT NULL DEFAULT 'Pending',
    Note       NVARCHAR(1000)    NULL,
    DecidedAt  DATETIME2         NULL,
     
    INDEX IX_ProposalApprovalSteps_ProposalId (ProposalId),
    INDEX IX_ProposalApprovalSteps_UserId     (UserId)
);