using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

[AllowAnonymous]
[ApiController]
[Route("stripe/webhook")]
public class StripeWebhookController : ControllerBase
{
    private readonly ViaReservaDbContext _db;
    private readonly IConfiguration _config;
    private readonly INotificationService _notify;
    private readonly IEmailTemplateService _templates;

    public StripeWebhookController(
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

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return BadRequest("Stripe webhook secret not configured.");

        var json = await new StreamReader(HttpContext.Request.Body, Encoding.UTF8).ReadToEndAsync(ct);
        var sigHeader = Request.Headers["Stripe-Signature"].ToString();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, sigHeader, webhookSecret);
        }
        catch
        {
            return BadRequest();
        }

        if (string.Equals(stripeEvent.Type, "payment_intent.succeeded", StringComparison.OrdinalIgnoreCase))
        {
            if (stripeEvent.Data.Object is PaymentIntent pi)
            {
                await UpdatePaymentStatusAsync(pi.Id, "Succeeded", ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "payment_intent.payment_failed", StringComparison.OrdinalIgnoreCase))
        {
            if (stripeEvent.Data.Object is PaymentIntent pi)
            {
                await UpdatePaymentStatusAsync(pi.Id, "Failed", ct);
                await AlertAdminsAsync(pi.Id, "Payment Failed", $"Stripe payment failed for PaymentIntent {pi.Id}.", ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "charge.refunded", StringComparison.OrdinalIgnoreCase))
        {
            if (stripeEvent.Data.Object is Charge charge && !string.IsNullOrWhiteSpace(charge.PaymentIntentId))
            {
                await UpdatePaymentStatusAsync(charge.PaymentIntentId, "Refunded", ct);
                await AlertAdminsAsync(charge.PaymentIntentId, "Refund Issued", $"Stripe refund processed for PaymentIntent {charge.PaymentIntentId}.", ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "checkout.session.completed", StringComparison.OrdinalIgnoreCase))
        {
            if (stripeEvent.Data.Object is Session session)
            {
                await TryActivateSubscriptionAsync(session, ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "invoice.paid", StringComparison.OrdinalIgnoreCase))
        {
            var stripeSubscriptionId = TryGetInvoiceSubscriptionId(json);
            if (!string.IsNullOrWhiteSpace(stripeSubscriptionId))
            {
                await UpdateSubscriptionStatusByStripeIdAsync(stripeSubscriptionId, "Active", ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "invoice.payment_failed", StringComparison.OrdinalIgnoreCase))
        {
            var stripeSubscriptionId = TryGetInvoiceSubscriptionId(json);
            if (!string.IsNullOrWhiteSpace(stripeSubscriptionId))
            {
                await UpdateSubscriptionStatusByStripeIdAsync(stripeSubscriptionId, "PastDue", ct);
                await _notify.NotifySuperAdminsAsync(
                    title: "Subscription Payment Failed",
                    message: $"Stripe invoice payment failed for Subscription {stripeSubscriptionId}.",
                    type: "Alert",
                    ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "customer.subscription.updated", StringComparison.OrdinalIgnoreCase))
        {
            if (stripeEvent.Data.Object is Stripe.Subscription stripeSub && !string.IsNullOrWhiteSpace(stripeSub.Id))
            {
                var status = NormalizeStripeSubscriptionStatus(stripeSub.Status);
                await UpdateSubscriptionStatusByStripeIdAsync(stripeSub.Id, status, ct);
            }
        }
        else if (string.Equals(stripeEvent.Type, "customer.subscription.deleted", StringComparison.OrdinalIgnoreCase))
        {
            if (stripeEvent.Data.Object is Stripe.Subscription stripeSub && !string.IsNullOrWhiteSpace(stripeSub.Id))
            {
                await UpdateSubscriptionStatusByStripeIdAsync(stripeSub.Id, "Cancelled", ct);
            }
        }

        return Ok();
    }

    private static string NormalizeStripeSubscriptionStatus(string? stripeStatus)
    {
        var s = (stripeStatus ?? string.Empty).Trim().ToLowerInvariant();

        if (s == "active" || s == "trialing") return "Active";
        if (s == "past_due" || s == "unpaid") return "PastDue";
        if (s == "canceled" || s == "cancelled") return "Cancelled";
        if (s == "incomplete" || s == "incomplete_expired") return "Pending";
        return "Pending";
    }

    private static string? TryGetInvoiceSubscriptionId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return null;
            if (!dataEl.TryGetProperty("object", out var objEl))
                return null;
            if (!objEl.TryGetProperty("subscription", out var subEl))
                return null;

            return subEl.ValueKind == JsonValueKind.String ? subEl.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateSubscriptionStatusByStripeIdAsync(string stripeSubscriptionId, string status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
            return;

        var sub = await _db.Subscriptions
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

        if (sub == null)
            return;

        sub.Status = status;

        if (sub.Company != null)
        {
            sub.Company.SubscriptionStatus = status;

            if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                sub.Company.IsActive = true;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = sub.CompanyId,
            Action = $"Stripe Webhook: Subscription {status}",
            TableName = "Subscriptions",
            RecordId = sub.SubscriptionId,
            NewValues = stripeSubscriptionId,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task TryActivateSubscriptionAsync(Session session, CancellationToken ct)
    {
        if (!string.Equals(session.Mode, "subscription", StringComparison.OrdinalIgnoreCase))
            return;

        if (session.Metadata == null || !session.Metadata.TryGetValue("subscriptionId", out var rawSubscriptionId))
            return;

        if (!int.TryParse(rawSubscriptionId, out var subscriptionId))
            return;

        var sub = await _db.Subscriptions
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, ct);

        if (sub == null)
            return;

        sub.Status = "Active";
        if (!string.IsNullOrWhiteSpace(session.SubscriptionId))
            sub.StripeSubscriptionId = session.SubscriptionId;

        if (sub.Company != null)
        {
            sub.Company.SubscriptionStatus = "Active";
            sub.Company.IsActive = true;
            if (!string.IsNullOrWhiteSpace(session.CustomerId))
                sub.Company.StripeCustomerId = session.CustomerId;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = sub.CompanyId,
            Action = "Stripe Webhook: Subscription Activated",
            TableName = "Subscriptions",
            RecordId = sub.SubscriptionId,
            NewValues = sub.StripeSubscriptionId,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);

        if (sub.CompanyId.HasValue)
        {
            await _notify.NotifyRoleAsync(sub.CompanyId.Value, roleId: 2, "Subscription Active", "Stripe confirmed the subscription payment. Your account is now active.", "Onboarding", ct);
            
            var companyAdmin = await _db.Users
                .AsNoTracking()
                .Where(u => u.CompanyId == sub.CompanyId.Value && u.RoleId == 2 && u.IsActive && !u.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (companyAdmin != null)
            {
                try
                {
                    var baseUrl = _config["BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
                    var loginUrl = $"{baseUrl}{Url.Action("Login", "Account")}";
                    
                    var (plain, html) = _templates.GetSubscriptionActiveTemplate(
                        companyAdmin.FullName ?? "Administrator",
                        sub.Company?.CompanyName ?? "Your Company",
                        sub.Plan?.PlanName ?? "Subscription",
                        loginUrl
                    );

                    await _notify.EmailUserAsync(
                        toEmail: companyAdmin.Email ?? "",
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
                        Action = "Email Failed (Webhook Active)",
                        TableName = "Notifications",
                        NewValues = ex.Message,
                        ActionDate = ViaReservaERP.AppTime.Now
                    });
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        await _notify.NotifySuperAdminsAsync("Subscription Activated", $"Stripe checkout completed for SubscriptionId {sub.SubscriptionId}.", "Onboarding", ct);
    }

    private async Task UpdatePaymentStatusAsync(string paymentIntentId, string status, CancellationToken ct)
    {
        var payment = await _db.Payments
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId, ct);

        if (payment is null)
            return;

        payment.Status = status;
        await _db.SaveChangesAsync(ct);

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = payment.CompanyId,
            Action = $"Stripe Webhook: Payment {status}",
            TableName = "Payments",
            RecordId = payment.PaymentId,
            NewValues = paymentIntentId,
            ActionDate = ViaReservaERP.AppTime.Now
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task AlertAdminsAsync(string paymentIntentId, string title, string message, CancellationToken ct)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId, ct);

        var companyId = payment?.CompanyId;
        if (companyId.HasValue)
        {
            await _notify.NotifyRoleAsync(companyId.Value, roleId: 2, title, message, type: "Alert", ct);
            await _notify.NotifyRoleAsync(companyId.Value, roleId: 3, title, message, type: "Alert", ct);
        }

        await _notify.NotifySuperAdminsAsync(title, message, type: "Alert", ct);
    }
}
