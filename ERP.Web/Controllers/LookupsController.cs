using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Application.DTOs.Lookups;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Web.Controllers
{
    [Authorize]
    public class LookupsController : Controller
    {
        private readonly ILookupService _lookupService;
        private readonly ICurrentUserContext _currentUser;

        public LookupsController(ILookupService lookupService, ICurrentUserContext currentUser)
        {
            _lookupService = lookupService;
            _currentUser = currentUser;
        }

        [HttpGet]
        public async Task<IActionResult> Search(
            [FromQuery] string type,
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] int? branchId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(type))
                return BadRequest(new { message = "Entity type is required." });

            // Extract any extra query parameters as filters
            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in Request.Query)
            {
                if (kvp.Key.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("q", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("page", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("pageSize", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("branchId", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                filters[kvp.Key] = kvp.Value.ToString();
            }

            int? activeBranchId = null;
            if (branchId.HasValue && branchId.Value > 0)
            {
                activeBranchId = branchId.Value;
            }
            else if (!_currentUser.HasAllBranches && _currentUser.BranchId > 0)
            {
                activeBranchId = _currentUser.BranchId;
            }

            var request = new LookupSearchRequest
            {
                EntityType = type,
                Query = q,
                Page = page,
                PageSize = pageSize,
                BranchId = activeBranchId,
                Filters = filters
            };

            var result = await _lookupService.SearchAsync(request, cancellationToken);
            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetById(
            [FromQuery] string type,
            [FromQuery] string id,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
                return BadRequest();

            LookupResultDto? result;
            if (int.TryParse(id, out var intId) && intId > 0 && !type.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                result = await _lookupService.GetByIdAsync(type, intId, cancellationToken);
            }
            else
            {
                result = await _lookupService.GetByStringIdAsync(type, id, cancellationToken);
            }

            if (result == null)
                return NotFound();

            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetByCode(
            [FromQuery] string type,
            [FromQuery] string code,
            [FromQuery] int? branchId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(code))
                return BadRequest();

            // Single-Active-Branch Session Rule: Always isolate by active branch
            int? targetBranch = _currentUser.BranchId > 0 ? _currentUser.BranchId : branchId;
            var result = await _lookupService.GetByCodeAsync(type, code, targetBranch, cancellationToken);
            if (result == null)
                return NotFound();

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> ResolveBatch(
            [FromBody] BatchResolveRequestDto request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EntityType))
                return BadRequest(new { message = "Entity type and request body are required." });

            // Enforce active session branch isolation
            if (_currentUser.BranchId > 0)
            {
                request.BranchId = _currentUser.BranchId;
            }

            var result = await _lookupService.ResolveBatchAsync(request, cancellationToken);
            return Json(result);
        }
    }
}



























