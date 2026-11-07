using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Collections.Generic;
using System.Linq;

namespace ERP.Web.Extensions
{
    public static class AjaxExtensions
    {
        public static bool IsAjaxRequest(this HttpRequest request)
        {
            if (request == null) return false;
            return request.Headers["X-Requested-With"] == "XMLHttpRequest";
        }

        public static JsonResult JsonValidationFailure(this Controller controller, string mainMessage = "لا يمكن حفظ البيانات. يرجى تصحيح الأخطاء التالية.")
        {
            return controller.JsonValidationFailure(controller.ModelState, mainMessage);
        }

        public static JsonResult JsonValidationFailure(this Controller controller, ModelStateDictionary modelState, string mainMessage = "لا يمكن حفظ البيانات. يرجى تصحيح الأخطاء التالية.")
        {
            var errors = new Dictionary<string, List<string>>();
            foreach (var kvp in modelState)
            {
                if (kvp.Value.Errors.Any())
                {
                    errors[kvp.Key] = kvp.Value.Errors.Select(e => e.ErrorMessage).ToList();
                }
            }

            return new JsonResult(new
            {
                success = false,
                message = mainMessage,
                errors = errors
            });
        }

        public static JsonResult JsonValidationFailure(this Controller controller, IEnumerable<string> generalErrors, string mainMessage = "لا يمكن حفظ البيانات. يرجى تصحيح الأخطاء التالية.")
        {
            var errors = new Dictionary<string, List<string>>
            {
                [""] = generalErrors.ToList()
            };

            return new JsonResult(new
            {
                success = false,
                message = mainMessage,
                errors = errors
            });
        }

        public static JsonResult JsonSuccess(this Controller controller, string message = "تم الحفظ بنجاح.", string? redirectUrl = null)
        {
            return new JsonResult(new
            {
                success = true,
                message = message,
                redirectUrl = redirectUrl
            });
        }

        public static JsonResult JsonFailure(this Controller controller, string message)
        {
            return new JsonResult(new
            {
                success = false,
                message = message
            });
        }
    }
}
