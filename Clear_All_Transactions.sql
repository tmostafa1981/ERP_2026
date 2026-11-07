-- ==============================================================================================
-- ERP 2026 Platform - Purge All Transactional Data (اسكريبت مسح جميع الحركات للاختبار)
-- Target Environment: Microsoft SQL Server 2022+ / Azure SQL Database
-- Description:
--   يقوم هذا الاسكريبت بمسح وتصفير كافة الحركات والعمليات المالية والمستندية والمخزنية
--   دون المساس بالبيانات الأساسية (Master Data) كالحسابات، الفروع، الشركات، الأصناف، العملاء، إلخ.
--
-- Tables Cleared (الجداول التي يتم مسحها وتصفيرها بالكامل):
--   1. DocumentAdjustmentLines   (سطور تسويات وخصومات/مصاريف المستندات)
--   2. DocumentLineSerials       (سيريال أرقام سطور الفواتير والمستندات)
--   3. DocumentLineLinks         (روابط تحويل سطور المستندات)
--   4. DocumentConversionLinks   (روابط تحويل المستندات والفواتير)
--   5. DocumentLines             (سطور المستندات والفواتير وأذون المخازن)
--   6. DocumentHeaders           (رؤوس المستندات والفواتير وأذون الصرف والإضافة)
--   7. ItemSerialMovements       (حركات تتبع سيريال الأصناف)
--   8. ItemSerialNumbers         (أرقام السيريال للأصناف)
--   9. VoucherLines              (سطور سندات القبض والصرف)
--  10. VoucherHeaders            (رؤوس سندات القبض والصرف)
--  11. JournalEntryLines         (سطور قيود اليومية العامة)
--  12. JournalEntryHeaders       (رؤوس قيود اليومية العامة)
--  13. AuditLogs                 (سجل التدقيق والمتابعة)
--
-- Preserved Master Data (البيانات الأساسية المحفوظة بالكامل ولا يتم المساس بها):
--   - دليل الحسابات (GLAccounts, AccountPermissions)
--   - الشركات والفروع (Companies, Branches, UserBranches)
--   - السنوات والفترات المالية (FiscalYears, FiscalPeriods)
--   - العملاء والموردين وتصنيفاتهم (Customers, CustomerCategories, Vendors, VendorCategories)
--   - الأصناف والمجموعات والوحدات (Items, ItemGroups, Units, ItemUnits)
--   - المستودعات والمخازن (Warehouses)
--   - البنوك والعملات ومراكز التكلفة (Banks, Currencies, CostCenters)
--   - أنواع الحركات والمستندات وإعداداتها (TransactionTypes, TransactionTypeConversions, etc.)
--   - المستخدمين والأدوار والصلاحيات (AspNetUsers, AspNetRoles, Permissions, etc.)
--   - سلاسل الترقيم للبيانات الأساسية (CodeSequences)
-- ==============================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'==============================================================================';
PRINT N'              ERP 2026 - PURGE ALL TRANSACTIONS (مسح كل الحركات)             ';
PRINT N'==============================================================================';
PRINT N'Database Target : ' + DB_NAME();
PRINT N'Server Name     : ' + @@SERVERNAME;
PRINT N'Execution Date  : ' + CONVERT(NVARCHAR(30), GETDATE(), 120);
PRINT N'------------------------------------------------------------------------------';

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT N'[1/5] Checking Pre-Cleanup Counts (فحص أعداد السجلات الحالية)...';

    DECLARE @cnt_DocAdjLines INT = (SELECT COUNT(*) FROM [DocumentAdjustmentLines]);
    DECLARE @cnt_DocSerials  INT = (SELECT COUNT(*) FROM [DocumentLineSerials]);
    DECLARE @cnt_DocLineLinks INT = (SELECT COUNT(*) FROM [DocumentLineLinks]);
    DECLARE @cnt_DocConvLinks INT = (SELECT COUNT(*) FROM [DocumentConversionLinks]);
    DECLARE @cnt_DocLines    INT = (SELECT COUNT(*) FROM [DocumentLines]);
    DECLARE @cnt_DocHeaders  INT = (SELECT COUNT(*) FROM [DocumentHeaders]);
    DECLARE @cnt_SerialMoves INT = (SELECT COUNT(*) FROM [ItemSerialMovements]);
    DECLARE @cnt_Serials     INT = (SELECT COUNT(*) FROM [ItemSerialNumbers]);
    DECLARE @cnt_VchrLines   INT = (SELECT COUNT(*) FROM [VoucherLines]);
    DECLARE @cnt_VchrHeaders INT = (SELECT COUNT(*) FROM [VoucherHeaders]);
    DECLARE @cnt_JELines     INT = (SELECT COUNT(*) FROM [JournalEntryLines]);
    DECLARE @cnt_JEHeaders   INT = (SELECT COUNT(*) FROM [JournalEntryHeaders]);
    DECLARE @cnt_Audit       INT = (SELECT COUNT(*) FROM [AuditLogs]);

    PRINT N'  -> DocumentAdjustmentLines : ' + CAST(@cnt_DocAdjLines AS NVARCHAR(20));
    PRINT N'  -> DocumentLineSerials     : ' + CAST(@cnt_DocSerials AS NVARCHAR(20));
    PRINT N'  -> DocumentLineLinks       : ' + CAST(@cnt_DocLineLinks AS NVARCHAR(20));
    PRINT N'  -> DocumentConversionLinks : ' + CAST(@cnt_DocConvLinks AS NVARCHAR(20));
    PRINT N'  -> DocumentLines           : ' + CAST(@cnt_DocLines AS NVARCHAR(20));
    PRINT N'  -> DocumentHeaders         : ' + CAST(@cnt_DocHeaders AS NVARCHAR(20));
    PRINT N'  -> ItemSerialMovements     : ' + CAST(@cnt_SerialMoves AS NVARCHAR(20));
    PRINT N'  -> ItemSerialNumbers       : ' + CAST(@cnt_Serials AS NVARCHAR(20));
    PRINT N'  -> VoucherLines            : ' + CAST(@cnt_VchrLines AS NVARCHAR(20));
    PRINT N'  -> VoucherHeaders          : ' + CAST(@cnt_VchrHeaders AS NVARCHAR(20));
    PRINT N'  -> JournalEntryLines       : ' + CAST(@cnt_JELines AS NVARCHAR(20));
    PRINT N'  -> JournalEntryHeaders     : ' + CAST(@cnt_JEHeaders AS NVARCHAR(20));
    PRINT N'  -> AuditLogs               : ' + CAST(@cnt_Audit AS NVARCHAR(20));

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[2/5] Disabling Foreign Key Constraints Temporarily (تعطيل القيود مؤقتاً)...';

    ALTER TABLE [DocumentAdjustmentLines] NOCHECK CONSTRAINT ALL;
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

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[3/5] Deleting All Transactional Records (جاري مسح بيانات الحركات)...';

    -- 1. فك ارتباط القيود بالمستندات والسندات أولاً لتفادي أي قيد دائري
    UPDATE [DocumentHeaders] SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;
    UPDATE [VoucherHeaders]  SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;

    -- 2. مسح جداول تفاصيل المستندات والمخازن
    DELETE FROM [DocumentAdjustmentLines];
    PRINT N'  [OK] Cleared: DocumentAdjustmentLines';

    DELETE FROM [DocumentLineSerials];
    PRINT N'  [OK] Cleared: DocumentLineSerials';

    DELETE FROM [DocumentLineLinks];
    PRINT N'  [OK] Cleared: DocumentLineLinks';

    DELETE FROM [DocumentConversionLinks];
    PRINT N'  [OK] Cleared: DocumentConversionLinks';

    DELETE FROM [DocumentLines];
    PRINT N'  [OK] Cleared: DocumentLines';

    DELETE FROM [DocumentHeaders];
    PRINT N'  [OK] Cleared: DocumentHeaders';

    -- 3. مسح حركات وسيريال الأصناف
    DELETE FROM [ItemSerialMovements];
    PRINT N'  [OK] Cleared: ItemSerialMovements';

    DELETE FROM [ItemSerialNumbers];
    PRINT N'  [OK] Cleared: ItemSerialNumbers';

    -- 4. مسح سندات القبض والصرف
    DELETE FROM [VoucherLines];
    PRINT N'  [OK] Cleared: VoucherLines';

    DELETE FROM [VoucherHeaders];
    PRINT N'  [OK] Cleared: VoucherHeaders';

    -- 5. مسح قيود اليومية العامة
    DELETE FROM [JournalEntryLines];
    PRINT N'  [OK] Cleared: JournalEntryLines';

    DELETE FROM [JournalEntryHeaders];
    PRINT N'  [OK] Cleared: JournalEntryHeaders';

    -- 6. مسح سجل التدقيق
    DELETE FROM [AuditLogs];
    PRINT N'  [OK] Cleared: AuditLogs';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[4/5] Resetting Identity Seeds (إعادة تصفير العدادات التلقائية لتبدأ من 1)...';

    DBCC CHECKIDENT ('DocumentAdjustmentLines', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLineSerials',     RESEED, 0);
    DBCC CHECKIDENT ('DocumentLineLinks',       RESEED, 0);
    DBCC CHECKIDENT ('DocumentConversionLinks', RESEED, 0);
    DBCC CHECKIDENT ('DocumentLines',           RESEED, 0);
    DBCC CHECKIDENT ('DocumentHeaders',         RESEED, 0);
    DBCC CHECKIDENT ('ItemSerialMovements',     RESEED, 0);
    DBCC CHECKIDENT ('ItemSerialNumbers',       RESEED, 0);
    DBCC CHECKIDENT ('VoucherLines',            RESEED, 0);
    DBCC CHECKIDENT ('VoucherHeaders',          RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryLines',       RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryHeaders',     RESEED, 0);
    DBCC CHECKIDENT ('AuditLogs',               RESEED, 0);

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[5/5] Re-enabling and Validating Foreign Key Constraints (إعادة تفعيل القيود)...';

    ALTER TABLE [JournalEntryHeaders]     WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryLines]       WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherHeaders]          WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherLines]            WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [ItemSerialNumbers]       WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [ItemSerialMovements]     WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentHeaders]         WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLines]           WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentConversionLinks] WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLineLinks]       WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentLineSerials]     WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentAdjustmentLines] WITH CHECK CHECK CONSTRAINT ALL;

    COMMIT TRANSACTION;

    PRINT N'==============================================================================';
    PRINT N'                  VERIFICATION & FINAL AUDIT SUMMARY                          ';
    PRINT N'==============================================================================';

    -- جدول التأكيد النهائي للحركات (يجب أن تكون جميعها 0)
    SELECT 'DocumentHeaders'         AS TableName, COUNT(*) AS RemainingCount FROM [DocumentHeaders]
    UNION ALL SELECT 'DocumentLines',               COUNT(*) FROM [DocumentLines]
    UNION ALL SELECT 'DocumentLineSerials',         COUNT(*) FROM [DocumentLineSerials]
    UNION ALL SELECT 'DocumentAdjustmentLines',     COUNT(*) FROM [DocumentAdjustmentLines]
    UNION ALL SELECT 'DocumentConversionLinks',     COUNT(*) FROM [DocumentConversionLinks]
    UNION ALL SELECT 'DocumentLineLinks',           COUNT(*) FROM [DocumentLineLinks]
    UNION ALL SELECT 'VoucherHeaders',              COUNT(*) FROM [VoucherHeaders]
    UNION ALL SELECT 'VoucherLines',                COUNT(*) FROM [VoucherLines]
    UNION ALL SELECT 'JournalEntryHeaders',         COUNT(*) FROM [JournalEntryHeaders]
    UNION ALL SELECT 'JournalEntryLines',           COUNT(*) FROM [JournalEntryLines]
    UNION ALL SELECT 'ItemSerialNumbers',           COUNT(*) FROM [ItemSerialNumbers]
    UNION ALL SELECT 'ItemSerialMovements',         COUNT(*) FROM [ItemSerialMovements]
    UNION ALL SELECT 'AuditLogs',                   COUNT(*) FROM [AuditLogs];

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'MASTER DATA HEALTH CHECK (التأكد من سلامة وبقاء البيانات الأساسية كاملة):';
    
    SELECT 'GLAccounts (شجرة الحسابات)'       AS MasterTable, COUNT(*) AS PreservedCount FROM [GLAccounts]
    UNION ALL SELECT 'Companies (الشركات)',               COUNT(*) FROM [Companies]
    UNION ALL SELECT 'Branches (الفروع)',                 COUNT(*) FROM [Branches]
    UNION ALL SELECT 'FiscalYears (السنوات المالية)',     COUNT(*) FROM [FiscalYears]
    UNION ALL SELECT 'FiscalPeriods (الفترات المالية)',   COUNT(*) FROM [FiscalPeriods]
    UNION ALL SELECT 'Customers (العملاء)',               COUNT(*) FROM [Customers]
    UNION ALL SELECT 'Vendors (الموردين)',                COUNT(*) FROM [Vendors]
    UNION ALL SELECT 'Items (الأصناف)',                   COUNT(*) FROM [Items]
    UNION ALL SELECT 'Warehouses (المستودعات)',           COUNT(*) FROM [Warehouses]
    UNION ALL SELECT 'Banks (البنوك)',                    COUNT(*) FROM [Banks]
    UNION ALL SELECT 'Currencies (العملات)',              COUNT(*) FROM [Currencies]
    UNION ALL SELECT 'CostCenters (مراكز التكلفة)',       COUNT(*) FROM [CostCenters]
    UNION ALL SELECT 'TransactionTypes (أنواع الحركات)',  COUNT(*) FROM [TransactionTypes]
    UNION ALL SELECT 'AspNetUsers (المستخدمين)',          COUNT(*) FROM [AspNetUsers];

    PRINT N'------------------------------------------------------------------------------';
    PRINT N' SUCCESS: All transaction data has been completely and safely wiped!';
    PRINT N' النظام الآن جاهز 100% لبدء دورة اختبارات جديدة بأرقام ومسلسلات تبدأ من 1.';
    PRINT N'==============================================================================';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT N'******************************************************************************';
    PRINT N' ERROR OCCURRED! All changes were completely rolled back.';
    PRINT N' Error Number  : ' + CAST(ERROR_NUMBER() AS NVARCHAR(20));
    PRINT N' Error Message : ' + ERROR_MESSAGE();
    PRINT N' Error Line    : ' + CAST(ERROR_LINE() AS NVARCHAR(20));
    PRINT N'******************************************************************************';
    THROW;
END CATCH;
GO
