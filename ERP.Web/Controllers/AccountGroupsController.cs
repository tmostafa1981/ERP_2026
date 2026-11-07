using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Web.Controllers
{
    /// <summary>
    /// تم دمج مجموعات الحسابات وحسابات الحركة في شجرة الحسابات الموحدة (GLAccounts).
    /// يتم تحويل كافة الطلبات تلقائياً إلى المتحكم الموحد AccountsController.
    /// </summary>
    [Authorize]
    public class AccountGroupsController : Controller
    {
        [HttpGet]
        public IActionResult Index() => RedirectToAction("Index", "Accounts");

        [HttpGet]
        public IActionResult GetTreeData() => RedirectToAction("GetTreeData", "Accounts");
    }
}
