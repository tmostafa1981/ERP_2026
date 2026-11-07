BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    DROP INDEX [IX_VendorCategories_Code] ON [VendorCategories];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    DROP INDEX [IX_CustomerCategories_Code] ON [CustomerCategories];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Vendors] ADD [FinancialAccountId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Vendors] ADD [Mobile2] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Vendors] ADD [Notes] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [VendorCategories] ADD [BranchId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [VendorCategories] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Customers] ADD [FinancialAccountId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Customers] ADD [Mobile2] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Customers] ADD [Notes] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [CustomerCategories] ADD [BranchId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [CustomerCategories] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Branches] ADD [BranchCode] nvarchar(20) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [AccountGroups] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [NameAr] nvarchar(200) NOT NULL,
        [NameEn] nvarchar(200) NOT NULL,
        [ParentId] int NULL,
        [Level] int NOT NULL,
        [AccountNature] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_AccountGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccountGroups_AccountGroups_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [AccountGroups] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [CodeSequences] (
        [Id] int NOT NULL IDENTITY,
        [EntityName] nvarchar(50) NOT NULL,
        [BranchId] int NULL,
        [Prefix] nvarchar(10) NOT NULL,
        [LastNumber] bigint NOT NULL DEFAULT CAST(0 AS bigint),
        [PaddingLength] int NOT NULL DEFAULT 6,
        [IsAuto] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsManualAllowed] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_CodeSequences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CodeSequences_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [CostCenters] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [NameAr] nvarchar(200) NOT NULL,
        [NameEn] nvarchar(200) NOT NULL,
        [ParentId] int NULL,
        [IsTransactional] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_CostCenters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CostCenters_CostCenters_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [CostCenters] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [JournalEntryHeaders] (
        [Id] int NOT NULL IDENTITY,
        [BranchId] int NOT NULL,
        [FiscalYearId] int NOT NULL,
        [EntryNumber] nvarchar(50) NOT NULL,
        [EntryDate] datetime2 NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [Status] int NOT NULL,
        [ReferenceNumber] nvarchar(100) NULL,
        [SourceDocumentType] int NOT NULL,
        [SourceDocumentId] int NULL,
        [TotalDebitLocal] decimal(18,4) NOT NULL DEFAULT 0.0,
        [TotalCreditLocal] decimal(18,4) NOT NULL DEFAULT 0.0,
        [PostedAt] datetime2 NULL,
        [PostedByUserId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_JournalEntryHeaders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JournalEntryHeaders_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalEntryHeaders_FiscalYears_FiscalYearId] FOREIGN KEY ([FiscalYearId]) REFERENCES [FiscalYears] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [Accounts] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [NameAr] nvarchar(200) NOT NULL,
        [NameEn] nvarchar(200) NOT NULL,
        [AccountGroupId] int NOT NULL,
        [IsReconciliationAccount] bit NOT NULL,
        [IsTransactional] bit NOT NULL,
        [CurrencyId] int NULL,
        [AccountUsage] int NOT NULL,
        [RequiresCostCenter] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Accounts_AccountGroups_AccountGroupId] FOREIGN KEY ([AccountGroupId]) REFERENCES [AccountGroups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Accounts_Currencies_CurrencyId] FOREIGN KEY ([CurrencyId]) REFERENCES [Currencies] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [AccountPermissions] (
        [Id] int NOT NULL IDENTITY,
        [AccountId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [CanView] bit NOT NULL,
        [CanPost] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_AccountPermissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccountPermissions_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE TABLE [JournalEntryLines] (
        [Id] int NOT NULL IDENTITY,
        [JournalEntryHeaderId] int NOT NULL,
        [LineNumber] int NOT NULL,
        [AccountId] int NOT NULL,
        [SubLedgerType] int NOT NULL,
        [SubLedgerId] int NULL,
        [CurrencyId] int NOT NULL,
        [ExchangeRate] decimal(18,6) NOT NULL DEFAULT 1.0,
        [DebitForeign] decimal(18,4) NOT NULL DEFAULT 0.0,
        [CreditForeign] decimal(18,4) NOT NULL DEFAULT 0.0,
        [DebitLocal] decimal(18,4) NOT NULL DEFAULT 0.0,
        [CreditLocal] decimal(18,4) NOT NULL DEFAULT 0.0,
        [CostCenterId] int NULL,
        [Description] nvarchar(500) NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_JournalEntryLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JournalEntryLines_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalEntryLines_CostCenters_CostCenterId] FOREIGN KEY ([CostCenterId]) REFERENCES [CostCenters] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalEntryLines_Currencies_CurrencyId] FOREIGN KEY ([CurrencyId]) REFERENCES [Currencies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_JournalEntryLines_JournalEntryHeaders_JournalEntryHeaderId] FOREIGN KEY ([JournalEntryHeaderId]) REFERENCES [JournalEntryHeaders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_Vendors_FinancialAccountId] ON [Vendors] ([FinancialAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VendorCategories_BranchId_Code] ON [VendorCategories] ([BranchId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_Customers_FinancialAccountId] ON [Customers] ([FinancialAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerCategories_BranchId_Code] ON [CustomerCategories] ([BranchId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Branches_BranchCode] ON [Branches] ([BranchCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AccountGroups_Code] ON [AccountGroups] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_AccountGroups_ParentId] ON [AccountGroups] ([ParentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AccountPermissions_Account_User] ON [AccountPermissions] ([AccountId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_Accounts_AccountGroupId] ON [Accounts] ([AccountGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Accounts_Code] ON [Accounts] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_Accounts_CurrencyId] ON [Accounts] ([CurrencyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_CodeSequences_BranchId] ON [CodeSequences] ([BranchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CodeSequences_EntityName_BranchId] ON [CodeSequences] ([EntityName], [BranchId]) WHERE [BranchId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CostCenters_Code] ON [CostCenters] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_CostCenters_ParentId] ON [CostCenters] ([ParentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_JournalEntryHeaders_Branch_FY_Date] ON [JournalEntryHeaders] ([BranchId], [FiscalYearId], [EntryDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_JournalEntryHeaders_Branch_Number] ON [JournalEntryHeaders] ([BranchId], [EntryNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_JournalEntryHeaders_FiscalYearId] ON [JournalEntryHeaders] ([FiscalYearId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_JournalEntryHeaders_SourceDoc] ON [JournalEntryHeaders] ([SourceDocumentType], [SourceDocumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_JournalEntryLines_Account_SubLedger] ON [JournalEntryLines] ([AccountId], [SubLedgerType], [SubLedgerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_JournalEntryLines_CostCenterId] ON [JournalEntryLines] ([CostCenterId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE INDEX [IX_JournalEntryLines_CurrencyId] ON [JournalEntryLines] ([CurrencyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_JournalEntryLines_Header_LineNumber] ON [JournalEntryLines] ([JournalEntryHeaderId], [LineNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [CustomerCategories] ADD CONSTRAINT [FK_CustomerCategories_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Customers] ADD CONSTRAINT [FK_Customers_Accounts_FinancialAccountId] FOREIGN KEY ([FinancialAccountId]) REFERENCES [Accounts] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [VendorCategories] ADD CONSTRAINT [FK_VendorCategories_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    ALTER TABLE [Vendors] ADD CONSTRAINT [FK_Vendors_Accounts_FinancialAccountId] FOREIGN KEY ([FinancialAccountId]) REFERENCES [Accounts] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926154012_Phase4_CoreAccountingFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926154012_Phase4_CoreAccountingFoundation', N'8.0.8');
END;
GO

COMMIT;
GO

