/****************************************************************************************************
  ملف رقم (4): تشخيص الـ Deadlocks واختناقات الأقفال (Lock Waits & Extended Events)
  File: 04_Diagnostics_DeadlocksAndLockWaits.sql
  Author: Performance & QA Engineer (Antigravity Agent)
  Target: SQL Server 2019 / 2022
  Description:
    يستخرج تفاصيل أي Deadlocks حدثت أثناء اختبار التحمل من جلسة system_health المدمجة في 
    SQL Server، بالإضافة إلى مراقبة إحصائيات الانتظار (Lock Wait Statistics) لتحديد الاختناقات.
****************************************************************************************************/

USE ERP_2026_DB;
GO

PRINT '================================================================================';
PRINT '            تشخيص الـ Deadlocks وإحصائيات أقفال قاعدة البيانات (Diagnostics)         ';
PRINT '================================================================================';

-- ================================================================================
-- 1. استخراج مخططات الـ Deadlocks (Deadlock Graphs XML) من Extended Events (system_health)
-- ================================================================================
WITH DeadlockReports AS
(
    SELECT 
        XEvent.value('(@timestamp)[1]', 'DATETIME2') AS DeadlockTimeUtc,
        XEvent.query('.') AS DeadlockGraphXml
    FROM
    (
        SELECT CAST(target_data AS XML) AS TargetData
        FROM sys.dm_xe_session_targets st
        INNER JOIN sys.dm_xe_sessions s ON s.address = st.event_session_address
        WHERE s.name = 'system_health' 
          AND st.target_name = 'ring_buffer'
    ) AS Data
    CROSS APPLY TargetData.nodes('RingBufferTarget/event[@name="xml_deadlock_report"]') AS XEventData(XEvent)
)
SELECT 
    DeadlockTimeUtc,
    DATEADD(HOUR, 2, DeadlockTimeUtc) AS DeadlockTimeLocalCairo, -- توقيت القاهرة
    DeadlockGraphXml
FROM DeadlockReports
ORDER BY DeadlockTimeUtc DESC;

IF @@ROWCOUNT = 0
    PRINT '-> لم يتم تسجيل أي Deadlocks في ذاكرة الـ Ring Buffer الأخيرة.';
ELSE
    PRINT '-> تم العثور على تقارير Deadlocks أعلاه. انقر على عمود DeadlockGraphXml لمشاهدة المخطط التفاعلي.';


-- ================================================================================
-- 2. إحصائيات زمن الانتظار على أقفال الجداول والصفوف (Lock Wait Statistics)
-- ================================================================================
SELECT 
    wait_type,
    waiting_tasks_count,
    wait_time_ms,
    max_wait_time_ms,
    signal_wait_time_ms,
    CAST(100.0 * wait_time_ms / NULLIF(SUM(wait_time_ms) OVER(), 0) AS DECIMAL(5,2)) AS PctOfTotalWaitTime
FROM sys.dm_os_wait_stats
WHERE wait_type LIKE 'LCK_M_%' -- أنواع الانتظار الخاصة بالأقفال الحصرية والمشتركة
  AND waiting_tasks_count > 0
ORDER BY wait_time_ms DESC;

PRINT '================================================================================';
GO
