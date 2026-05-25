using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.SaaS;
using ViaReservaERP.Security;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

[AllowAnonymous]
public class SubscriptionController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IConfiguration _config;
    private readonly INotificationService _notify;
    private readonly IEmailTemplateService _templates;

    public SubscriptionController(
        ViaReservaDbContext db, 
        IConfiguration config, 
        INotificationService notify,
        IEmailTemplateService templates)
    {
        _db = db;
        _config = config;
        _notify = notify;
        _templates = templates;
    }

    [HttpGet]
    public IActionResult Register(string? plan)
    {
        var normalizedPlan = (plan ?? string.Empty).Trim();
        if (!SubscriptionPlansCatalog.IsSupportedPlan(normalizedPlan))
            return NotFound();

        var vm = new SubscriptionRegisterViewModel
        {
            Plan = normalizedPlan
        };

        ViewData["Title"] = "Register";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(SubscriptionRegisterViewModel model, CancellationToken ct)
    {
        model.Plan = (model.Plan ?? string.Empty).Trim();
        if (!SubscriptionPlansCatalog.IsSupportedPlan(model.Plan))
            return NotFound();

        if (!ModelState.IsValid)
            return View(model);

        var adminEmail = (model.AdminEmail ?? string.Empty).Trim().ToLowerInvariant();
        var companyEmail = string.IsNullOrWhiteSpace(model.BusinessEmail) ? adminEmail : model.BusinessEmail.Trim().ToLowerInvariant();

        var emailExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == adminEmail && !u.IsDeleted, ct);

        if (emailExists)
        {
            ModelState.AddModelError(nameof(SubscriptionRegisterViewModel.AdminEmail), "Email is already registered.");
            return View(model);
        }

        var planInfo = SubscriptionPlansCatalog.GetPlan(model.Plan);

        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var company = new Company
            {
                CompanyName = model.CompanyName,
                Email = companyEmail,
                Phone = model.PhoneNumber,
                Address = model.HotelAddress,
                SubscriptionStatus = "Pending Payment",
                IsActive = false,
                CreatedAt = ViaReservaERP.AppTime.Now
            };
            _db.Companies.Add(company);
            await _db.SaveChangesAsync(ct);

            var planRow = await _db.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.PlanName != null && p.PlanName.ToLower() == planInfo.PlanName.ToLower(), ct);

            if (planRow == null)
            {
                planRow = new SubscriptionPlan
                {
                    PlanName = planInfo.PlanName,
                    Price = planInfo.MonthlyPrice,
                    DurationMonths = 1
                };
                _db.SubscriptionPlans.Add(planRow);
                await _db.SaveChangesAsync(ct);
            }

            var subscription = new ViaReservaERP.Models.Subscription
            {
                CompanyId = company.CompanyId,
                PlanId = planRow.PlanId,
                StartDate = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now),
                EndDate = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.AddMonths(1)),
                Status = "Pending",
                StripeSubscriptionId = null
            };
            _db.Subscriptions.Add(subscription);

            await _db.SaveChangesAsync(ct);

            var admin = new ErpUser
            {
                CompanyId = company.CompanyId,
                RoleId = 2,
                FullName = model.AdminFullName,
                Email = adminEmail,
                Phone = model.PhoneNumber,
                PasswordHash = PasswordHasher.Hash(model.Password),
                IsActive = true,
                CreatedAt = ViaReservaERP.AppTime.Now
            };
            _db.Users.Add(admin);

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = company.CompanyId,
                Action = "Company Signup Started",
                TableName = "Subscriptions",
                RecordId = subscription.SubscriptionId,
                NewValues = $"Plan={planInfo.PlanName}; Admin={adminEmail}; Rooms={model.NumberOfRooms}; BusinessType={model.BusinessType}; OwnerManager={model.OwnerManagerName}",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await _notify.NotifySuperAdminsAsync(
                title: "New Company Signup",
                message: $"{company.CompanyName} started signup for {planInfo.PlanName}.",
                type: "Onboarding",
                ct);

            try
            {
                var baseUrl = _config["BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
                var checkoutUrl = $"{baseUrl}{Url.Action(nameof(ProcessPayment), "Subscription", new { subscriptionId = subscription.SubscriptionId })}";
                
                var (plain, html) = _templates.GetSubscriptionWelcomeTemplate(
                    model.AdminFullName,
                    company.CompanyName,
                    planInfo.PlanName,
                    checkoutUrl
                );

                await _notify.EmailUserAsync(
                    toEmail: adminEmail,
                    subject: "Welcome to ViaReserva - Complete Your Signup",
                    plainText: plain,
                    html: html,
                    ct);
            }
            catch (Exception ex)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = company.CompanyId,
                    Action = "Email Failed",
                    TableName = "Notifications",
                    NewValues = ex.Message,
                    ActionDate = ViaReservaERP.AppTime.Now
                });
                await _db.SaveChangesAsync(ct);
            }

            return RedirectToAction(nameof(ProcessPayment), new { subscriptionId = subscription.SubscriptionId });
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProcessPayment(int subscriptionId, CancellationToken ct)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, ct);

        if (sub == null)
            return NotFound();

        if (sub.CompanyId == null || sub.Company == null)
            return NotFound();

        if (sub.Plan == null || string.IsNullOrWhiteSpace(sub.Plan.PlanName))
            return NotFound();

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            return BadRequest("Stripe secret key is not configured.");

        var planName = sub.Plan.PlanName.Trim();
        var priceId = planName.Equals("Basic", StringComparison.OrdinalIgnoreCase)
            ? _config["Stripe:BasicPriceId"]
            : planName.Equals("Standard", StringComparison.OrdinalIgnoreCase)
                ? _config["Stripe:StandardPriceId"]
                : null;

        if (string.IsNullOrWhiteSpace(priceId))
            return BadRequest($"Stripe price id is not configured for plan '{planName}'. Set Stripe:BasicPriceId and Stripe:StandardPriceId.");

        StripeConfiguration.ApiKey = secretKey;

        var successUrl = Url.Action(nameof(Success), "Subscription", null, Request.Scheme) + "?session_id={CHECKOUT_SESSION_ID}";
        var cancelUrl = Url.Action(nameof(Register), "Subscription", new { plan = planName }, Request.Scheme);

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            Metadata = new Dictionary<string, string>
            {
                ["subscriptionId"] = subscriptionId.ToString(CultureInfo.InvariantCulture),
                ["companyId"] = sub.CompanyId.Value.ToString(CultureInfo.InvariantCulture),
                ["plan"] = planName
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(session.Url))
            return BadRequest("Stripe did not return a checkout URL.");

        return Redirect(session.Url);
    }

    [HttpGet]
    public async Task<IActionResult> Success(string? session_id, CancellationToken ct)
    {
        var vm = new SubscriptionSuccessViewModel();

        if (string.IsNullOrWhiteSpace(session_id))
        {
            vm.Message = "Subscription payment completed.";
            ViewData["Title"] = "Success";
            return View(vm);
        }

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            vm.Message = "Subscription payment completed.";
            ViewData["Title"] = "Success";
            return View(vm);
        }

        StripeConfiguration.ApiKey = secretKey;

        var service = new SessionService();
        var session = await service.GetAsync(session_id, cancellationToken: ct);

        var subscriptionIdRaw = session.Metadata != null && session.Metadata.TryGetValue("subscriptionId", out var sid) ? sid : null;
        if (!int.TryParse(subscriptionIdRaw, out var subscriptionId))
        {
            vm.Message = "Subscription payment completed.";
            ViewData["Title"] = "Success";
            return View(vm);
        }

        await ActivateSubscriptionIfNeededAsync(subscriptionId, session.SubscriptionId, ct);

        var sub = await _db.Subscriptions
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, ct);

        vm.CompanyName = sub?.Company?.CompanyName;
        vm.PlanName = sub?.Plan?.PlanName;
        vm.Message = "Payment successful. Your company and admin account have been created.";

        ViewData["Title"] = "Success";
        return View(vm);
    }

    private async Task ActivateSubscriptionIfNeededAsync(int subscriptionId, string? stripeSubscriptionId, CancellationToken ct)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, ct);

        if (sub == null || sub.CompanyId == null)
            return;

        var alreadyActive = string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase);
        if (alreadyActive && !string.IsNullOrWhiteSpace(sub.StripeSubscriptionId))
            return;

        sub.Status = "Active";
        sub.StripeSubscriptionId = string.IsNullOrWhiteSpace(stripeSubscriptionId) ? sub.StripeSubscriptionId : stripeSubscriptionId;

        if (sub.Company != null)
        {
            sub.Company.SubscriptionStatus = "Active";
            sub.Company.IsActive = true;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = sub.CompanyId,
            Action = "Subscription Activated",
            TableName = "Subscriptions",
            RecordId = sub.SubscriptionId,
            NewValues = sub.StripeSubscriptionId,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);

        var companyAdminId = await _db.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == sub.CompanyId.Value && u.RoleId == 2 && u.IsActive && !u.IsDeleted)
            .Select(u => (int?)u.UserId)
            .FirstOrDefaultAsync(ct);

        if (companyAdminId.HasValue)
        {
            var adminUser = await _db.Users.FindAsync(new object[] { companyAdminId.Value }, ct);
            if (adminUser != null)
            {
                await _notify.NotifyUserAsync(
                    userId: companyAdminId.Value,
                    title: "Subscription Active",
                    message: "Your subscription is now active. You can log in and start onboarding.",
                    type: "Onboarding",
                    companyId: sub.CompanyId,
                    ct);

                try
                {
                    var baseUrl = _config["BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
                    var loginUrl = $"{baseUrl}{Url.Action("Login", "Account")}";
                    
                    var (plain, html) = _templates.GetSubscriptionActiveTemplate(
                        adminUser.FullName ?? "Administrator",
                        sub.Company?.CompanyName ?? "Your Company",
                        sub.Plan?.PlanName ?? "Subscription",
                        loginUrl
                    );

                    await _notify.EmailUserAsync(
                        toEmail: adminUser.Email ?? "",
                        subject: "Your ViaReserva Workspace is Active!",
                        plainText: plain,
                        html: html,
                        ct);
                }
                catch (Exception ex)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        CompanyId = sub.CompanyId,
                        Action = "Email Failed (Active)",
                        TableName = "Notifications",
                        NewValues = ex.Message,
                        ActionDate = ViaReservaERP.AppTime.Now
                    });
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        await _notify.NotifySuperAdminsAsync(
            title: "Subscription Activated",
            message: $"CompanyId {sub.CompanyId} subscription activated.",
            type: "Onboarding",
            ct);
    }
}
