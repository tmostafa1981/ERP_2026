/****************************************************************************************************
  ملف رقم (1): إعداد جداول الاختبار، نوع الجدول الممرر (TVP)، والإجراء المخزن عالي التزامن
  File: 01_Setup_LoadTest_Database_Objects.sql
  Author: Performance & QA Engineer (Antigravity Agent)
  Target: SQL Server 2019 / 2022
  Description:
    يقوم بتهيئة جداول الاختبار للفواتير والمخزون والترقيم التلقائي، وإنشاء الإجراء المخزن 
    dbo.sp_CreateInvoiceWithItems الذي يستخدم تقنيات الأقفال الدقيقة (UPDLOCK, ROWLOCK)
    لمنع تضارب الترقيم والبيع بالسالب عند وصول 50 طلباً في نفس الثانية عبر 42 فرعاً.
****************************************************************************************************/

-- 1. التأكد من وجود قاعدة البيانات
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'ERP_2026_DB')
BEGIN
    CREATE DATABASE ERP_2026_DB;
END
GO

USE ERP_2026_DB;
GO

-- 2. إنشاء نوع الجدول كمعامل (Table-Valued Parameter Type) لسطور الفاتورة
IF NOT EXISTS (SELECT 1 FROM sys.types WHERE name = 'InvoiceLineTableType' AND is_table_type = 1)
BEGIN
    CREATE TYPE dbo.InvoiceLineTableType AS TABLE
    (
        LineNumber      INT NOT NULL,
        ItemId          INT NOT NULL,
        UnitId          INT NOT NULL,
        WarehouseId     INT NOT NULL,
        Quantity        DECIMAL(18, 4) NOT NULL,
        UnitPrice       DECIMAL(18, 4) NOT NULL,
        DiscountAmount  DECIMAL(18, 4) NOT NULL DEFAULT (0),
        TaxRate         DECIMAL(18, 4) NOT NULL DEFAULT (0.14),
        TaxAmount       DECIMAL(18, 4) NOT NULL DEFAULT (0),
        LineTotal       DECIMAL(18, 4) NOT NULL
    );
END
GO

-- 3. جدول تسلسل الأرقام للفروع (CodeSequences)
IF OBJECT_ID('dbo.CodeSequences', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CodeSequences
    (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        EntityName    NVARCHAR(100) NOT NULL,
        BranchId      INT NOT NULL,
        Prefix        NVARCHAR(20) NOT NULL,
        LastNumber    BIGINT NOT NULL DEFAULT(0),
        PaddingLength INT NOT NULL DEFAULT(6),
        UpdatedAt     DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT UQ_CodeSequences_Entity_Branch UNIQUE (EntityName, BranchId)
    );
END
GO

-- 4. جدول أرصدة المخزون (ItemStocks) لاختبار منع البيع بالسالب
IF OBJECT_ID('dbo.ItemStocks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ItemStocks
    (
        Id             INT IDENTITY(1,1) PRIMARY KEY,
        BranchId       INT NOT NULL,
        WarehouseId    INT NOT NULL,
        ItemId         INT NOT NULL,
        InitialBalance DECIMAL(18, 4) NOT NULL DEFAULT (10000.0000), -- رصيد افتتاحي كبير للاختبار
        CurrentBalance DECIMAL(18, 4) NOT NULL DEFAULT (10000.0000), -- الرصيد الحالي المتغير
        ReservedQty    DECIMAL(18, 4) NOT NULL DEFAULT (0.0000),
        RowVersion     ROWVERSION,
        CONSTRAINT UQ_ItemStocks_Branch_Warehouse_Item UNIQUE (BranchId, WarehouseId, ItemId)
    );
END
GO

-- 5. جدول ترويسة الفواتير (DocumentHeaders / Invoices)
IF OBJECT_ID('dbo.DocumentHeaders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentHeaders
    (
        Id                   INT IDENTITY(1,1) PRIMARY KEY,
        BranchId             INT NOT NULL,
        FiscalYearId         INT NOT NULL,
        TransactionTypeId    INT NOT NULL,
        DocumentNumber       NVARCHAR(50) NOT NULL,
        DocumentDate         DATETIME2 NOT NULL,
        CustomerId           INT NOT NULL,
        WarehouseId          INT NOT NULL,
        CurrencyId           INT NOT NULL DEFAULT (1),
        ExchangeRate         DECIMAL(18, 6) NOT NULL DEFAULT (1.0),
        PaymentMethod        INT NOT NULL DEFAULT (1),
        TotalAmountBeforeTax DECIMAL(18, 4) NOT NULL,
        DiscountAmount       DECIMAL(18, 4) NOT NULL,
        TaxAmount            DECIMAL(18, 4) NOT NULL,
        TotalAmountNet       DECIMAL(18, 4) NOT NULL,
        Notes                NVARCHAR(500) NULL,
        CreatedByUserId      NVARCHAR(100) NOT NULL,
        CreatedAt            DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_DocumentHeaders_Branch_Number UNIQUE (BranchId, DocumentNumber)
    );
END
GO

-- 6. جدول سطور الفواتير (DocumentLines / InvoiceLines)
IF OBJECT_ID('dbo.DocumentLines', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentLines
    (
        Id               INT IDENTITY(1,1) PRIMARY KEY,
        DocumentHeaderId INT NOT NULL,
        LineNumber       INT NOT NULL,
        ItemId           INT NOT NULL,
        UnitId           INT NOT NULL,
        WarehouseId      INT NOT NULL,
        Quantity         DECIMAL(18, 4) NOT NULL,
        UnitPrice        DECIMAL(18, 4) NOT NULL,
        DiscountAmount   DECIMAL(18, 4) NOT NULL DEFAULT(0),
        TaxRate          DECIMAL(18, 4) NOT NULL DEFAULT(0.14),
        TaxAmount        DECIMAL(18, 4) NOT NULL DEFAULT(0),
        LineTotal        DECIMAL(18, 4) NOT NULL,
        CreatedAt        DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_DocumentLines_Header FOREIGN KEY (DocumentHeaderId) REFERENCES dbo.DocumentHeaders(Id) ON DELETE CASCADE
    );
END
GO

-- 7. تغذية بيانات أولية لـ 42 فرعاً في جداول التسلسل وأرصدة المخزون
-- تهيئة عدادات التسلسل للفروع من 1 إلى 42
DECLARE @BranchCounter INT = 1;
WHILE @BranchCounter <= 42
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.CodeSequences WHERE EntityName = 'SalesInvoice' AND BranchId = @BranchCounter)
    BEGIN
        INSERT INTO dbo.CodeSequences (EntityName, BranchId, Prefix, LastNumber, PaddingLength)
        VALUES ('SalesInvoice', @BranchCounter, CONCAT('INV-B', RIGHT(CONCAT('00', @BranchCounter), 2), '-'), 0, 6);
    END;

    -- تهيئة مخزون لـ 30 صنف لكل فرع ومستودع
    DECLARE @ItemCounter INT = 1;
    WHILE @ItemCounter <= 30
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.ItemStocks WHERE BranchId = @BranchCounter AND WarehouseId = @BranchCounter AND ItemId = @ItemCounter)
        BEGIN
            INSERT INTO dbo.ItemStocks (BranchId, WarehouseId, ItemId, InitialBalance, CurrentBalance)
            VALUES (@BranchCounter, @BranchCounter, @ItemCounter, 5000.0000, 5000.0000);
        END;
        SET @ItemCounter = @ItemCounter + 1;
    END;

    SET @BranchCounter = @BranchCounter + 1;
END;
GO

-- 8. إنشاء الإجراء المخزن عالي التحمل لحفظ الفواتير والتسلسل وخصم المخزون
CREATE OR ALTER PROCEDURE dbo.sp_CreateInvoiceWithItems
    @BranchId             INT,
    @FiscalYearId         INT,
    @TransactionTypeId    INT,
    @DocumentDate         DATETIME2,
    @CustomerId           INT,
    @WarehouseId          INT,
    @CurrencyId           INT,
    @ExchangeRate         DECIMAL(18, 6),
    @PaymentMethod        INT,
    @TotalAmountBeforeTax DECIMAL(18, 4),
    @DiscountAmount       DECIMAL(18, 4),
    @TaxAmount            DECIMAL(18, 4),
    @TotalAmountNet       DECIMAL(18, 4),
    @Notes                NVARCHAR(500),
    @CreatedByUserId      NVARCHAR(100),
    @Lines                dbo.InvoiceLineTableType READONLY,
    @GeneratedDocNumber   NVARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- معالجة الأخطاء والتراجع الفوري
    BEGIN TRY
        BEGIN TRANSACTION;

        -- أ. توليد الرقم المتسلسل للفاتورة باستخدام قفل ذري دقيق (UPDLOCK, ROWLOCK)
        -- يمنع تضارب رقم الفاتورة بين الخيوط المتزامنة لنفس الفرع نهائياً
        DECLARE @Prefix NVARCHAR(20);
        DECLARE @NextNumber BIGINT;
        DECLARE @Padding INT;

        SELECT 
            @Prefix = Prefix,
            @NextNumber = LastNumber + 1,
            @Padding = PaddingLength
        FROM dbo.CodeSequences WITH (UPDLOCK, ROWLOCK)
        WHERE EntityName = 'SalesInvoice' AND BranchId = @BranchId;

        IF @NextNumber IS NULL
        BEGIN
            -- في حالة عدم وجود السجل، نهيئه فوراً
            SET @Prefix = CONCAT('INV-B', RIGHT(CONCAT('00', @BranchId), 2), '-');
            SET @NextNumber = 1;
            SET @Padding = 6;
            
            INSERT INTO dbo.CodeSequences (EntityName, BranchId, Prefix, LastNumber, PaddingLength)
            VALUES ('SalesInvoice', @BranchId, @Prefix, 1, @Padding);
        END
        ELSE
        BEGIN
            UPDATE dbo.CodeSequences
            SET LastNumber = @NextNumber,
                UpdatedAt = SYSUTCDATETIME()
            WHERE EntityName = 'SalesInvoice' AND BranchId = @BranchId;
        END

        SET @GeneratedDocNumber = CONCAT(@Prefix, RIGHT(REPLICATE('0', @Padding) + CAST(@NextNumber AS NVARCHAR(20)), @Padding));

        -- ب. خصم المخزون والتحقق الذري من عدم حدوث رصيد بالسالب (Atomic Stock Decrement)
        -- نستخدم ترتيب الأصناف بالـ ItemId لمنع حدوث Deadlocks بين المعاملات التي تطلب نفس الأصناف بترتيب مختلف
        DECLARE @CurItemId INT;
        DECLARE @CurQty DECIMAL(18, 4);

        DECLARE stock_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT ItemId, SUM(Quantity) AS TotalQty
            FROM @Lines
            GROUP BY ItemId
            ORDER BY ItemId; -- الترتيب يحمي من الـ Deadlock الناتج عن تضارب ترتيب حجز الموارد

        OPEN stock_cursor;
        FETCH NEXT FROM stock_cursor INTO @CurItemId, @CurQty;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- خصم مع قفل حصري على مستوى السطر والتحقق من كفاية الرصيد
            UPDATE s WITH (UPDLOCK, ROWLOCK)
            SET s.CurrentBalance = s.CurrentBalance - @CurQty
            FROM dbo.ItemStocks s
            WHERE s.BranchId = @BranchId 
              AND s.WarehouseId = @WarehouseId 
              AND s.ItemId = @CurItemId
              AND s.CurrentBalance >= @CurQty; -- شرط الأمان: يمنع الرصيد السالب

            -- إذا لم يتم تحديث أي سطر، فهذا يعني أن الرصيد غير كافٍ أو الصنف غير موجود
            IF @@ROWCOUNT = 0
            BEGIN
                CLOSE stock_cursor;
                DEALLOCATE stock_cursor;
                RAISERROR(N'الرصيد غير كافٍ للصنف رقم %d في المستودع أو حدث تضارب في الرصيد.', 16, 1, @CurItemId);
                RETURN;
            END

            FETCH NEXT FROM stock_cursor INTO @CurItemId, @CurQty;
        END

        CLOSE stock_cursor;
        DEALLOCATE stock_cursor;

        -- ج. إدخال رأس الفاتورة (DocumentHeader)
        DECLARE @NewHeaderId INT;

        INSERT INTO dbo.DocumentHeaders
        (
            BranchId, FiscalYearId, TransactionTypeId, DocumentNumber, DocumentDate,
            CustomerId, WarehouseId, CurrencyId, ExchangeRate, PaymentMethod,
            TotalAmountBeforeTax, DiscountAmount, TaxAmount, TotalAmountNet,
            Notes, CreatedByUserId, CreatedAt
        )
        VALUES
        (
            @BranchId, @FiscalYearId, @TransactionTypeId, @GeneratedDocNumber, @DocumentDate,
            @CustomerId, @WarehouseId, @CurrencyId, @ExchangeRate, @PaymentMethod,
            @TotalAmountBeforeTax, @DiscountAmount, @TaxAmount, @TotalAmountNet,
            @Notes, @CreatedByUserId, SYSUTCDATETIME()
        );

        SET @NewHeaderId = SCOPE_IDENTITY();

        -- د. إدخال سطور الفاتورة (DocumentLines) دفعة واحدة
        INSERT INTO dbo.DocumentLines
        (
            DocumentHeaderId, LineNumber, ItemId, UnitId, WarehouseId,
            Quantity, UnitPrice, DiscountAmount, TaxRate, TaxAmount, LineTotal, CreatedAt
        )
        SELECT
            @NewHeaderId, LineNumber, ItemId, UnitId, WarehouseId,
            Quantity, UnitPrice, DiscountAmount, TaxRate, TaxAmount, LineTotal, SYSUTCDATETIME()
        FROM @Lines;

        -- هـ. تأكيد المعاملة
        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- إعادة رفع الخطأ ليلتقطه NBomber بدقة
        DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrNum INT = ERROR_NUMBER();
        DECLARE @ErrSev INT = ERROR_SEVERITY();
        DECLARE @ErrState INT = ERROR_STATE();

        RAISERROR(@ErrMsg, @ErrSev, @ErrState);
    END CATCH
END;
GO
