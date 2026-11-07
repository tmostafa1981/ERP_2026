using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Accounts;

namespace ERP.Application.Interfaces
{
    public interface IAccountImportService
    {
        /// <summary>
        /// توليد ملف إكسيل قياسي منسق (.xlsx) يحتوي على الشرح والقوائم المنسدلة وأمثلة شجرة الحسابات.
        /// </summary>
        byte[] GenerateTemplate();

        /// <summary>
        /// قراءة ملف الإكسيل وفحص التناسق الهرمي والأكواد وإرجاع تقرير معاينة للمستخدم قبل الحفظ.
        /// </summary>
        Task<AccountImportPreviewDto> ValidateAndPreviewAsync(Stream excelStream, CancellationToken cancellationToken = default);

        /// <summary>
        /// استيراد وتخزين شجرة الحسابات في قاعدة البيانات مع دعم تحديث الحسابات الموجودة مسبقاً.
        /// </summary>
        Task<AccountImportResultDto> ExecuteImportAsync(Stream excelStream, bool updateExisting, CancellationToken cancellationToken = default);
    }
}
