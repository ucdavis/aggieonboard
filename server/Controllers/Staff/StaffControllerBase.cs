using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Server.Core.Domain;
using Server.Core.Journey;

namespace Server.Controllers.Staff;

/// <summary>
/// Everything under <c>/api/staff</c>: staff whitelist members only. Candidates never reach these
/// endpoints (AGENTS.md, invariant 4).
/// </summary>
[Authorize(Roles = AppRoles.Staff)]
[ApiController]
[Route("api/staff")]
[JourneyErrors]
public abstract class StaffControllerBase : ControllerBase
{
}

/// <summary>
/// A move the journey's rules refuse is a 409 with the reason; invalid candidate details are a 400
/// with field errors in the standard validation-problem shape.
/// </summary>
public sealed class JourneyErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case JourneyRuleException rule:
                context.Result = new ConflictObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "That change isn't allowed.",
                    Detail = rule.Message,
                });
                context.ExceptionHandled = true;
                break;

            case JourneyValidationException invalid:
                context.Result = new BadRequestObjectResult(new ValidationProblemDetails(
                    invalid.Errors.ToDictionary(e => e.Key, e => e.Value)));
                context.ExceptionHandled = true;
                break;
        }
    }
}
