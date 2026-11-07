using System.Threading.Tasks;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Web.Controllers
{
    [Authorize]
    [Route("api/sequence")]
    [ApiController]
    public class CodeSequenceApiController : ControllerBase
    {
        private readonly ICodeSequenceService _sequenceService;
        private readonly ICurrentUserContext _currentUser;

        public CodeSequenceApiController(ICodeSequenceService sequenceService, ICurrentUserContext currentUser)
        {
            _sequenceService = sequenceService;
            _currentUser     = currentUser;
        }

        [HttpGet("config")]
        public async Task<IActionResult> GetConfig([FromQuery] string entity, [FromQuery] int? branchId)
        {
            if (string.IsNullOrWhiteSpace(entity))
                return BadRequest("Entity is required.");

            if (branchId.HasValue && branchId.Value > 0 && !_currentUser.CanAccessBranch(branchId.Value))
                return Forbid();

            var config = await _sequenceService.GetConfigAsync(entity, branchId);
            return Ok(config);
        }

        [HttpGet("preview")]
        public async Task<IActionResult> Preview([FromQuery] string entity, [FromQuery] int? branchId)
        {
            if (string.IsNullOrWhiteSpace(entity))
                return BadRequest("Entity is required.");

            if (branchId.HasValue && branchId.Value > 0 && !_currentUser.CanAccessBranch(branchId.Value))
                return Forbid();

            var preview = await _sequenceService.PeekNextCodeAsync(entity, branchId);
            var isAuto = await _sequenceService.IsAutoEnabledAsync(entity, branchId);
            var isManualAllowed = await _sequenceService.IsManualAllowedAsync(entity, branchId);

            return Ok(new { code = preview, isAuto, isManualAllowed });
        }
    }
}
