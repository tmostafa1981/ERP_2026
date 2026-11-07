-- ==============================================================================================
-- ERP 2026 Platform - Purge Chart of Accounts (اسكريبت مسح وتصفير شجرة الحسابات منفصلاً)
-- Target Environment: Microsoft SQL Server 2022+ / Azure SQL Database
-- Description:
--   يقوم هذا الاسكريبت بمسح شجرة الحسابات بالكامل (GLAccounts) وتصفير ترقيمها لتبدأ من 1 مجدداً.
--
-- آلية حل خطأ قيود المفاتيح الأجنبية (FK Conflict Resolution):
--   الخطأ (FK_DocumentAdjustments_GLAccounts) يحدث لأن جدول تعريف الخصومات والمصاريف الإضافية
--   (DocumentAdjustments) وكذلك سطور التسويات (DocumentAdjustmentLines) مرتبطة بحسابات مالية إلزامية.
--   لذلك يقوم هذا الاسكريبت بالترتيب الدقيق التالي:
--     1. فك ارتباط الحسابات من العملاء والموردين وتصنيفاتهم وأنواع الحركات (Set NULL).
--     2. فك ترابط الشجرة الهرمية (UPDATE GLAccounts SET ParentId = NULL).
--     3. مسح جداول التسويات والمصاريف الإضافية (DocumentAdjustmentLines, TransactionTypeAdjustments, DocumentAdjustments).
--     4. مسح قيود اليومية وسندات القبض والصرف (JournalEntries, Vouchers) وصلاحيات الحسابات.
--     5. مسح شجرة الحسابات (DELETE FROM GLAccounts).
--     6. تصفير العداد التلقائي (Identity Reseed) ليبدأ من الرقم 1.
-- ==============================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'==============================================================================';
PRINT N'         ERP 2026 - PURGE CHART OF ACCOUNTS (مسح وتصفير شجرة الحسابات)        ';
PRINT N'==============================================================================';
PRINT N'Database Target : ' + DB_NAME();
PRINT N'Server Name     : ' + @@SERVERNAME;
PRINT N'Execution Date  : ' + CONVERT(NVARCHAR(30), GETDATE(), 120);
PRINT N'------------------------------------------------------------------------------';

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT N'[1/7] فحص السجلات الحالية...';

    DECLARE @cnt_GLAccounts    INT = (SELECT COUNT(*) FROM [GLAccounts]);
    DECLARE @cnt_AccPerms      INT = (SELECT COUNT(*) FROM [AccountPermissions]);
    DECLARE @cnt_DocAdjLines   INT = (SELECT COUNT(*) FROM [DocumentAdjustmentLines]);
    DECLARE @cnt_DocAdjusts    INT = (SELECT COUNT(*) FROM [DocumentAdjustments]);
    DECLARE @cnt_JELines       INT = (SELECT COUNT(*) FROM [JournalEntryLines]);
    DECLARE @cnt_JEHeaders     INT = (SELECT COUNT(*) FROM [JournalEntryHeaders]);
    DECLARE @cnt_VchrLines     INT = (SELECT COUNT(*) FROM [VoucherLines]);
    DECLARE @cnt_VchrHeaders   INT = (SELECT COUNT(*) FROM [VoucherHeaders]);

    PRINT N'  -> شجرة الحسابات (GLAccounts)              : ' + CAST(@cnt_GLAccounts AS NVARCHAR(20));
    PRINT N'  -> بنود المصاريف والخصومات (DocAdjustments) : ' + CAST(@cnt_DocAdjusts AS NVARCHAR(20));
    PRINT N'  -> سطور تسويات المستندات (DocAdjLines)     : ' + CAST(@cnt_DocAdjLines AS NVARCHAR(20));
    PRINT N'  -> قيود اليومية (Journal Entries)          : ' + CAST(@cnt_JEHeaders AS NVARCHAR(20));
    PRINT N'  -> سندات القبض والصرف (Vouchers)            : ' + CAST(@cnt_VchrHeaders AS NVARCHAR(20));

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[2/7] فك ارتباط الحسابات بالحقول القابلة للقيمة الفارغة (Set NULL)...';

    -- 1. فك ارتباط الحساب المالي من بطاقات العملاء والموردين
    UPDATE [Customers] SET [FinancialAccountId] = NULL WHERE [FinancialAccountId] IS NOT NULL;
    UPDATE [Vendors]   SET [FinancialAccountId] = NULL WHERE [FinancialAccountId] IS NOT NULL;
    PRINT N'  [OK] تم فك ارتباط حسابات الرقابة من العملاء والموردين.';

    -- 2. فك ارتباط حسابات المراقبة من تصنيفات العملاء والموردين
    UPDATE [CustomerCategories] SET [ControlAccountId] = NULL WHERE [ControlAccountId] IS NOT NULL;
    UPDATE [VendorCategories]   SET [ControlAccountId] = NULL WHERE [ControlAccountId] IS NOT NULL;
    PRINT N'  [OK] تم فك ارتباط حسابات المراقبة من تصنيفات العملاء والموردين.';

    -- 3. تصفير الحسابات الافتراضية في أنواع الحركات
    UPDATE [TransactionTypes]
    SET [DefaultCashBankAccountId]  = NULL,
        [DefaultDebitAccountId]     = NULL,
        [DefaultCreditAccountId]    = NULL,
        [DefaultTaxAccountId]       = NULL,
        [DefaultDiscountAccountId]  = NULL;
    PRINT N'  [OK] تم تصفير توجيهات الحسابات الافتراضية في أنواع الحركات.';

    -- 4. فك ارتباط الحسابات والقيود من المستندات والسندات
    UPDATE [DocumentHeaders] SET [CashBankAccountId] = NULL WHERE [CashBankAccountId] IS NOT NULL;
    UPDATE [DocumentHeaders] SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;
    UPDATE [VoucherHeaders]  SET [JournalEntryHeaderId] = NULL WHERE [JournalEntryHeaderId] IS NOT NULL;

    -- 5. كسر الهيكل الشجري للحسابات لتفادي خطأ Self-Referencing Foreign Key
    UPDATE [GLAccounts] SET [ParentId] = NULL WHERE [ParentId] IS NOT NULL;
    PRINT N'  [OK] تم فك ترابط الشجرة الهرمية للحسابات (تصفير ParentId).';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[3/7] تعطيل قيود المفاتيح الأجنبية المرتبطة بالحسابات مؤقتاً...';

    -- تعطيل كل قيد يشير لجدول GLAccounts ديناميكياً
    DECLARE @sql_disable NVARCHAR(MAX) = N'';
    SELECT @sql_disable += N'ALTER TABLE [' + OBJECT_SCHEMA_NAME(parent_object_id) + N'].[' + OBJECT_NAME(parent_object_id) + N'] NOCHECK CONSTRAINT [' + name + N'];' + CHAR(13)
    FROM sys.foreign_keys
    WHERE referenced_object_id = OBJECT_ID('GLAccounts');
    
    IF LEN(@sql_disable) > 0
        EXEC sp_executesql @sql_disable;

    ALTER TABLE [DocumentAdjustmentLines]     NOCHECK CONSTRAINT ALL;
    ALTER TABLE [TransactionTypeAdjustments]   NOCHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentAdjustments]         NOCHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherLines]                NOCHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherHeaders]              NOCHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryLines]           NOCHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryHeaders]         NOCHECK CONSTRAINT ALL;
    ALTER TABLE [AccountPermissions]          NOCHECK CONSTRAINT ALL;
    ALTER TABLE [GLAccounts]                  NOCHECK CONSTRAINT ALL;

    PRINT N'  [OK] تم تعطيل قيود المفاتيح الأجنبية بنجاح.';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[4/7] مسح الجداول المرتبطة إجبارياً بحسابات شجرة الحسابات...';

    -- 1. مسح سطور تسويات المستندات
    DELETE FROM [DocumentAdjustmentLines];
    PRINT N'  [OK] تم مسح سطور تسويات المستندات (DocumentAdjustmentLines).';

    -- 2. مسح روابط أنواع الحركات بالمصاريف الإضافية
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TransactionTypeAdjustments')
    BEGIN
        DELETE FROM [TransactionTypeAdjustments];
        PRINT N'  [OK] تم مسح روابط أنواع الحركات بالخصومات والمصاريف (TransactionTypeAdjustments).';
    END

    -- 3. مسح بنود المصاريف الإضافية والخصومات المرتبطة بالحسابات (FK_DocumentAdjustments_GLAccounts)
    DELETE FROM [DocumentAdjustments];
    PRINT N'  [OK] تم مسح بنود المصاريف والخصومات (DocumentAdjustments).';

    -- 4. مسح سندات القبض والصرف
    DELETE FROM [VoucherLines];
    DELETE FROM [VoucherHeaders];
    PRINT N'  [OK] تم مسح سندات القبض والصرف (Vouchers).';

    -- 5. مسح قيود اليومية العامة
    DELETE FROM [JournalEntryLines];
    DELETE FROM [JournalEntryHeaders];
    PRINT N'  [OK] تم مسح قيود اليومية العامة (Journal Entries).';

    -- 6. مسح صلاحيات الحسابات
    DELETE FROM [AccountPermissions];
    PRINT N'  [OK] تم مسح صلاحيات الحسابات (AccountPermissions).';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[5/7] مسح شجرة الحسابات (GLAccounts)...';

    DELETE FROM [GLAccounts];
    PRINT N'  [OK] تم مسح شجرة الحسابات بالكامل بنجاح.';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[6/7] تصفير عدادات الترقيم التلقائي لتبدأ من 1 (Identity Reseed)...';

    DBCC CHECKIDENT ('GLAccounts', RESEED, 0);
    DBCC CHECKIDENT ('AccountPermissions', RESEED, 0);
    DBCC CHECKIDENT ('DocumentAdjustments', RESEED, 0);
    DBCC CHECKIDENT ('DocumentAdjustmentLines', RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryHeaders', RESEED, 0);
    DBCC CHECKIDENT ('JournalEntryLines', RESEED, 0);
    DBCC CHECKIDENT ('VoucherHeaders', RESEED, 0);
    DBCC CHECKIDENT ('VoucherLines', RESEED, 0);

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TransactionTypeAdjustments')
    BEGIN
        IF COLUMNPROPERTY(OBJECT_ID('TransactionTypeAdjustments'), 'Id', 'IsIdentity') = 1
            DBCC CHECKIDENT ('TransactionTypeAdjustments', RESEED, 0);
    END

    PRINT N'  [OK] تم إعادة تصفير عدادات الترقيم التلقائي لتبدأ من 1.';

    PRINT N'------------------------------------------------------------------------------';
    PRINT N'[7/7] إعادة تفعيل قيود المفاتيح الأجنبية وفحص التكامل...';

    -- إعادة تفعيل كل قيد يشير لجدول GLAccounts ديناميكياً
    DECLARE @sql_enable NVARCHAR(MAX) = N'';
    SELECT @sql_enable += N'ALTER TABLE [' + OBJECT_SCHEMA_NAME(parent_object_id) + N'].[' + OBJECT_NAME(parent_object_id) + N'] WITH CHECK CHECK CONSTRAINT [' + name + N'];' + CHAR(13)
    FROM sys.foreign_keys
    WHERE referenced_object_id = OBJECT_ID('GLAccounts');

    IF LEN(@sql_enable) > 0
        EXEC sp_executesql @sql_enable;

    ALTER TABLE [DocumentAdjustmentLines]     CHECK CONSTRAINT ALL;
    ALTER TABLE [DocumentAdjustments]         CHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherLines]                CHECK CONSTRAINT ALL;
    ALTER TABLE [VoucherHeaders]              CHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryLines]           CHECK CONSTRAINT ALL;
    ALTER TABLE [JournalEntryHeaders]         CHECK CONSTRAINT ALL;
    ALTER TABLE [AccountPermissions]          CHECK CONSTRAINT ALL;
    ALTER TABLE [GLAccounts]                  CHECK CONSTRAINT ALL;

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TransactionTypeAdjustments')
        ALTER TABLE [TransactionTypeAdjustments] CHECK CONSTRAINT ALL;

    COMMIT TRANSACTION;

    PRINT N'==============================================================================';
    PRINT N'         SUCCESS! تم مسح وتصفير شجرة الحسابات بنجاح تام                       ';
    PRINT N'==============================================================================';

    -- التحقق النهائي من الأعداد
    SELECT 
        'شجرة الحسابات (GLAccounts)' AS [الجدول], (SELECT COUNT(*) FROM [GLAccounts]) AS [العدد الحالي]
    UNION ALL SELECT 'صلاحيات الحسابات (AccountPermissions)', (SELECT COUNT(*) FROM [AccountPermissions])
    UNION ALL SELECT 'بنود المصاريف والخصومات (DocumentAdjustments)', (SELECT COUNT(*) FROM [DocumentAdjustments])
    UNION ALL SELECT 'قيود اليومية (JournalEntryHeaders)', (SELECT COUNT(*) FROM [JournalEntryHeaders])
    UNION ALL SELECT 'سندات القبض والصرف (VoucherHeaders)', (SELECT COUNT(*) FROM [VoucherHeaders])
    UNION ALL SELECT 'العملاء (Customers - محفوظين بالكامل)', (SELECT COUNT(*) FROM [Customers])
    UNION ALL SELECT 'الموردين (Vendors - محفوظين بالكامل)', (SELECT COUNT(*) FROM [Vendors])
    UNION ALL SELECT 'الأصناف (Items - محفوظة بالكامل)', (SELECT COUNT(*) FROM [Items]);

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
