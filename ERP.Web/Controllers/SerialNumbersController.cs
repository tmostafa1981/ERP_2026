using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Web.Authorization;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class SerialNumbersController : Controller
    {
        private readonly ISerialNumberService _serialService;
        private readonly ErpDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public SerialNumbersController(
            ISerialNumberService serialService,
            ErpDbContext context,
            ICurrentUserContext currentUser)
        {
            _serialService = serialService;
            _context = context;
            _currentUser = currentUser;
        }

        [HttpGet]
        [HasPermission("Inventory.SerialNumbers.View,Inventory.Items.View")]
        public async Task<IActionResult> Inquiry(string? sn)
        {
            ViewBag.Branches = await _context.Branches
                .AsNoTracking()
                .OrderBy(b => b.NameAr)
                .Select(b => new { b.Id, b.NameAr })
                .ToListAsync();

            ViewBag.Warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.NameAr)
                .Select(w => new { w.Id, w.NameAr })
                .ToListAsync();

            ViewBag.InitialSerial = sn ?? "";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetDetails(string serialNumber, int? itemId)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return BadRequest(new { success = false, message = "رقم السيريال مطلوب" });

            var result = await _serialService.GetSerialDetailsAsync(serialNumber, itemId);
            if (result == null)
            {
                return NotFound(new { success = false, message = $"لم يتم العثور على أي بيانات للسيريال ({serialNumber})." });
            }

            return Ok(new { success = true, data = result });
        }

        [HttpPost]
        public async Task<IActionResult> Search([FromBody] SerialInquiryFilterDto filter)
        {
            filter ??= new SerialInquiryFilterDto();
            var results = await _serialService.SearchSerialsAsync(filter);
            return Ok(new { success = true, data = results });
        }

        [HttpGet]
        public async Task<IActionResult> ValidateForIssue(int itemId, int warehouseId, int branchId, string serialNumber)
        {
            var res = await _serialService.ValidateSerialForIssueAsync(itemId, warehouseId, branchId, serialNumber);
            return Ok(res);
        }

        [HttpGet]
        public async Task<IActionResult> ValidateForReceive(int itemId, string serialNumber)
        {
            var res = await _serialService.ValidateSerialForReceiveAsync(itemId, serialNumber);
            return Ok(res);
        }

        [HttpPost]
        public async Task<IActionResult> ValidateBatchForReceive([FromBody] SerialBatchValidationRequestDto req)
        {
            req ??= new SerialBatchValidationRequestDto();
            if (req.BranchId <= 0)
            {
                req.BranchId = _currentUser.BranchId;
            }
            var res = await _serialService.ValidateBatchForReceiveAsync(req);
            return Ok(new { success = true, data = res });
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailablePaged(
            int itemId,
            int warehouseId,
            string? search = null,
            int page = 1,
            int pageSize = 50,
            int? excludeDocumentId = null)
        {
            var res = await _serialService.GetAvailableSerialsPagedAsync(new AvailableSerialsQueryDto
            {
                ItemId = itemId,
                WarehouseId = warehouseId,
                BranchId = _currentUser.BranchId,
                Search = search,
                PageNumber = page,
                PageSize = pageSize,
                ExcludeDocumentId = excludeDocumentId
            });
            return Ok(new { success = true, data = res });
        }
    }
}
