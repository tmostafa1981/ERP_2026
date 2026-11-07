using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace ERP.Web.Models
{
    public class SystemFeedbackListViewModel
    {
        public List<SystemFeedbackItemViewModel> Items { get; set; } = new();

        // Filters
        public FeedbackType? SelectedType { get; set; }
        public FeedbackStatus? SelectedStatus { get; set; }
        public FeedbackModule? SelectedModule { get; set; }
        public string? SearchTerm { get; set; }
        public int? SelectedBranchId { get; set; }

        // KPI Counters
        public int TotalCount { get; set; }
        public int NewCount { get; set; }
        public int InProgressCount { get; set; }
        public int TestingCount { get; set; }
        public int CompletedCount { get; set; }

        public bool CanReply { get; set; }
        public bool CanCreate { get; set; }
    }

    public class SystemFeedbackItemViewModel
    {
        public int Id { get; set; }
        public string TicketCode { get; set; } = string.Empty;
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public string CreatedByUserName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public FeedbackType RequestType { get; set; }
        public FeedbackModule Module { get; set; }
        public FeedbackPriority Priority { get; set; }
        public FeedbackStatus Status { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string? AttachmentFileName { get; set; }
        public string? AttachmentPath { get; set; }
        public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentPath);
        public bool IsImageAttachment => HasAttachment && (
            AttachmentFileName?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true ||
            AttachmentFileName?.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true ||
            AttachmentFileName?.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) == true ||
            AttachmentFileName?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) == true ||
            AttachmentFileName?.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) == true);

        public string? DeveloperResponse { get; set; }
        public string? RespondedByUserName { get; set; }
        public DateTime? RespondedAt { get; set; }
        public bool HasResponse => !string.IsNullOrWhiteSpace(DeveloperResponse);
    }

    public class FeedbackCreateViewModel
    {
        [Required(ErrorMessage = "يرجى تحديد نوع الطلب")]
        [Display(Name = "نوع الطلب")]
        public FeedbackType RequestType { get; set; } = FeedbackType.Bug;

        [Required(ErrorMessage = "يرجى تحديد القسم الخاص بالمشكلة")]
        [Display(Name = "القسم / الموديول")]
        public FeedbackModule Module { get; set; } = FeedbackModule.General;

        [Required(ErrorMessage = "يرجى تحديد درجة الأهمية")]
        [Display(Name = "الأولوية")]
        public FeedbackPriority Priority { get; set; } = FeedbackPriority.Medium;

        [Required(ErrorMessage = "يرجى كتابة عنوان مختصر للمشكلة أو الطلب")]
        [StringLength(250, ErrorMessage = "العنوان يجب ألا يتجاوز 250 حرفاً")]
        [Display(Name = "عنوان الموضوع")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "يرجى كتابة الشرح والتفاصيل")]
        [StringLength(4000, ErrorMessage = "الشرح يجب ألا يتجاوز 4000 حرف")]
        [Display(Name = "الشرح والتفاصيل")]
        public string Description { get; set; } = string.Empty;

        [Display(Name = "صورة أو ملف توضيحي")]
        public IFormFile? AttachmentFile { get; set; }
    }

    public class FeedbackReplyViewModel
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "يرجى اختيار حالة الطلب")]
        [Display(Name = "حالة الطلب")]
        public FeedbackStatus NewStatus { get; set; } = FeedbackStatus.InProgress;

        [Required(ErrorMessage = "يرجى كتابة رد المبرمج")]
        [StringLength(4000, ErrorMessage = "الرد يجب ألا يتجاوز 4000 حرف")]
        [Display(Name = "رد المبرمج")]
        public string DeveloperResponse { get; set; } = string.Empty;
    }
}
