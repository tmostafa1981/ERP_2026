/****************************************************************************************************
  ملف رقم (3): مطابقة أرصدة المخزون، الحسابات التراكمية، ومنع البيع بالسالب
  File: 03_Validation_InventoryConsistencyAndRunningTotals.sql
  Author: Performance & QA Engineer (Antigravity Agent)
  Target: SQL Server 2019 / 2022
  Description:
    يتحقق هذا الاستعلام الرياضي الشامل من:
    1. عدم هبوط أي رصيد مخزني إلى السالب (Negative Inventory Violation).
    2. مطابقة مجموع الكميات المباعة في سطور الفواتير (DocumentLines) مع الفرق الدقيق بين
       (الرصيد الافتتاحي - الرصيد الحالي الفعلي) في جدول ItemStocks.
    3. التحقق من تطابق إجماليات الفاتورة (Header Net Total) مع مجموع سطورها (SUM of Line Totals).
****************************************************************************************************/

-- USE [db_ab95d6_sewtec2026]; -- أو اسم قاعدة البيانات الحالية
-- GO

PRINT '================================================================================';
PRINT '           تقرير التدقيق الرياضي للمخزون والحسابات التراكمية بعد اختبار التحمل      ';
PRINT '================================================================================';

-- ================================================================================
-- الفحص الأول: الكشف عن أي رصيد مخزني سالب (Negative Stock Balance Check)
-- النتيجة المتوقعة: 0 صفوف (Empty Result)
-- ================================================================================
SELECT 
    BranchId,
    WarehouseId,
    ItemId,
    InitialBalance,
    CurrentBalance,
    'CRITICAL RACE CONDITION: NEGATIVE STOCK DETECTED!' AS ViolationMessage
FROM dbo.ItemStocks
WHERE CurrentBalance < 0
ORDER BY BranchId, ItemId;

IF @@ROWCOUNT = 0
    PRINT '-> [نجاح الفحص 1]: جميع أرصدة الأصناف موجبة؛ لا يوجد أي بيع بالسالب (Zero Negative Balances).';
ELSE
    PRINT '-> [فشل حرج]: تم رصد أصناف برصيد سالب نتيجة تداخل الأقفال أو تسريب في المعاملات!';


-- ================================================================================
-- الفحص الثاني: مطابقة الكميات المباعة فعلياً مع الفرق المخزني (Stock Balance Reconciliation)
-- المعادلة: [InitialBalance - TotalQuantitySold = CurrentBalance]
-- النتيجة المتوقعة: Variance = 0.0000 لجميع الأصناف والفروع
-- ================================================================================
WITH SoldSummary AS
(
    SELECT 
        h.BranchId,
        l.WarehouseId,
        l.ItemId,
        SUM(l.Quantity) AS TotalQtySold,
        COUNT(DISTINCT h.Id) AS InvoicesCount
    FROM dbo.DocumentHeaders h
    INNER JOIN dbo.DocumentLines l ON h.Id = l.DocumentHeaderId
    GROUP BY h.BranchId, l.WarehouseId, l.ItemId
)
SELECT 
    s.BranchId,
    s.WarehouseId,
    s.ItemId,
    s.InitialBalance,
    ISNULL(sold.TotalQtySold, 0.0000) AS TotalQuantitySold,
    (s.InitialBalance - ISNULL(sold.TotalQtySold, 0.0000)) AS ExpectedCurrentBalance,
    s.CurrentBalance AS ActualCurrentBalance,
    (s.CurrentBalance - (s.InitialBalance - ISNULL(sold.TotalQtySold, 0.0000))) AS BalanceVariance,
    CASE 
        WHEN (s.CurrentBalance - (s.InitialBalance - ISNULL(sold.TotalQtySold, 0.0000))) = 0.0000 
            THEN '100% RECONCILED (تطابق تام ومثالي)'
        ELSE 'MISMATCH DETECTED (خلل وتضارب في حركة المخزون)'
    END AS ReconciliationStatus
FROM dbo.ItemStocks s
LEFT JOIN SoldSummary sold 
    ON s.BranchId = sold.BranchId 
   AND s.WarehouseId = sold.WarehouseId 
   AND s.ItemId = sold.ItemId
WHERE (s.CurrentBalance - (s.InitialBalance - ISNULL(sold.TotalQtySold, 0.0000))) <> 0.0000
   OR sold.TotalQtySold > 0
ORDER BY ABS(s.CurrentBalance - (s.InitialBalance - ISNULL(sold.TotalQtySold, 0.0000))) DESC, s.BranchId, s.ItemId;

IF @@ROWCOUNT = 0
    PRINT '-> [تنبيه]: لم تسجل أي حركات بيع على الأصناف أو الجداول فارغة.';
ELSE
    PRINT '-> [ملاحظة]: يرجى فحص عمود ReconciliationStatus للتأكد من حالة التطابق لجميع الأصناف.';


-- ================================================================================
-- الفحص الثالث: مطابقة إجمالي الفاتورة مع مجموع سطورها (Header Total vs Lines Sum Integrity)
-- المعادلة: TotalAmountNet == SUM(LineTotal)
-- النتيجة المتوقعة: 0 صفوف (Empty Result)
-- ================================================================================
WITH InvoiceLinesAudit AS
(
    SELECT 
        DocumentHeaderId,
        SUM(Quantity * UnitPrice) AS CalculatedBeforeTax,
        SUM(DiscountAmount) AS CalculatedDiscount,
        SUM(TaxAmount) AS CalculatedTax,
        SUM(LineTotal) AS CalculatedNetLinesSum
    FROM dbo.DocumentLines
    GROUP BY DocumentHeaderId
)
SELECT 
    h.Id AS HeaderId,
    h.BranchId,
    h.DocumentNumber,
    h.TotalAmountNet AS HeaderRecordedNet,
    a.CalculatedNetLinesSum AS LinesAggregatedNet,
    (h.TotalAmountNet - a.CalculatedNetLinesSum) AS DifferenceAmount,
    'INTEGRITY VIOLATION: Header Net does not match sum of Line totals' AS ErrorDescription
FROM dbo.DocumentHeaders h
INNER JOIN InvoiceLinesAudit a ON h.Id = a.DocumentHeaderId
WHERE ABS(h.TotalAmountNet - a.CalculatedNetLinesSum) > 0.0001
ORDER BY h.BranchId, h.Id;

IF @@ROWCOUNT = 0
    PRINT '-> [نجاح الفحص 3]: تكامل الحسابات التراكمية سليم بنسبة 100%؛ جميع إجماليات الفواتير مطابقة تماماً لمجموع سطورها.';
ELSE
    PRINT '-> [تحذير]: يوجد تباين بين إجماليات الفواتير ومجموع سطورها!';

PRINT '================================================================================';
GO
