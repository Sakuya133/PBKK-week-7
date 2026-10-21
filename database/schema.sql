IF DB_ID(N'PosDesktop') IS NULL CREATE DATABASE PosDesktop;
GO
USE PosDesktop;
GO
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Username nvarchar(80) NOT NULL,
        PasswordHash nvarchar(250) NOT NULL,
        FullName nvarchar(160) NOT NULL,
        Role varchar(16) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        FailedLoginCount int NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT (0),
        LockoutUntil datetimeoffset(7) NULL,
        CONSTRAINT UQ_Users_Username UNIQUE (Username),
        CONSTRAINT CK_Users_Role CHECK (Role IN ('Admin','Kasir','Pelanggan')),
        CONSTRAINT CK_Users_FullName CHECK (LEN(TRIM(FullName)) > 0),
        CONSTRAINT CK_Users_FailedLoginCount CHECK (FailedLoginCount >= 0)
    );
END;
GO
IF COL_LENGTH(N'dbo.Users', N'FailedLoginCount') IS NULL
    ALTER TABLE dbo.Users ADD FailedLoginCount int NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT (0);
IF COL_LENGTH(N'dbo.Users', N'LockoutUntil') IS NULL
    ALTER TABLE dbo.Users ADD LockoutUntil datetimeoffset(7) NULL;
GO
IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categories (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
        Name nvarchar(100) NOT NULL CONSTRAINT UQ_Categories_Name UNIQUE,
        CONSTRAINT CK_Categories_Name CHECK (LEN(TRIM(Name)) > 0)
    );
END;
GO
IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Products (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        Name nvarchar(160) NOT NULL,
        CategoryId int NOT NULL,
        Price decimal(18,2) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT (1),
        CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id),
        CONSTRAINT CK_Products_Name CHECK (LEN(TRIM(Name)) > 0),
        CONSTRAINT CK_Products_Price CHECK (Price > 0)
    );
    CREATE INDEX IX_Products_Active_Name ON dbo.Products(IsActive, Name) INCLUDE (CategoryId, Price);
END;
GO
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Transactions (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Transactions PRIMARY KEY,
        Number varchar(48) NOT NULL CONSTRAINT UQ_Transactions_Number UNIQUE,
        CashierId int NOT NULL,
        CustomerId int NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        PaymentMethod varchar(10) NOT NULL,
        Total decimal(18,2) NOT NULL,
        RequestId uniqueidentifier NULL,
        CONSTRAINT FK_Transactions_Cashier FOREIGN KEY (CashierId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_Transactions_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_Transactions_Payment CHECK (PaymentMethod IN ('Tunai','Qris')),
        CONSTRAINT CK_Transactions_Total CHECK (Total > 0)
    );
    CREATE INDEX IX_Transactions_Date ON dbo.Transactions(CreatedAt) INCLUDE (Total);
    CREATE INDEX IX_Transactions_Cashier_Date ON dbo.Transactions(CashierId, CreatedAt DESC);
    CREATE INDEX IX_Transactions_Customer_Date ON dbo.Transactions(CustomerId, CreatedAt DESC) WHERE CustomerId IS NOT NULL;
    CREATE UNIQUE INDEX UX_Transactions_RequestId ON dbo.Transactions(RequestId) WHERE RequestId IS NOT NULL;
END;
GO
IF COL_LENGTH(N'dbo.Transactions', N'RequestId') IS NULL
    ALTER TABLE dbo.Transactions ADD RequestId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.Transactions', N'GuestName') IS NULL
    ALTER TABLE dbo.Transactions ADD GuestName nvarchar(80) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Transactions') AND name=N'UX_Transactions_RequestId')
    CREATE UNIQUE INDEX UX_Transactions_RequestId ON dbo.Transactions(RequestId) WHERE RequestId IS NOT NULL;
GO
IF OBJECT_ID(N'dbo.TransactionItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransactionItems (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransactionItems PRIMARY KEY,
        TransactionId bigint NOT NULL,
        ProductId int NOT NULL,
        ProductName nvarchar(160) NOT NULL,
        UnitPrice decimal(18,2) NOT NULL,
        Quantity int NOT NULL,
        Subtotal decimal(18,2) NOT NULL,
        CONSTRAINT FK_TransactionItems_Transaction FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(Id),
        CONSTRAINT FK_TransactionItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT UQ_TransactionItems_Transaction_Product UNIQUE (TransactionId, ProductId),
        CONSTRAINT CK_TransactionItems_Quantity CHECK (Quantity > 0),
        CONSTRAINT CK_TransactionItems_Price CHECK (UnitPrice > 0 AND Subtotal = UnitPrice * Quantity)
    );
    CREATE INDEX IX_TransactionItems_Product ON dbo.TransactionItems(ProductId);
END;
GO
IF OBJECT_ID(N'dbo.GuestOrders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GuestOrders (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_GuestOrders PRIMARY KEY,
        Number varchar(48) NOT NULL CONSTRAINT UQ_GuestOrders_Number UNIQUE,
        RequestId uniqueidentifier NOT NULL CONSTRAINT UQ_GuestOrders_RequestId UNIQUE,
        GuestName nvarchar(80) NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        PaymentMethod varchar(10) NOT NULL,
        Total decimal(18,2) NOT NULL,
        Status varchar(16) NOT NULL CONSTRAINT DF_GuestOrders_Status DEFAULT ('Menunggu'),
        TransactionId bigint NULL,
        CONSTRAINT FK_GuestOrders_Transaction FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions(Id),
        CONSTRAINT CK_GuestOrders_Name CHECK (LEN(TRIM(GuestName)) > 0),
        CONSTRAINT CK_GuestOrders_Payment CHECK (PaymentMethod IN ('Tunai','Qris')),
        CONSTRAINT CK_GuestOrders_Total CHECK (Total > 0),
        CONSTRAINT CK_GuestOrders_Status CHECK (Status IN ('Menunggu','Selesai')),
        CONSTRAINT CK_GuestOrders_Completed CHECK ((Status='Menunggu' AND TransactionId IS NULL) OR (Status='Selesai' AND TransactionId IS NOT NULL))
    );
    CREATE INDEX IX_GuestOrders_Status_CreatedAt ON dbo.GuestOrders(Status,CreatedAt);
END;
GO
IF OBJECT_ID(N'dbo.GuestOrders', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.GuestOrders') AND name=N'UX_GuestOrders_TransactionId')
    CREATE UNIQUE INDEX UX_GuestOrders_TransactionId ON dbo.GuestOrders(TransactionId) WHERE TransactionId IS NOT NULL;
GO
IF OBJECT_ID(N'dbo.GuestOrderItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GuestOrderItems (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_GuestOrderItems PRIMARY KEY,
        GuestOrderId bigint NOT NULL,
        ProductId int NOT NULL,
        ProductName nvarchar(160) NOT NULL,
        UnitPrice decimal(18,2) NOT NULL,
        Quantity int NOT NULL,
        Subtotal decimal(18,2) NOT NULL,
        CONSTRAINT FK_GuestOrderItems_Order FOREIGN KEY (GuestOrderId) REFERENCES dbo.GuestOrders(Id),
        CONSTRAINT FK_GuestOrderItems_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT UQ_GuestOrderItems_Order_Product UNIQUE (GuestOrderId,ProductId),
        CONSTRAINT CK_GuestOrderItems_Quantity CHECK (Quantity > 0),
        CONSTRAINT CK_GuestOrderItems_Price CHECK (UnitPrice > 0 AND Subtotal = UnitPrice * Quantity)
    );
    CREATE INDEX IX_GuestOrderItems_Product ON dbo.GuestOrderItems(ProductId);
END;
GO
