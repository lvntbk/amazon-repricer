using System.Security.Claims;
using AmazonRepricer.Api.Controllers;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Auth;

public sealed class TwoFactorEnrollmentFilter : IAsyncActionFilter
{
    private readonly AuthDbContext _db;

    public TwoFactorEnrollmentFilter(AuthDbContext db)
    {
        _db = db;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var principal = context.HttpContext.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        // Giriş, token yenileme ve 2FA kurulumu erişilebilir kalmalı.
        var controllerType =
            (context.ActionDescriptor as ControllerActionDescriptor)
            ?.ControllerTypeInfo.AsType();

        if (controllerType == typeof(AuthController) ||
            controllerType == typeof(TwoFactorController))
        {
            await next();
            return;
        }

        var requiresTwoFactor =
            principal.IsInRole(AppRoles.Admin) ||
            principal.IsInRole(AppRoles.Operator);

        if (!requiresTwoFactor)
        {
            await next();
            return;
        }

        if (!Guid.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var enrolled = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId &&
                        user.IsActive &&
                        user.TwoFactorEnabled,
                context.HttpContext.RequestAborted);

        if (!enrolled)
        {
            context.Result = new ObjectResult(new
            {
                Code = "two_factor_setup_required",
                Message = "Complete two-factor setup before accessing this resource."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
