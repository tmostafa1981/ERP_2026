using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using ERP.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class SystemFeedbackController : Controller
    {
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IWebHostEnvironment _env;

        public SystemFeedbackController(
            ErpDbContext context,
            ICurrentUserContext currentUser,
            IWebHostEnvironment env)
        {
            _context = context;
            _currentUser = currentUser;
            _env = env;
        }

        // ─── GET: Index (عرض قائمة الملاحظات والاختبارات) ────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(
            FeedbackType? type,
            FeedbackStatus? status,
            FeedbackModule? module,
            int? branchId,
            string? search)
        {
            bool isSuperAdmin = _currentUser.IsSuperAdmin();
            bool hasViewPerm = _currentUser.HasPermission("Administration.SystemFeedback.View") || isSuperAdmin;

            // إذا لم يكن لديه صلاحية العرض الإدارية، يرى الطلبات التي أنشأها بنفسه
            var query = _context.SystemFeedbacks
                .Include(f => f.Branch)
                .AsNoTracking();

            if (!hasViewPerm)
            {
                string userId = _currentUser.UserId ?? "";
                query = query.Where(f => f.CreatedByUserId == userId);
            }

            // تطبيق الفلاتر
            if (type.HasValue)
                query = query.Where(f => f.RequestType == type.Value);

            if (status.HasValue)
                query = query.Where(f => f.Status == status.Value);

            if (module.HasValue)
                query = query.Where(f => f.Module == module.Value);

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(f => f.BranchId == branchId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(f =>
                    f.TicketCode.Contains(search) ||
                    f.Title.Contains(search) ||
                    f.Description.Contains(search) ||
                    (f.DeveloperResponse != null && f.DeveloperResponse.Contains(search)) ||
                    f.CreatedByUserName.Contains(search));
            }

            // ترتيب: الطلبات غير المكتملة أولاً، ثم الأحدث
            var allFiltered = await query
                .OrderBy(f => f.Status == FeedbackStatus.Completed || f.Status == FeedbackStatus.Rejected ? 1 : 0)
                .ThenByDescending(f => f.CreatedAt)
                .ToListAsync();

            // حساب العدادات الإجمالية (KPIs)
            var statsQuery = _context.SystemFeedbacks.AsNoTracking();
            if (!hasViewPerm)
            {
                string userId = _currentUser.UserId ?? "";
                statsQuery = statsQuery.Where(f => f.CreatedByUserId == userId);
            }

            var allStats = await statsQuery.Select(f => f.Status).ToListAsync();

            bool canReply = true;
            bool canCreate = true;

            var model = new SystemFeedbackListViewModel
            {
                SelectedType = type,
                SelectedStatus = status,
                SelectedModule = module,
                SelectedBranchId = branchId,
                SearchTerm = search,
                CanReply = canReply,
                CanCreate = canCreate,
                TotalCount = allStats.Count,
                NewCount = allStats.Count(s => s == FeedbackStatus.New),
                InProgressCount = allStats.Count(s => s == FeedbackStatus.InProgress),
                TestingCount = allStats.Count(s => s == FeedbackStatus.Testing),
                CompletedCount = allStats.Count(s => s == FeedbackStatus.Completed),
                Items = allFiltered.Select(f => new SystemFeedbackItemViewModel
                {
                    Id = f.Id,
                    TicketCode = f.TicketCode,
                    BranchId = f.BranchId,
                    BranchName = f.Branch?.NameAr ?? f.Branch?.Name ?? $"فرع {f.BranchId}",
                    CreatedByUserName = f.CreatedByUserName,
                    CreatedAt = f.CreatedAt,
                    RequestType = f.RequestType,
                    Module = f.Module,
                    Priority = f.Priority,
                    Status = f.Status,
                    Title = f.Title,
                    Description = f.Description,
                    AttachmentFileName = f.AttachmentFileName,
                    AttachmentPath = f.AttachmentPath,
                    DeveloperResponse = f.DeveloperResponse,
                    RespondedByUserName = f.RespondedByUserName,
                    RespondedAt = f.RespondedAt
                }).ToList()
            };

            ViewBag.Branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Id)
                .Select(b => new { b.Id, BranchNameAr = b.NameAr ?? b.Name })
                .ToListAsync();

            return View(model);
        }

        // ─── POST: Create (تسجيل طلب / اختبار / مشكلة جديدة) ─────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] FeedbackCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "يرجى ملء جميع الحقول المطلوبة والتأكد من صحة البيانات.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? attachmentPath = null;
                string? originalFileName = null;
                string? contentType = null;
                long? fileSize = null;

                // رفع وحفظ الملف المرفق
                if (model.AttachmentFile != null && model.AttachmentFile.Length > 0)
                {
                    // التحقق من الحجم (الحد الأقصى 15 ميجابايت)
                    if (model.AttachmentFile.Length > 15 * 1024 * 1024)
                    {
                        TempData["ErrorMessage"] = "حجم الملف المرفق كبير جداً. الحد الأقصى المسموح به هو 15 ميجابايت.";
                        return RedirectToAction(nameof(Index));
                    }

                    string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "feedback");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    originalFileName = Path.GetFileName(model.AttachmentFile.FileName);
                    string extension = Path.GetExtension(originalFileName).ToLowerInvariant();
                    string safeFileName = $"fb_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
                    string fullPath = Path.Combine(uploadsFolder, safeFileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await model.AttachmentFile.CopyToAsync(stream);
                    }

                    attachmentPath = $"/uploads/feedback/{safeFileName}";
                    contentType = model.AttachmentFile.ContentType;
                    fileSize = model.AttachmentFile.Length;
                }

                // توليد كود الطلب
                int lastId = await _context.SystemFeedbacks
                    .OrderByDescending(f => f.Id)
                    .Select(f => f.Id)
                    .FirstOrDefaultAsync();

                string ticketCode = $"TCK-{(lastId + 1):D4}";

                int branchId = _currentUser.BranchId > 0 ? _currentUser.BranchId : 1;
                string userId = _currentUser.UserId ?? "";
                string userName = _currentUser.Username ?? User.Identity?.Name ?? "مستخدم النظام";

                var feedback = new SystemFeedback
                {
                    TicketCode = ticketCode,
                    BranchId = branchId,
                    CreatedByUserId = userId,
                    CreatedByUserName = userName,
                    CreatedAt = DateTime.UtcNow,
                    RequestType = model.RequestType,
                    Module = model.Module,
                    Priority = model.Priority,
                    Status = FeedbackStatus.New,
                    Title = model.Title.Trim(),
                    Description = model.Description.Trim(),
                    AttachmentFileName = originalFileName,
                    AttachmentPath = attachmentPath,
                    AttachmentContentType = contentType,
                    AttachmentSize = fileSize
                };

                _context.SystemFeedbacks.Add(feedback);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"تم تسجيل الطلب بنجاح برقم ({ticketCode}). سيتم مراجعته وموافاتك بالرد فوراً.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"حدث خطأ أثناء حفظ الطلب: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // ─── POST: Reply (رد المبرمج وتحديث حالة الطلب) ──────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply([FromForm] FeedbackReplyViewModel model)
        {
            bool isSuperAdmin = _currentUser.IsSuperAdmin();
            bool canReply = _currentUser.HasPermission("Administration.SystemFeedback.Reply") || isSuperAdmin;

            if (!canReply)
            {
                TempData["ErrorMessage"] = "عفواً، هذه العملية مخصصة للمبرمجين ومديري النظام فقط.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "يرجى كتابة نص الرد واختيار حالة الطلب.";
                return RedirectToAction(nameof(Index));
            }

            var feedback = await _context.SystemFeedbacks.FindAsync(model.Id);
            if (feedback == null)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السجل المطلوب.";
                return RedirectToAction(nameof(Index));
            }

            feedback.Status = model.NewStatus;
            feedback.DeveloperResponse = model.DeveloperResponse.Trim();
            feedback.RespondedByUserId = _currentUser.UserId;
            feedback.RespondedByUserName = _currentUser.Username ?? User.Identity?.Name ?? "المبرمج";
            feedback.RespondedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم حفظ رد المبرمج وتحديث حالة الطلب ({feedback.TicketCode}) بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ─── POST: QuickStatus (تحديث سريع للحالة: جاري العمل / تحت التنفيذ / إغلاق) ───
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickStatus(int id, FeedbackStatus status, string? responseText)
        {
            var feedback = await _context.SystemFeedbacks.FindAsync(id);
            if (feedback == null)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السجل المطلوب.";
                return RedirectToAction(nameof(Index));
            }

            feedback.Status = status;
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                feedback.DeveloperResponse = responseText.Trim();
            }
            else if (string.IsNullOrWhiteSpace(feedback.DeveloperResponse))
            {
                feedback.DeveloperResponse = status switch
                {
                    FeedbackStatus.InProgress => "جاري العمل على فحص وحل المشكلة من قبل فريق التطوير.",
                    FeedbackStatus.Testing => "تم تجهيز التعديل المطلوب وهو حالياً تحت التنفيذ والاختبار للتأكد من سلامته.",
                    FeedbackStatus.Completed => "تم الانتهاء والحل بنجاح، وتم إغلاق الطلب.",
                    FeedbackStatus.Rejected => "تم إلغاء الطلب / رفضه.",
                    _ => feedback.DeveloperResponse
                };
            }

            feedback.RespondedByUserId = _currentUser.UserId;
            feedback.RespondedByUserName = _currentUser.Username ?? User.Identity?.Name ?? "المبرمج";
            feedback.RespondedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            string statusName = status switch
            {
                FeedbackStatus.InProgress => "جاري العمل",
                FeedbackStatus.Testing => "تحت التنفيذ / قيد الاختبار",
                FeedbackStatus.Completed => "تم الانتهاء وإغلاق الطلب",
                FeedbackStatus.Rejected => "ملغي / مرفوض",
                _ => "تحديث الحالة"
            };

            TempData["SuccessMessage"] = $"تم تحديث حالة الطلب ({feedback.TicketCode}) إلى ({statusName}) بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ─── GET: GetDetails (استرجاع تفاصيل الطلب بالـ JSON للمودال السريع) ─────
        [HttpGet]
        public async Task<IActionResult> GetDetails(int id)
        {
            var f = await _context.SystemFeedbacks
                .Include(x => x.Branch)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (f == null)
                return NotFound(new { success = false, message = "الطلب غير موجود" });

            return Json(new
            {
                success = true,
                id = f.Id,
                ticketCode = f.TicketCode,
                branchName = f.Branch?.NameAr ?? f.Branch?.Name ?? $"فرع {f.BranchId}",
                createdByUser = f.CreatedByUserName,
                createdAt = f.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                requestType = (int)f.RequestType,
                requestTypeName = GetEnumDisplayName(f.RequestType),
                module = (int)f.Module,
                moduleName = GetEnumDisplayName(f.Module),
                priority = (int)f.Priority,
                priorityName = GetEnumDisplayName(f.Priority),
                status = (int)f.Status,
                statusName = GetEnumDisplayName(f.Status),
                title = f.Title,
                description = f.Description,
                attachmentFileName = f.AttachmentFileName,
                attachmentPath = f.AttachmentPath,
                isImage = !string.IsNullOrEmpty(f.AttachmentPath) && (
                    f.AttachmentFileName?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true ||
                    f.AttachmentFileName?.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true ||
                    f.AttachmentFileName?.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) == true),
                developerResponse = f.DeveloperResponse,
                respondedBy = f.RespondedByUserName,
                respondedAt = f.RespondedAt?.ToString("yyyy-MM-dd HH:mm")
            });
        }

        private static string GetEnumDisplayName<T>(T enumVal) where T : Enum
        {
            var field = enumVal.GetType().GetField(enumVal.ToString());
            if (field != null)
            {
                var attr = Attribute.GetCustomAttribute(field, typeof(System.ComponentModel.DataAnnotations.DisplayAttribute)) 
                    as System.ComponentModel.DataAnnotations.DisplayAttribute;
                if (attr != null) return attr.Name ?? enumVal.ToString();
            }
            return enumVal.ToString();
        }
    }

    internal static class CurrentUserExtensions
    {
        public static bool IsSuperAdmin(this ICurrentUserContext user)
        {
            return user.Username?.Equals("admin", StringComparison.OrdinalIgnoreCase) == true ||
                   user.Username?.Equals("superadmin", StringComparison.OrdinalIgnoreCase) == true ||
                   user.HasAllBranches;
        }
    }
}
