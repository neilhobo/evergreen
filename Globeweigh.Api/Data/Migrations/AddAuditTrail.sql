-- A generic audit trail: one row per FIELD that changed on a record after it was first created.
--
-- Generic on purpose. Nothing here names a dispatch order — the entity is identified by name and
-- id, so any table the API writes can be audited by adding it to the registry in
-- Data/Auditing/AuditedEntities.cs, with no schema change and no code in the controller doing the
-- saving. The writing happens in GbEvergreenContext.SaveChanges, so a change is audited because it
-- was SAVED, not because somebody remembered to log it.
--
-- Its own table rather than the legacy dbo.EntityHistory / dbo.ChangeLog pair the retired WPF app
-- left half-built. Those cannot carry an API's audit: EntityHistory requires a UserId and a small
-- fixed EntityTypeId enum, and a change made by a touch or picking screen has neither — those apps
-- post anonymously. Here the user is NULLABLE and Source says where the change came from instead,
-- which is the honest record.
--
-- Applied automatically by Globeweigh.Api\deploy\migrate-db.ps1 (npm run migrate:local / :prod).

IF OBJECT_ID('dbo.AuditTrail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditTrail (
        id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditTrail PRIMARY KEY,
        -- What was changed. Name + id rather than a foreign key: the trail outlives the record it
        -- describes, and one table cannot hold an FK to every table in the application.
        EntityName  nvarchar(100)  NOT NULL,
        EntityId    int            NOT NULL,
        -- The property as the model names it — the truth of what was touched. A friendlier label
        -- for the screen comes from the registry, not from rewriting this.
        FieldName   nvarchar(200)  NOT NULL,
        -- WHICH line of the record, for a change to a child row filed under its parent: an order
        -- item's quantity is "Quantity" on the order's trail, which says nothing without the
        -- product beside it. NULL for a change to the record itself.
        FieldScope  nvarchar(200)  NULL,
        -- Formatted for a person to read, because that is what an audit trail is for. NULL is a
        -- genuinely empty value, which is why "set for the first time" and "cleared" both read
        -- correctly rather than as the string "null".
        OldValue    nvarchar(max)  NULL,
        NewValue    nvarchar(max)  NULL,
        -- 'Updated' for a field that changed; 'Added' / 'Removed' for a child row (an order line)
        -- that appeared or went away.
        Action      nvarchar(20)   NOT NULL,
        DateChanged datetime       NOT NULL CONSTRAINT DF_AuditTrail_DateChanged DEFAULT (getdate()),
        -- NULL when the change came from a screen with no login — the touch and picking apps post
        -- anonymously, and recording 0 would be a lie dressed as a fact.
        UserId      int            NULL,
        UserName    nvarchar(200)  NULL,
        -- Where it came from: the calling app when it says so, otherwise the caller's address.
        Source      nvarchar(100)  NULL
    );
END
GO

-- Every read is "the trail for this one record, newest first".
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditTrail_Entity' AND object_id = OBJECT_ID('dbo.AuditTrail'))
    CREATE INDEX IX_AuditTrail_Entity ON dbo.AuditTrail (EntityName, EntityId, DateChanged DESC);
GO
