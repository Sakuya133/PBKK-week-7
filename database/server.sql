USE PosDesktop;
GO
IF OBJECT_ID(N'dbo.AuthSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuthSessions (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuthSessions PRIMARY KEY,
        UserId int NOT NULL,
        TokenHash binary(32) NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        ExpiresAt datetimeoffset(7) NOT NULL,
        RevokedAt datetimeoffset(7) NULL,
        CONSTRAINT FK_AuthSessions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT UQ_AuthSessions_TokenHash UNIQUE (TokenHash),
        CONSTRAINT CK_AuthSessions_Expiry CHECK (ExpiresAt > CreatedAt)
    );
    CREATE INDEX IX_AuthSessions_User_Expiry ON dbo.AuthSessions(UserId, ExpiresAt);
    CREATE INDEX IX_AuthSessions_Expiry ON dbo.AuthSessions(ExpiresAt);
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AuthSessions') AND name=N'IX_AuthSessions_Expiry')
    CREATE INDEX IX_AuthSessions_Expiry ON dbo.AuthSessions(ExpiresAt);
GO
