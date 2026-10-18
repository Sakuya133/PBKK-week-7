USE PosDesktop;
GO
IF DATABASE_PRINCIPAL_ID(N'pos_api_role') IS NULL EXEC(N'CREATE ROLE pos_api_role');
GO
GRANT SELECT ON dbo.Users TO pos_api_role;
GRANT UPDATE (PasswordHash, FailedLoginCount, LockoutUntil) ON dbo.Users TO pos_api_role;
GRANT SELECT ON dbo.Categories TO pos_api_role;
GRANT SELECT, INSERT, UPDATE ON dbo.Products TO pos_api_role;
GRANT SELECT, INSERT ON dbo.Transactions TO pos_api_role;
GRANT SELECT, INSERT ON dbo.TransactionItems TO pos_api_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AuthSessions TO pos_api_role;
IF DATABASE_PRINCIPAL_ID(N'pos_app_role') IS NOT NULL
BEGIN
    REVOKE UPDATE (PasswordHash, FailedLoginCount, LockoutUntil) ON dbo.Users FROM pos_app_role;
    REVOKE SELECT, INSERT, UPDATE ON dbo.Users FROM pos_app_role;
    REVOKE SELECT ON dbo.Categories FROM pos_app_role;
    REVOKE SELECT, INSERT, UPDATE ON dbo.Products FROM pos_app_role;
    REVOKE SELECT, INSERT ON dbo.Transactions FROM pos_app_role;
    REVOKE SELECT, INSERT ON dbo.TransactionItems FROM pos_app_role;
END;
GO
