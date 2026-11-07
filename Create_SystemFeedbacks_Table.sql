-- ==============================================================================================
-- إنشاء جدول ملاحظات واختبارات النظام (SystemFeedbacks) وصلاحيات الشاشة
-- Target Database: db_ab95d6_sewtec2026
-- Description:
--   ينشئ جدول SystemFeedbacks لتسجيل طلبات المستخدمين (مشاكل، اختبارات، ميزات جديدة، استفسارات)
--   مع دعم رفع المرفقات وتسجيل ردود المبرمج وتحديث حالة الطلبات.
-- ==============================================================================================

SET NOCOUNT ON;

-- 1. إنشاء جدول SystemFeedbacks في حال عدم وجوده
IF OBJECT_ID(N'dbo.SystemFeedbacks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemFeedbacks
    (
        Id                    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TicketCode            NVARCHAR(50) NOT NULL,
        BranchId              INT NOT NULL,
        CreatedByUserId       NVARCHAR(450) NOT NULL,
        CreatedByUserName     NVARCHAR(256) NOT NULL,
        CreatedAt             DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        RequestType           INT NOT NULL DEFAULT (1), -- 1: Bug, 2: NewFeature, 3: Inquiry, 4: SystemTest
        Module                INT NOT NULL DEFAULT (0), -- 0: General, 1: Accounting, 2: CashBank, 3: Sales, 4: Purchases, 5: Inventory, 6: Administration, 7: Reports, 8: Other
        Priority              INT NOT NULL DEFAULT (2), -- 1: Low, 2: Medium, 3: High, 4: Critical
        Status                INT NOT NULL DEFAULT (1), -- 1: New, 2: InProgress, 3: Testing, 4: Completed, 5: Rejected
        Title                 NVARCHAR(250) NOT NULL,
        Description           NVARCHAR(4000) NOT NULL,
        AttachmentFileName    NVARCHAR(255) NULL,
        AttachmentPath        NVARCHAR(500) NULL,
        AttachmentContentType NVARCHAR(100) NULL,
        AttachmentSize        BIGINT NULL,
        DeveloperResponse     NVARCHAR(4000) NULL,
        RespondedByUserId     NVARCHAR(450) NULL,
        RespondedByUserName   NVARCHAR(256) NULL,
        RespondedAt           DATETIME2 NULL,
        CONSTRAINT UQ_SystemFeedbacks_TicketCode UNIQUE (TicketCode),
        CONSTRAINT FK_SystemFeedbacks_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches (Id)
    );

    -- إنشاء فهارس لتحسين سرعة الاستعلام والبحث
    CREATE NONCLUSTERED INDEX IX_SystemFeedbacks_Status ON dbo.SystemFeedbacks (Status);
    CREATE NONCLUSTERED INDEX IX_SystemFeedbacks_RequestType ON dbo.SystemFeedbacks (RequestType);
    CREATE NONCLUSTERED INDEX IX_SystemFeedbacks_BranchId ON dbo.SystemFeedbacks (BranchId);
    CREATE NONCLUSTERED INDEX IX_SystemFeedbacks_CreatedAt ON dbo.SystemFeedbacks (CreatedAt DESC);

    PRINT N'-> تم إنشاء جدول SystemFeedbacks والفهارس بنجاح.';
END
ELSE
BEGIN
    PRINT N'-> جدول SystemFeedbacks موجود بالفعل.';
END
GO

-- 2. إدخال صلاحيات الشاشة الجديدة في جدول Permissions
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'Administration.SystemFeedback.View')
BEGIN
    INSERT INTO dbo.Permissions (Code, Module, Screen, Action, NameAr, NameEn, Description, SortOrder)
    VALUES (N'Administration.SystemFeedback.View', N'Administration', N'SystemFeedback', N'View', N'عرض سجل ملاحظات واختبارات النظام', N'View System Feedback & Testing', N'عرض قائمة الملاحظات والاختبارات', 171);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'Administration.SystemFeedback.Create')
BEGIN
    INSERT INTO dbo.Permissions (Code, Module, Screen, Action, NameAr, NameEn, Description, SortOrder)
    VALUES (N'Administration.SystemFeedback.Create', N'Administration', N'SystemFeedback', N'Create', N'إضافة ملاحظة أو اختبار جديد', N'Create System Feedback & Testing', N'تسجيل طلب أو اختبار جديد', 172);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'Administration.SystemFeedback.Reply')
BEGIN
    INSERT INTO dbo.Permissions (Code, Module, Screen, Action, NameAr, NameEn, Description, SortOrder)
    VALUES (N'Administration.SystemFeedback.Reply', N'Administration', N'SystemFeedback', N'Reply', N'الرد على الملاحظات وتحديث حالة الطلب (المبرمجين)', N'Reply to System Feedback & Change Status', N'الرد على الملاحظات وتغيير حالتها', 173);
END;

PRINT N'-> تم إدراج صلاحيات شاشة الملاحظات والدعم الفني بنجاح.';
GO

-- 3. منح الصلاحيات تلقائياً لدور مدير النظام (Admin / SuperAdmin)
DECLARE @AdminRoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE NormalizedName IN ('ADMIN', 'SUPERADMIN', 'ADMINISTRATOR'));
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @AdminRoleId, p.Id
    FROM dbo.Permissions p
    WHERE p.Code LIKE N'Administration.SystemFeedback.%'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolePermissions rp 
          WHERE rp.RoleId = @AdminRoleId AND rp.PermissionId = p.Id
      );

    PRINT N'-> تم منح كامل صلاحيات الشاشة لدور مدير النظام.';
END
GO
