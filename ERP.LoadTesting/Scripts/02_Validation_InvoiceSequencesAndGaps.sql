/****************************************************************************************************
  ملف رقم (2): التحقق الشامل من تسلسل أرقام الفواتير واكتشاف التكرارات والفجوات (Sequence Gaps)
  File: 02_Validation_InvoiceSequencesAndGaps.sql
  Author: Performance & QA Engineer (Antigravity Agent)
  Target: SQL Server 2019 / 2022
  Description:
    يتم تشغيل هذا الاستعلام فور انتهاء اختبار التحمل للتحقق من:
    1. عدم وجود أي تكرار نهائياً في أرقام الفواتير داخل كل فرع من الفروع الـ 42.
    2. عدم وجود فجوات عددية (Sequence Gaps) ناتجة عن تسريب أرقام أو مشاكل في الـ Race Conditions.
    3. مطابقة آخر رقم تسلسلي مسجل في جدول CodeSequences مع أعلى رقم فعلي تم إدخاله في DocumentHeaders.
****************************************************************************************************/

-- USE [db_ab95d6_sewtec2026]; -- أو قاعدة البيانات الحالية
-- GO

PRINT '================================================================================';
PRINT '         تقرير فحص سلامة أرقام الفواتير والتسلسل عبر 42 فرعاً بعد اختبار التحمل     ';
PRINT '================================================================================';

-- ================================================================================
-- الفحص الأول: الكشف عن أي أرقام فواتير مكررة داخل نفس الفرع (Duplicate Invoice Numbers)
-- النتيجة المتوقعة: 0 صفوف (Empty Result)
-- ================================================================================
SELECT 
    BranchId,
    DocumentNumber,
    COUNT(*) AS DuplicateOccurrences,
    MIN(CreatedAt) AS FirstCreated,
    MAX(CreatedAt) AS LastCreated,
    'CRITICAL ERROR: DUPLICATE INVOICE NUMBER DETECTED!' AS AuditStatus
FROM dbo.DocumentHeaders
GROUP BY BranchId, DocumentNumber
HAVING COUNT(*) > 1
ORDER BY BranchId, DocumentNumber;

IF @@ROWCOUNT = 0
    PRINT '-> [نجاح الفحص 1]: لا يوجد أي تكرار في أرقام الفواتير عبر جميع الفروع.';
ELSE
    PRINT '-> [تحذير شديد]: تم رصد أرقام فواتير مكررة! يرجى مراجعة الجدول أعلاه.';


-- ================================================================================
-- الفحص الثاني: تحليل الفجوات في التسلسل (Sequence Gap Analysis) باستخدام دالة LAG()
-- النتيجة المتوقعة: 0 صفوف (Empty Result)
-- ================================================================================
WITH ParsedInvoices AS
(
    SELECT 
        Id,
        BranchId,
        DocumentNumber,
        -- استخراج الرقم العددي من نهاية رقم الفاتورة (آخر 6 خانات)
        TRY_CAST(RIGHT(DocumentNumber, 6) AS BIGINT) AS SequenceNumber,
        CreatedAt
    FROM dbo.DocumentHeaders
),
SequencedInvoices AS
(
    SELECT 
        BranchId,
        DocumentNumber,
        SequenceNumber,
        LAG(SequenceNumber, 1, 0) OVER (PARTITION BY BranchId ORDER BY SequenceNumber) AS PreviousSequenceNumber,
        CreatedAt
    FROM ParsedInvoices
    WHERE SequenceNumber IS NOT NULL
)
SELECT 
    BranchId,
    PreviousSequenceNumber AS [From_Gap_After],
    SequenceNumber AS [To_Gap_Before],
    (SequenceNumber - PreviousSequenceNumber - 1) AS [Missing_Numbers_Count],
    CONCAT('GAP DETECTED: Missing ', (SequenceNumber - PreviousSequenceNumber - 1), ' numbers between ', PreviousSequenceNumber, ' and ', SequenceNumber) AS GapDetails
FROM SequencedInvoices
WHERE (SequenceNumber - PreviousSequenceNumber) > 1
  AND PreviousSequenceNumber > 0 -- استبعاد السطر الأول (بداية التسلسل)
ORDER BY BranchId, SequenceNumber;

IF @@ROWCOUNT = 0
    PRINT '-> [نجاح الفحص 2]: التسلسل العددي للفواتير متصل بنسبة 100% ولا توجد أي فجوات (Zero Gaps).';
ELSE
    PRINT '-> [تنبيه فجوات]: تم رصد فجوات تسلسلية. قد تنتج عن Rollback لمعاملات فشلت أو تخطي في العداد.';


-- ================================================================================
-- الفحص الثالث: مطابقة آخر رقم في جدول CodeSequences مع أعلى رقم فعلي في DocumentHeaders
-- ================================================================================
WITH ActualBranchMax AS
(
    SELECT 
        BranchId,
        COUNT(*) AS TotalInvoicesInserted,
        MIN(TRY_CAST(RIGHT(DocumentNumber, 6) AS BIGINT)) AS MinSequenceFound,
        MAX(TRY_CAST(RIGHT(DocumentNumber, 6) AS BIGINT)) AS MaxSequenceFound
    FROM dbo.DocumentHeaders
    GROUP BY BranchId
)
SELECT 
    cs.BranchId,
    cs.Prefix,
    cs.LastNumber AS [Counter_In_CodeSequences],
    ISNULL(ab.MaxSequenceFound, 0) AS [Max_Actual_In_Headers],
    ISNULL(ab.TotalInvoicesInserted, 0) AS [Total_Invoices_Inserted],
    CASE 
        WHEN cs.LastNumber = ISNULL(ab.MaxSequenceFound, 0) AND cs.LastNumber = ISNULL(ab.TotalInvoicesInserted, 0)
            THEN 'PERFECT MATCH (تسلسل سليم ومتطابق تماماً)'
        WHEN cs.LastNumber > ISNULL(ab.MaxSequenceFound, 0) 
            THEN 'GAP WARNING: Counter higher than actual (Possible Rolled-back Transactions)'
        ELSE 'OUT OF SYNC: Discrepancy Found'
    END AS SynchronizationStatus
FROM dbo.CodeSequences cs
LEFT JOIN ActualBranchMax ab ON cs.BranchId = ab.BranchId
WHERE cs.EntityName = 'SalesInvoice'
ORDER BY cs.BranchId;

PRINT '================================================================================';
GO
