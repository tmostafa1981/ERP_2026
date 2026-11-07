using System;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Domain.Entities
{
    /// <summary>
    /// سجل اختبارات وملاحظات وطلبات المستخدمين والدعم الفني
    /// </summary>
    public class SystemFeedback : IAuditableEntity
    {
        public int Id { get; set; }

        /// <summary>كود الطلب، مثل TCK-0001</summary>
        public string TicketCode { get; set; } = string.Empty;

        /// <summary>الفرع التابع له المستخدم عند التسجيل</summary>
        public int BranchId { get; set; }

        /// <summary>معرّف واسم المستخدم الذي سجل الطلب</summary>
        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByUserName { get; set; } = string.Empty;

        /// <summary>تاريخ ووقت الإنشاء</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>نوع السجل: مشكلة، طلب جديد، استفسار، اختبار</summary>
        public FeedbackType RequestType { get; set; } = FeedbackType.Bug;

        /// <summary>القسم / الشاشة الخاصة بالطلب</summary>
        public FeedbackModule Module { get; set; } = FeedbackModule.General;

        /// <summary>درجة الأهمية</summary>
        public FeedbackPriority Priority { get; set; } = FeedbackPriority.Medium;

        /// <summary>حالة الطلب: جديد، جاري العمل، تحت التنفيذ، تم الانتهاء، ملغي</summary>
        public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

        /// <summary>عنوان الطلب / ملخص الملاحظة</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>الشرح والتفاصيل المطلوبة من المستخدم</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>الملف أو الصورة المرفقة</summary>
        public string? AttachmentFileName { get; set; }
        public string? AttachmentPath { get; set; }
        public string? AttachmentContentType { get; set; }
        public long? AttachmentSize { get; set; }

        /// <summary>رد المبرمج وملاحظات الحل</summary>
        public string? DeveloperResponse { get; set; }
        public string? RespondedByUserId { get; set; }
        public string? RespondedByUserName { get; set; }
        public DateTime? RespondedAt { get; set; }

        // Navigation
        public Branch? Branch { get; set; }
    }
}
