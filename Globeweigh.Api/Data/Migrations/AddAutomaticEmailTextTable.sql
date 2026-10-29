-- The wording of the automatic emails (Admin > Automatic Emails): the "ready for collection"
-- email and the dispatch confirmation that go to the haulier from the Loads / Completed Loads
-- grids.
--
-- One row per PIECE of an email that has been changed from what the code says: the subject, the
-- greeting, the intro, and so on (Services/AutomaticEmails.cs lists them, with the default
-- wording). A piece with no row reads as the default, so nothing is seeded, a new piece costs no
-- schema change, and "reset to default" is a delete.
--
-- Applied automatically by Globeweigh.Api\deploy\migrate-db.ps1 (npm run migrate:local / :prod).

IF OBJECT_ID('dbo.AutomaticEmailText', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomaticEmailText (
        id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AutomaticEmailText PRIMARY KEY,
        -- Which email: ready | dispatched (AutomaticEmails.Ready / .Dispatched).
        Email        nvarchar(40)  NOT NULL,
        -- Which piece of it: subject | greeting | intro | ... (AutomaticEmails field keys).
        Field        nvarchar(40)  NOT NULL,
        -- The wording, with its {Placeholders} unexpanded. Empty is allowed and means the piece
        -- is left out of the email; it is the ABSENCE of a row that means "the default".
        Text         nvarchar(max) NOT NULL,
        DateChanged  datetime      NOT NULL,
        ChangedById  int           NULL,
        CONSTRAINT UQ_AutomaticEmailText_Email_Field UNIQUE (Email, Field)
    );
END
GO
