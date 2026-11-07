-- ==============================================================================================
-- ERP 2026 Platform - Purge Master Data (شجرة الحسابات، العملاء، الموردين، الأصناف)
-- Target Environment: Microsoft SQL Server 2022+ / Azure SQL Database
-- Description:
--   يقوم هذا الاسكريبت بمسح وتصفير البيانات التالية بالكامل وإعادة ترقيمها لتبدأ من 1:
--     1. شجرة الحسابات بالكامل (GLAccounts & AccountPermissions)
--     2. العملاء (Customers) ومجموعات ربط العملات (PartyGroups)
--     3. الموردين (Vendors)
--     4. الأصناف ووحدات القياس وسيريال الأصناف (Items, ItemUnits, ItemSerialNumbers, ItemSerialMovements)
--
-- تنبيه مهم جداً (Crucial Dependency Rule):
--   نظراً لأن جميع الحركات والعمليات المالية والمخزنية (فواتير، سندات قبض وصرف، قيود يومية، أذون مخازن)
--   مرتبطة ارتباطاً وثيقاً بالحسابات والأصناف والعملاء والموردين عبر قيود المفاتيح الأجنبية (Foreign Keys)،
--   فإن حذف هذه البيانات يستلزم بالضرورة تصفير الحركات والعمليات التابعة لها أولاً لضمان سلامة قاعدة البيانات وعدم حدوث أخطاء تعارض (Constraint Conflicts).
--
-- البيانات المحفوظة التي لا يتم مسحها:
--   - الشركات والفروع (Companies, Branches, UserBranches)
--   - المستخدمين والأدوار وصلاحيات الشاشات (AspNetUsers, AspNetRoles, etc.)
--   - السنوات والفترات المالية (FiscalYears, FiscalPeriods)
--   - المستودعات والمخازن (Warehouses)
--   - البنوك والعملات ومراكز التكلفة (Banks, Currencies, CostCenters)
--   - أنواع الحركات والمستندات (TransactionTypes) - يتم فك ارتباط الحسابات الافتراضية فقط
--   - تصنيفات العملاء والموردين ومجموعات الأصناف ووحدات القياس (Categories & ItemGroups & Units)
-- ==============================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'==============================================================================';
PRINT N'   ERP 2026 - PURGE ACCOUNTS, CUSTOMERS, VENDORS & ITEMS                      ';
PRINT N'   (مسح وتصفير: شجرة الحسابات، العملاء، الموردين، الأصناف)                     ';
PRINT N'==============================================================================';
PRINT N'Database Target : ' + DB_NAME();
PRINT N'Server Name     : ' + @@SERVERNAME;
PRINT N'Execution Date  : ' + CONVERT(NVARCHAR(30), GETDATE(), 120);
PRINT N'------------------------------------------------------------------------------';

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT N'[1/7] فحص وحساب عدد السجلات الحالية قبل المسح...';

    -- الحركات والمستندات التابعة
    DECLARE @cnt_DocAdjLines   INT = (SELECT COUNT(*) FROM [DocumentAdjustmentLines]);
    DECLARE @cnt_DocSerials    INT = (SELECT COUNT(*) FROM [DocumentLineSerials]);
    DECLARE @cnt_DocLineLinks  INT = (SELECT COUNT(*) FROM [DocumentLineLinks]);
    DECLARE @cnt_DocConvLinks  INT = (SELECT COUNT(*) FROM [DocumentConversionLinks]);
    DECLARE @cnt_DocLines      INT = (SELECT COUNT(*) FROM [DocumentLines]);
    DECLARE @cnt_DocHeaders    INT = (SELECT COUNT(*) FROM [DocumentHeaders]);
    DECLARE @cnt_SerialMoves   INT = (SELECT COUNT(*) FROM [ItemSerialMovements]);
    DECLARE @cnt_Serials       INT = (SELECT COUNT(*) FROM [ItemSerialNumbers]);
    DECLARE @cnt_VchrLines     INT = (SELECT COUNT(*) FROM [VoucherLines]);
    DECLARE @cnt_VchrHeaders   INT = (SELECT COUNT(*) FROM [VoucherHeaders]);
    DECLARE @cnt_JELines       INT = (SELECT COUNT(*) FROM [JournalEntryLines]);
    DECLARE @cnt_JEHeaders     INT = (SELECT COUNT(*) FROM [JournalEntryHeaders]);
    DECLARE @cnt_Audit         INT = (SELECT COUNT(*) FROM [AuditLogs]);

    -- البيانات المطلوب حذفها
    DECLARE @cnt_ItemUnits     INT = (SELECT COUNT(*) FROM [ItemUnits]);
    DECLARE @cnt_Items         INT = (SELECT COUNT(*) FROM [Items]);
    DECLARE @cnt_Customers     INT = (SELECT COUNT(*) FROM [Customers]);
    DECLARE @cnt_Vendors       INT = (SELECT COUNT(*) FROM [Vendors]);
    DECLARE @cnt_PartyGroups   INT = (SELECT COUNT(*) FROM [PartyGroups]);
    DECLARE @cnt_AccPerms      INT = (SELECT COUNT(*) FROM [AccountPermissions]);
    DECLARE @cnt_GLAccounts    INT = (SELECT COUNT(*) FROM [GLAccounts]);

    PRINT N'  -> DocumentAdjustmentLines : ' + CAST(@cnt_DocAdjLines AS NVARCHAR(20));
    PRINT N'  -> DocumentLineSerials     : ' + CAST(@cnt_DocSerials AS NVARCHAR(20));
    PRINT N'  -> DocumentLines           : ' + CAST(@cnt_DocLines AS NVARCHAR(20));
    PRINT N'  -> DocumentHeaders         : ' + CAST(@cnt_DocHeaders AS NVARCHAR(20));
    PRINT N'  -> ItemSerialNumbers       : ' + CAST(@cnt_Serials AS NVARCHAR(20));
    PRINT N'  -> VoucherLines / Headers  : ' + CAST(@cnt_VchrLines AS NVARCHAR(20)) + N' / ' + CAST(@cnt_VchrHeaders AS NVARCHAR(20));
    PRINT N'  -> JournalEntryLines/Heads : ' + CAST(@cnt_JELines AS NVARCHAR(20)) + N' / ' + CAST(@cnt_JEHeaders AS NVARCHAR(20));
    PRINT N'  -------------------------------------------------------------';
    PRINT N'  -> الأصناف (Items / Units)   : ' + CAST(@cnt_Items AS NVARCHAR(20)) + N' / ' + CAST(@cnt_ItemUnits AS NVARCHAR(20));
    PRINT N'  -> العملاء (Customers)      : ' + CAST(@cnt_Customers AS NVARCHAR(20));
    PRINT N'  -> الموردين (Vendors)        : ' + CAST(@cnt_Vendors AS NVARCHAR(20));
    PRINT N'  -> مجموعات الربط PartyGroups: ' + CAST(@cnt_PartyGroups AS NVARCHAR(20));
    PRINT N'  -> شجرة الحسابات GLAccounts  : ' + CAST(@cnt_GLAccounts AS NVARCHAR(20)) + N' (صلاحيات: ' + CAST(@cnt_AccPerms AS NVARCHAR(20)) + N')';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[2/7] فك الارتباطات والقيود الدائرية والمفاتيح الأجنبية مؤقتاً...';

    -- 1. فك ارتباط القيود بالمستندات والسندات
    UPDATE [DocumentHeaders] SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;
    UPDATE [VoucherHeaders]  SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;

    -- 2. تصفير الحسابات والأطراف الافتراضية في إعدادات أنواع الحركات
    UPDATE [TransactionTypes]
    SET [DefaultCustomerId]         = NULL,
        [DefaultVendorId]           = NULL,
        [DefaultCashBankAccountId]  = NULL,
        [DefaultDebitAccountId]     = NULL,
        [DefaultCreditAccountId]    = NULL,
        [DefaultTaxAccountId]       = NULL,
        [DefaultDiscountAccountId]  = NULL;

    -- 3. تصفير حسابات المراقبة المرتبطة بتصنيفات العملاء والموردين
    UPDATE [CustomerCategories] SET [ControlAccountId] = NULL WHERE [ControlAccountId] IS NOT NULL;
    UPDATE [VendorCategories]   SET [ControlAccountId] = NULL WHERE [ControlAccountId] IS NOT NULL;

    -- 4. كسر الهيكل الشجري للحسابات (تصفير الحساب الرئيسي لتفادي خطأ Self-Referencing ParentId)
    UPDATE [GLAccounts] SET [ParentId] = NULL WHERE [ParentId] IS NOT NULL;

    -- 5. تعطيل القيود مؤقتاً لسرعة التنفيذ ومنع تعارض العلاقات
    ALTER TABLE [DocumentAdjustmentLines] NOCHECK CONSTRAINT ALL;
    ALTER TABLE [TransactionTypeAdjustments] NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentAdjustments]     NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLineSerials]     NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLineLinks]       NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentConversionLinks] NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLines]           NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentHeaders]         NOCHECK CONSTRAINT ALL;
    ALTER TABLE [ItemSerialMovements]     NOCHECK CONSTRAINT ALL;
    ALTER TABLE [ItemSerialNumbers]       NOCHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherLines]            NOCHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherHeaders]          NOCHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryLines]       NOCHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryHeaders]     NOCHECK CONSTRAINT ALL;
    ALTER TABLE [ItemUnits]               NOCHECK CONSTRAINT ALL;
    ALTER TABLE [Items]                   NOCHECK CONSTRAINT ALL;
    ALTER TABLE [Customers]               NOCHECK CONSTRAINT ALL;
    ALTER TABLE [Vendors]                 NOCHECK CONSTRAINT ALL;
    ALTER TABLE [PartyGroups]             NOCHECK CONSTRAINT ALL;
    ALTER TABLE [AccountPermissions]      NOCHECK CONSTRAINT ALL;
    ALTER TABLE [GLAccounts]              NOCHECK CONSTRAINT ALL;

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[3/7] مسح بيانات الحركات والعمليات التابعة (Transactions Cleanup)...';

    DELETE FROM [DocumentAdjustmentLines];
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TransactionTypeAdjustments')
        DELETE FROM [TransactionTypeAdjustments];
    DELETE FROM [DocumentAdjustments];
    DELETE FROM [DocumentLineSerials];
    DELETE FROM [DocumentLineLinks];
    DELETE FROM [DocumentConversionLinks];
    DELETE FROM [DocumentLines];
    DELETE FROM [DocumentHeaders];
    PRINT N'  [OK] تم مسح جميع الفواتير والمستندات وأذون المخازن والخصومات.';

    DELETE FROM [ItemSerialMovements];
    DELETE FROM [ItemSerialNumbers];
    PRINT N'  [OK] تم مسح جميع أرقام وسجلات سيريال الأصناف.';

    DELETE FROM [VoucherLines];
    DELETE FROM [VoucherHeaders];
    PRINT N'  [OK] تم مسح جميع سندات القبض والصرف.';

    DELETE FROM [JournalEntryLines];
    DELETE FROM [JournalEntryHeaders];
    PRINT N'  [OK] تم مسح جميع قيود اليومية العامة.';

    DELETE FROM [AuditLogs];
    PRINT N'  [OK] تم مسح سجل العمليات والتدقيق (AuditLogs).';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[4/7] مسح البيانات الأساسية المستهدفة (الأصناف، العملاء، الموردين، الحسابات)...';

    -- 1. مسح الأصناف ووحدات القياس
    DELETE FROM [ItemUnits];
    DELETE FROM [Items];
    PRINT N'  [OK] تم مسح جميع الأصناف ووحداتها (Items & ItemUnits).';

    -- 2. مسح العملاء
    DELETE FROM [Customers];
    PRINT N'  [OK] تم مسح جميع العملاء (Customers).';

    -- 3. مسح الموردين
    DELETE FROM [Vendors];
    PRINT N'  [OK] تم مسح جميع الموردين (Vendors).';

    -- 4. مسح مجموعات الربط للعملات المتعددة
    DELETE FROM [PartyGroups];
    PRINT N'  [OK] تم مسح مجموعات ربط العملاء والموردين (PartyGroups).';

    -- 5. مسح شجرة الحسابات وصلاحياتها
    DELETE FROM [AccountPermissions];
    DELETE FROM [GLAccounts];
    PRINT N'  [OK] تم مسح شجرة الحسابات بالكامل وصلاحياتها (GLAccounts & AccountPermissions).';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[5/7] إعادة تصفير عدادات الترقيم التلقائي (Identity Reseed to 0)...';

    -- الجداول الرئيسية
    DBCC CHECKIDENT ('Items', RESEED, 0);
    DBCC CHECKIDENT ('ItemUnits', RESEED, 0);
    DBCC CHECKIDENT ('Customers', RESEED, 0);
    DBCC CHECKIDENT ('Vendors', RESEED, 0);
    DBCC CHECKIDENT ('PartyGroups', RESEED, 0);
    DBCC CHECKIDENT ('GLAccounts', RESEED, 0);
    DBCC CHECKIDENT ('AccountPermissions', RESEED, 0);

    -- جداول الحركات والسيريال
    DBCC CHECKIDENT ('ItemSerialNumbers', RESEED, 0);
    DBCC CHECKIDENT ('ItemSerialMovements', RESEED, 0);
    DBCC CHECKIDENT ('DocumentHeaders', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLines', RESEED, 0);
    DBCC CHECKIDENT ('DocumentAdjustmentLines', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLineSerials', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLineLinks', RESEED, 0);
    DBCC CHECKIDENT ('DocumentConversionLinks', RESEED, 0);
    DBCC CHECKIDENT ('VoucherHeaders', RESEED, 0);
    DBCC CHECKIDENT ('VoucherLines', RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryHeaders', RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryLines', RESEED, 0);
    DBCC CHECKIDENT ('AuditLogs', RESEED, 0);

    PRINT N'  [OK] تم إعادة تصفير الترقيم التلقائي (Identity) لتبدأ السجلات الجديدة من الرقم 1.';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[6/7] تصفير سلاسل توليد الأكواد التلقائية (Reset CodeSequences)...';

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CodeSequences')
    BEGIN
        UPDATE [CodeSequences]
        SET [LastNumber] = 0
        WHERE [EntityName] IN (
            'Item',
            'Customer',
            'Vendor',
            'Document',
            'Voucher',
            'JournalEntry',
            'Receipt',
            'Payment'
        );
        PRINT N'  [OK] تم إعادة ضبط عدادات الأكواد التلقائية في جدول CodeSequences.';
    END

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[7/7] إعادة تفعيل قيود المفاتيح الأجنبية وفحص التكامل...';

    ALTER TABLE [DocumentAdjustmentLines] CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentAdjustments]     CHECK CONSTRAINT ALL;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TransactionTypeAdjustments')
        ALTER TABLE [TransactionTypeAdjustments] CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLineSerials]     CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLineLinks]       CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentConversionLinks] CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLines]           CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentHeaders]         CHECK CONSTRAINT ALL;
    ALTER TABLE [ItemSerialMovements]     CHECK CONSTRAINT ALL;
    ALTER TABLE [ItemSerialNumbers]       CHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherLines]            CHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherHeaders]          CHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryLines]       CHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryHeaders]     CHECK CONSTRAINT ALL;
    ALTER TABLE [ItemUnits]               CHECK CONSTRAINT ALL;
    ALTER TABLE [Items]                   CHECK CONSTRAINT ALL;
    ALTER TABLE [Customers]               CHECK CONSTRAINT ALL;
    ALTER TABLE [Vendors]                 CHECK CONSTRAINT ALL;
    ALTER TABLE [PartyGroups]             CHECK CONSTRAINT ALL;
    ALTER TABLE [AccountPermissions]      CHECK CONSTRAINT ALL;
    ALTER TABLE [GLAccounts]              CHECK CONSTRAINT ALL;

    COMMIT TRANSACTION;

    PRINT N'==============================================================================';
    PRINT N'           SUCCESS! تم تنفيذ عملية المسح والتصفير بنجاح تام                   ';
    PRINT N'==============================================================================';

    -- عرض الإحصائيات بعد التنفيذ
    SELECT 
        'شجرة الحسابات (GLAccounts)' AS [البيان], (SELECT COUNT(*) FROM [GLAccounts]) AS [عدد السجلات الحالي]
    UNION ALL SELECT 'العملاء (Customers)', (SELECT COUNT(*) FROM [Customers])
    UNION ALL SELECT 'الموردين (Vendors)', (SELECT COUNT(*) FROM [Vendors])
    UNION ALL SELECT 'الأصناف (Items)', (SELECT COUNT(*) FROM [Items])
    UNION ALL SELECT 'وحدات الأصناف (ItemUnits)', (SELECT COUNT(*) FROM [ItemUnits])
    UNION ALL SELECT 'فواتير ومستندات (DocumentHeaders)', (SELECT COUNT(*) FROM [DocumentHeaders])
    UNION ALL SELECT 'سندات القبض والصرف (VoucherHeaders)', (SELECT COUNT(*) FROM [VoucherHeaders])
    UNION ALL SELECT 'قيود اليومية (JournalEntryHeaders)', (SELECT COUNT(*) FROM [JournalEntryHeaders]);

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT N'==============================================================================';
    PRINT N'                 حدث خطأ أثناء التنفيذ! تم التراجع عن كافة التغييرات             ';
    PRINT N'==============================================================================';
    PRINT N'Error Message  : ' + ERROR_MESSAGE();
    PRINT N'Error Number   : ' + CAST(ERROR_NUMBER() AS NVARCHAR(20));
    PRINT N'Error Severity : ' + CAST(ERROR_SEVERITY() AS NVARCHAR(20));
    PRINT N'Error State    : ' + CAST(ERROR_STATE() AS NVARCHAR(20));
    PRINT N'Error Line     : ' + CAST(ERROR_LINE() AS NVARCHAR(20));
    
    THROW;
END CATCH;
GO
