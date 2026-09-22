namespace ERP.Domain.Enums
{
    /// <summary>
    /// Central enum for all auditable actions in the ERP system.
    /// Used as the Action field in AuditLog records.
    /// </summary>
    public enum AuditAction
    {
        // ─── CRUD ─────────────────────────────────────────────────────────────────
        Create = 1,
        Update = 2,
        Delete = 3,

        // ─── Read / Output ────────────────────────────────────────────────────────
        View = 10,
        Print = 11,
        Export = 12,

        // ─── Workflow ─────────────────────────────────────────────────────────────
        Approve = 20,
        Reject = 21,
        Cancel = 22,
        Post = 23,
        Unpost = 24,

        // ─── Auth ─────────────────────────────────────────────────────────────────
        Login = 30,
        Logout = 31,
        ChangePassword = 32,
        ResetPassword = 33,

        // ─── Security / Role Management ───────────────────────────────────────────
        AssignRole = 40,
        RemoveRole = 41,
        AssignBranch = 42,
        RemoveBranch = 43,

        // ─── Context Changes ──────────────────────────────────────────────────────
        ChangeFiscalYear = 50,
        ChangeCompany = 51,
        ChangeBranch = 52,
    }

    /// <summary>
    /// Extension helpers for AuditAction.
    /// </summary>
    public static class AuditActionExtensions
    {
        public static string ToDisplayString(this AuditAction action) => action switch
        {
            AuditAction.Create         => "Create",
            AuditAction.Update         => "Update",
            AuditAction.Delete         => "Delete",
            AuditAction.View           => "View",
            AuditAction.Print          => "Print",
            AuditAction.Export         => "Export",
            AuditAction.Approve        => "Approve",
            AuditAction.Reject         => "Reject",
            AuditAction.Cancel         => "Cancel",
            AuditAction.Post           => "Post",
            AuditAction.Unpost         => "Unpost",
            AuditAction.Login          => "Login",
            AuditAction.Logout         => "Logout",
            AuditAction.ChangePassword => "Change Password",
            AuditAction.ResetPassword  => "Reset Password",
            AuditAction.AssignRole     => "Assign Role",
            AuditAction.RemoveRole     => "Remove Role",
            AuditAction.AssignBranch   => "Assign Branch",
            AuditAction.RemoveBranch   => "Remove Branch",
            AuditAction.ChangeFiscalYear => "Change Fiscal Year",
            AuditAction.ChangeCompany  => "Change Company",
            AuditAction.ChangeBranch   => "Change Branch",
            _                          => action.ToString()
        };

        public static string ToArabicString(this AuditAction action) => action switch
        {
            AuditAction.Create         => "إضافة",
            AuditAction.Update         => "تعديل",
            AuditAction.Delete         => "حذف",
            AuditAction.View           => "عرض",
            AuditAction.Print          => "طباعة",
            AuditAction.Export         => "تصدير",
            AuditAction.Approve        => "اعتماد",
            AuditAction.Reject         => "رفض",
            AuditAction.Cancel         => "إلغاء",
            AuditAction.Post           => "ترحيل",
            AuditAction.Unpost         => "إلغاء ترحيل",
            AuditAction.Login          => "تسجيل دخول",
            AuditAction.Logout         => "تسجيل خروج",
            AuditAction.ChangePassword => "تغيير كلمة المرور",
            AuditAction.ResetPassword  => "إعادة تعيين كلمة المرور",
            AuditAction.AssignRole     => "تعيين دور",
            AuditAction.RemoveRole     => "إزالة دور",
            AuditAction.AssignBranch   => "تعيين فرع",
            AuditAction.RemoveBranch   => "إزالة فرع",
            AuditAction.ChangeFiscalYear => "تغيير السنة المالية",
            AuditAction.ChangeCompany  => "تغيير الشركة",
            AuditAction.ChangeBranch   => "تغيير الفرع",
            _                          => action.ToString()
        };
    }
}
