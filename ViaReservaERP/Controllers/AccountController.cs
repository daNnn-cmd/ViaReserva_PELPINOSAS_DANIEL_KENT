using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Auth;
using ViaReservaERP.Security;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

public class AccountController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly INotificationService _notification;
    private readonly IEmailTemplateService _templates;
    private readonly IConfiguration _config;

    public AccountController(
        ViaReservaDbContext db, 
        IMemoryCache cache, 
        INotificationService notification,
        IEmailTemplateService templates,
        IConfiguration config)
    {
        _db = db;
        _cache = cache;
        _notification = notification;
        _templates = templates;
        _config = config;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null, CancellationToken ct = default)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var normalizedEmail = (model.Email ?? string.Empty).Trim().ToLowerInvariant();
        var cacheKey = $"LoginFailures_{normalizedEmail}";
        var lockoutKey = $"LoginLockout_{normalizedEmail}";

        // Check for active lockout
        if (_cache.TryGetValue(lockoutKey, out DateTime lockoutExpiry))
        {
            var remaining = lockoutExpiry - ViaReservaERP.AppTime.Now;
            if (remaining.TotalSeconds > 0)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "Lockout Prevented Login",
                    TableName = "Users",
                    NewValues = $"Blocked login attempt for locked account: {normalizedEmail}",
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    ActionDate = ViaReservaERP.AppTime.Now
                });
                await _db.SaveChangesAsync(ct);

                ModelState.AddModelError(string.Empty, "Too many failed login attempts. Please wait 30 seconds before trying again.");
                TempData["LockoutSeconds"] = (int)remaining.TotalSeconds;
                return View(model);
            }
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && !u.IsDeleted && u.IsActive, ct);

        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            // Increment failures
            var failures = _cache.Get<int>(cacheKey) + 1;
            var maxAttempts = 5;
            var remainingAttempts = maxAttempts - failures;

            if (failures >= maxAttempts)
            {
                var expiry = ViaReservaERP.AppTime.Now.AddSeconds(30);
                _cache.Set(lockoutKey, expiry, TimeSpan.FromSeconds(30));
                _cache.Remove(cacheKey); // Reset failures for after lockout

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "Security Lockout",
                    TableName = "Users",
                    NewValues = $"Account locked for 30s due to 5 failures: {normalizedEmail}",
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    ActionDate = ViaReservaERP.AppTime.Now
                });

                ModelState.AddModelError(string.Empty, "Too many failed login attempts. Please wait 30 seconds before trying again.");
                TempData["LockoutSeconds"] = 30;
            }
            else
            {
                _cache.Set(cacheKey, failures, TimeSpan.FromMinutes(10));

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "Failed Login",
                    TableName = "Users",
                    NewValues = $"Invalid credentials for: {normalizedEmail}. Attempts remaining: {remainingAttempts}",
                    IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    ActionDate = ViaReservaERP.AppTime.Now
                });

                ModelState.AddModelError(string.Empty, $"Invalid email or password. You have {remainingAttempts} attempts remaining.");
                TempData["RemainingAttempts"] = remainingAttempts;
            }

            await _db.SaveChangesAsync(ct);
            return View(model);
        }

        // Login success - clear failures and lockout
        _cache.Remove(cacheKey);
        _cache.Remove(lockoutKey);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user.UserId,
            CompanyId = user.CompanyId,
            Action = "Login Success",
            TableName = "Users",
            NewValues = $"User {user.Email} logged in successfully.",
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = ViaReservaERP.AppTime.Now
        });
        await _db.SaveChangesAsync(ct);

        await SignInUserAsync(user, rememberMe: false, ct);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return user.RoleId switch
        {
            1 => RedirectToAction("Dashboard", "SuperAdmin"),
            2 => RedirectToAction("Dashboard", "Admin"),
            3 => RedirectToAction("Dashboard", "Accounting"),
            4 => RedirectToAction("Dashboard", "FrontDesk"),
            5 => RedirectToAction("Dashboard", "ServiceStaff"),
            6 => RedirectToAction("Dashboard", "Guest"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var roleId = User.FindFirstValue(ViaReservaClaims.RoleId);
            return roleId switch
            {
                "1" => RedirectToAction("Dashboard", "SuperAdmin"),
                "2" => RedirectToAction("Dashboard", "Admin"),
                "3" => RedirectToAction("Dashboard", "Accounting"),
                "4" => RedirectToAction("Dashboard", "FrontDesk"),
                "5" => RedirectToAction("Dashboard", "ServiceStaff"),
                "6" => RedirectToAction("Dashboard", "Guest"),
                _ => RedirectToAction("Index", "Home")
            };
        }
        return RedirectToAction("Login");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError(string.Empty, "Email is required.");
            return View();
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && !u.IsDeleted && u.IsActive, ct);

        if (user != null)
        {
            // In a real app, we'd generate a token and store it. 
            // For now, we'll generate a dummy reset link.
            var resetToken = Guid.NewGuid().ToString();
            var baseUrl = _config["BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var resetLink = $"{baseUrl}{Url.Action("ResetPassword", "Account", new { email = user.Email, token = resetToken })}";

            var (plain, html) = _templates.GetForgotPasswordTemplate(user.FullName, resetLink);
            await _notification.EmailUserAsync(user.Email, "Reset Your Password", plain, html, ct);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "Password Reset Requested",
                TableName = "Users",
                NewValues = $"Reset email sent to {normalizedEmail}",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                ActionDate = ViaReservaERP.AppTime.Now
            });
            await _db.SaveChangesAsync(ct);
        }

        // We return the confirmation view regardless of whether the user exists (security best practice)
        return RedirectToAction("ForgotPasswordConfirmation");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login");

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == model.Email && !u.IsDeleted && u.IsActive, ct);

        if (user != null)
        {
            user.PasswordHash = PasswordHasher.Hash(model.Password);

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = user.UserId,
                Action = "Password Reset Success",
                TableName = "Users",
                NewValues = $"Password successfully reset for {model.Email}",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                ActionDate = ViaReservaERP.AppTime.Now
            });
            await _db.SaveChangesAsync(ct);

            TempData["SuccessMessage"] = "Your password has been reset successfully. Please log in with your new password.";
            return RedirectToAction("Login");
        }

        return RedirectToAction("Login");
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Register(int? companyId, CancellationToken ct = default)
    {
        if (!companyId.HasValue)
        {
            return RedirectToAction("Login", "Account");
        }

        var companyExists = await _db.Companies
            .AsNoTracking()
            .AnyAsync(c => c.CompanyId == companyId.Value && !c.IsDeleted && c.IsActive, ct);

        if (!companyExists)
        {
            return NotFound();
        }

        return View(new RegisterViewModel { CompanyId = companyId.Value });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.Email = (model.Email ?? string.Empty).Trim().ToLowerInvariant();

        var companyExists = await _db.Companies
            .AsNoTracking()
            .AnyAsync(c => c.CompanyId == model.CompanyId && !c.IsDeleted && c.IsActive, ct);

        if (!companyExists)
        {
            return NotFound();
        }

        var emailExists = await _db.Users
            .AnyAsync(u => u.Email == model.Email, ct);

        if (emailExists)
        {
            ModelState.AddModelError(nameof(RegisterViewModel.Email), "Email is already registered.");
            return View(model);
        }

        var user = new ErpUser
        {
            CompanyId = model.CompanyId,
            RoleId = 6,
            FullName = model.FullName,
            Email = model.Email,
            PasswordHash = PasswordHasher.Hash(model.Password),
            IsActive = true,
            IsDeleted = false,
            CreatedAt = ViaReservaERP.AppTime.Now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        await SignInUserAsync(user, rememberMe: true, ct);

        if (user.RoleId == 1)
        {
            return RedirectToAction("Dashboard", "SuperAdmin");
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct = default)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var companyIdStr = User.FindFirstValue(ViaReservaClaims.CompanyId);
        var email = User.FindFirstValue(ClaimTypes.Email);

        if (int.TryParse(userIdStr, out var userId))
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                CompanyId = int.TryParse(companyIdStr, out var cid) ? cid : null,
                Action = "Logout",
                TableName = "Users",
                NewValues = $"User {email} logged out successfully.",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                ActionDate = ViaReservaERP.AppTime.Now
            });
            await _db.SaveChangesAsync(ct);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    private async Task SignInUserAsync(ErpUser user, bool rememberMe, CancellationToken ct)
    {
        var roleName = RoleIdMapper.ToRoleName(user.RoleId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, roleName),
            new(ViaReservaClaims.RoleId, user.RoleId.ToString()),
            new(ViaReservaClaims.CompanyId, user.CompanyId.ToString()),
            new(ViaReservaClaims.UserId, user.UserId.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
            });
    }
}
