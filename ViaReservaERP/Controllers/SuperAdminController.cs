using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.SuperAdmin;
using ViaReservaERP.Security;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.SuperAdmin)]
public class SuperAdminController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ISystemValidationService _validation;

    private static readonly TimeZoneInfo PhTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

    public SuperAdminController(ViaReservaDbContext db, IWebHostEnvironment env, IConfiguration config, ISystemValidationService validation)
    {
        _db = db;
        _env = env;
        _config = config;
        _validation = validation;
    }

    private int? CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ViaReservaClaims.UserId);
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    private static string FormatPhilippinesTime(DateTime dt)
    {
        var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        var ph = TimeZoneInfo.ConvertTimeFromUtc(utc, PhTimeZone);
        return ph.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    [HttpGet]
    public async Task<IActionResult> SystemSettings(string? q = null, CancellationToken ct = default)
    {
        var activeCompanies = await _db.Companies.AsNoTracking().CountAsync(c => !c.IsDeleted && c.IsActive, ct);
        var activeUsers = await _db.Users.AsNoTracking().CountAsync(u => !u.IsDeleted && u.IsActive, ct);
        var activePlans = await _db.SubscriptionPlans.AsNoTracking().CountAsync(ct);
        var workflowAutomations = await _db.Workflows.AsNoTracking().CountAsync(ct);

        var nowUtc = ViaReservaERP.AppTime.Now;
        var securityWindowStart = nowUtc.AddDays(-7);
        var securityAlerts = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionDate >= securityWindowStart)
            .CountAsync(a =>
                (a.Action ?? "").ToLower().Contains("unauthorized") ||
                (a.Action ?? "").ToLower().Contains("forbidden") ||
                (a.Action ?? "").ToLower().Contains("suspicious") ||
                (a.Action ?? "").ToLower().Contains("failed login") ||
                ((a.Action ?? "").ToLower().Contains("login") && (a.Action ?? "").ToLower().Contains("fail")),
                ct);

        var integrations = new List<SystemIntegrationStatusRow>
        {
            new()
            {
                IntegrationName = "Stripe",
                IsConfigured = !string.IsNullOrWhiteSpace(_config["Stripe:SecretKey"]) || !string.IsNullOrWhiteSpace(_config["Stripe:ApiKey"]),
                StatusLabel = "Payments"
            },
            new()
            {
                IntegrationName = "SendGrid",
                IsConfigured = !string.IsNullOrWhiteSpace(_config["SendGrid:ApiKey"]) || !string.IsNullOrWhiteSpace(_config["SendGrid:Key"]),
                StatusLabel = "Email"
            }
        };
        var activeIntegrations = integrations.Count(i => i.IsConfigured);

        var companiesPendingActivation = await _db.Companies.AsNoTracking().CountAsync(c => !c.IsDeleted && !c.IsActive, ct);
        var suspendedCompanies = await _db.Companies.AsNoTracking().CountAsync(c => !c.IsDeleted && ((c.SubscriptionStatus ?? "").ToLower().Contains("suspend") || (c.SubscriptionStatus ?? "").ToLower().Contains("past") || (c.SubscriptionStatus ?? "").ToLower().Contains("over")), ct);

        var tenantMaxUsers = await _db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(ct);
        var tenantCounts = await _db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .GroupBy(u => u.CompanyId)
            .Select(g => g.Count())
            .ToListAsync(ct);
        var tenantAvgUsers = tenantCounts.Count == 0 ? 0d : tenantCounts.Average();

        var permissionsTotal = await _db.Permissions.AsNoTracking().CountAsync(ct);

        var roleRows = await _db.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .Include(r => r.Users)
            .OrderBy(r => r.RoleId)
            .Select(r => new RoleAccessMonitoringRow
            {
                RoleId = r.RoleId,
                RoleName = r.RoleName,
                Users = r.Users.Count(u => !u.IsDeleted && u.IsActive),
                Permissions = r.RolePermissions.Count
            })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            roleRows = roleRows
                .Where(r => (r.RoleName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var workflowSteps = await _db.WorkflowSteps.AsNoTracking().CountAsync(ct);
        var escalationRules = await _db.EscalationRules.AsNoTracking().CountAsync(ct);
        var escalationMinutes = await _db.EscalationRules
            .AsNoTracking()
            .Where(r => r.EscalateAfterMinutes != null)
            .Select(r => r.EscalateAfterMinutes!.Value)
            .ToListAsync(ct);
        var avgEscalationMinutes = escalationMinutes.Count == 0 ? 0d : escalationMinutes.Average();

        var activeSubscriptions = await _db.Subscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status != null && s.Status.ToLower() == "active", ct);
        var trialSubscriptions = await _db.Subscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status != null && s.Status.ToLower().Contains("trial"), ct);

        var billingCycleDist = await _db.SubscriptionPlans
            .AsNoTracking()
            .GroupBy(p => p.DurationMonths)
            .Select(g => new BillingCycleDistributionRow { DurationMonths = g.Key, Plans = g.Count() })
            .OrderBy(x => x.DurationMonths)
            .ToListAsync(ct);

        var canConnect = await _db.Database.CanConnectAsync(ct);
        var healthWindowStart = nowUtc.AddHours(-24);
        var errorsLast24h = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionDate >= healthWindowStart)
            .CountAsync(a => (a.Action ?? "").ToLower().Contains("error") || (a.Action ?? "").ToLower().Contains("fail"), ct);
        var failedPaymentsLast24h = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CreatedAt >= healthWindowStart)
            .CountAsync(p => p.Status == null || p.Status.ToLower() != "succeeded", ct);

        var auditTrailRows = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Company)
            .OrderByDescending(a => a.ActionDate)
            .Where(a =>
                (a.TableName ?? "").ToLower().Contains("role") ||
                (a.TableName ?? "").ToLower().Contains("permission") ||
                (a.TableName ?? "").ToLower().Contains("subscription") ||
                (a.TableName ?? "").ToLower().Contains("workflow") ||
                (a.TableName ?? "").ToLower().Contains("escalation") ||
                (a.Action ?? "").ToLower().Contains("setting") ||
                (a.Action ?? "").ToLower().Contains("config"))
            .Take(250)
            .Select(a => new SettingsAuditTrailRow
            {
                AuditId = a.AuditId,
                Actor = a.User != null ? a.User.FullName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Area = a.TableName ?? "",
                Action = a.Action ?? "",
                WhenUtc = a.ActionDate,
                IpAddress = a.IPAddress ?? ""
            })
            .ToListAsync(ct);

        var model = new SystemSettingsViewModel
        {
            SearchQuery = q,

            ActiveCompanies = activeCompanies,
            ActiveUsers = activeUsers,
            ActiveIntegrations = activeIntegrations,
            SecurityAlertsLast7Days = securityAlerts,
            ActiveSubscriptionPlans = activePlans,
            WorkflowAutomations = workflowAutomations,

            CompanyOnboarding = new MultiTenantCompanySettingsSummary
            {
                PendingActivationCompanies = companiesPendingActivation,
                SuspendedCompanies = suspendedCompanies,
                TenantMaxUsers = tenantMaxUsers != null ? tenantMaxUsers.Count : 0,
                TenantAverageUsers = tenantAvgUsers,
                PermissionsDefined = permissionsTotal
            },

            RoleAccess = roleRows,

            Security = new SecuritySettingsSummary
            {
                CookieSessionHours = 12,
                MfaConfigured = false,
                IpRestrictionMode = "Not Enforced",
                LoginAttemptControls = "Monitored via Audit Logs",
                EncryptionMode = "Database + Transport (TLS)"
            },

            Workflow = new WorkflowAutomationSettingsSummary
            {
                Workflows = workflowAutomations,
                WorkflowSteps = workflowSteps,
                EscalationRules = escalationRules,
                AverageEscalationMinutes = avgEscalationMinutes
            },

            Subscription = new SubscriptionSettingsSummary
            {
                Plans = activePlans,
                ActiveSubscriptions = activeSubscriptions,
                TrialSubscriptions = trialSubscriptions,
                BillingCycles = billingCycleDist
            },

            Notifications = new NotificationSettingsSummary
            {
                EmailProvider = integrations.FirstOrDefault(i => i.IntegrationName == "SendGrid")?.IsConfigured == true ? "SendGrid" : "Not Configured",
                AlertsLast7Days = securityAlerts
            },

            Integrations = integrations,

            Compliance = new AuditComplianceSettingsSummary
            {
                RetentionPolicyDays = 90,
                ComplianceMonitoring = "Enabled (Audit Log Signal)"
            },

            Backup = new BackupRecoverySettingsSummary
            {
                BackupMode = "Managed via SQL Server",
                Scheduling = "External Scheduler",
                DisasterRecovery = "DR Plan Required"
            },

            Health = new SystemHealthSummary
            {
                DatabaseConnectivity = canConnect ? "OK" : "Unavailable",
                ApiUptime = "Live",
                ErrorsLast24Hours = errorsLast24h,
                FailedPaymentsLast24Hours = failedPaymentsLast24h
            },

            AuditTrail = auditTrailRows
        };

        return View("SystemSettings", model);
    }

    [HttpGet]
    public async Task<IActionResult> SystemValidation(bool escalate = false, CancellationToken ct = default)
    {
        ViewData["Title"] = "System Validation";

        var userId = CurrentUserId ?? 0;
        var model = escalate
            ? await _validation.RunAndEscalateAsync(userId, ct)
            : await _validation.RunAsync(ct);

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
        {
            granularity = "month";
        }

        DateOnly start;
        DateOnly end;

        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start)
            {
                (start, end) = (end, start);
            }
        }
        else
        {
            if (granularity == "day")
            {
                start = today;
                end = today;
            }
            else if (granularity == "year")
            {
                start = new DateOnly(today.Year, 1, 1);
                end = today;
            }
            else
            {
                start = new DateOnly(today.Year, today.Month, 1);
                end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }
        }

        var spanDays = Math.Max(1, end.DayNumber - start.DayNumber + 1);

        int compareMonthOffset;
        int compareYearOffset;

        if (granularity == "month")
        {
            compareMonthOffset = Math.Max(1, (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1);
            compareYearOffset = 0;
        }
        else if (granularity == "year")
        {
            compareYearOffset = Math.Max(1, (end.Year - start.Year) + 1);
            compareMonthOffset = 0;
        }
        else
        {
            compareMonthOffset = 0;
            compareYearOffset = 0;
        }

        DateOnly compareStart;
        DateOnly compareEnd;

        if (granularity == "month")
        {
            compareStart = start.AddMonths(-compareMonthOffset);
            compareEnd = end.AddMonths(-compareMonthOffset);
        }
        else if (granularity == "year")
        {
            compareStart = start.AddYears(-compareYearOffset);
            compareEnd = end.AddYears(-compareYearOffset);
        }
        else
        {
            compareEnd = start.AddDays(-1);
            compareStart = compareEnd.AddDays(-(spanDays - 1));
        }

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);
        var compareStartUtc = AsUtcStart(compareStart);
        var compareEndUtcExcl = AsUtcEndExclusive(compareEnd);

        var totalHotelCompanies = await _db.Companies.AsNoTracking().CountAsync(c => !c.IsDeleted, ct);
        var totalActiveUsers = await _db.Users.AsNoTracking().CountAsync(u => !u.IsDeleted && u.IsActive, ct);
        var activeSubscriptions = await _db.Subscriptions.AsNoTracking()
            .Join(_db.Companies, s => s.CompanyId, c => c.CompanyId, (s, c) => new { s, c })
            .CountAsync(x => !x.c.IsDeleted && x.s.Status != null && x.s.Status.ToLower() == "active", ct);

        var totalReservationsCurr = await _db.Reservations.AsNoTracking().CountAsync(r => r.CheckInDate != null && r.CheckInDate.Value >= start && r.CheckInDate.Value <= end, ct);
        var totalReservationsPrev = await _db.Reservations.AsNoTracking().CountAsync(r => r.CheckInDate != null && r.CheckInDate.Value >= compareStart && r.CheckInDate.Value <= compareEnd, ct);

        var totalRevenueCurr = await _db.Payments.AsNoTracking()
            .Where(p => p.Amount != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;

        var totalRevenuePrev = await _db.Payments.AsNoTracking()
            .Where(p => p.Amount != null && p.CreatedAt >= compareStartUtc && p.CreatedAt < compareEndUtcExcl)
            .SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;

        var pendingServiceRequestsCurr = await _db.ServiceRequests.AsNoTracking().CountAsync(sr =>
            sr.Status != null && sr.Status.ToLower() == "pending" &&
            sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl, ct);

        var pendingServiceRequestsPrev = await _db.ServiceRequests.AsNoTracking().CountAsync(sr =>
            sr.Status != null && sr.Status.ToLower() == "pending" &&
            sr.RequestDate >= compareStartUtc && sr.RequestDate < compareEndUtcExcl, ct);

        var workflowPendingApprovalsCurr = await _db.WorkflowInstances.AsNoTracking().CountAsync(wi =>
            wi.Status != null && (wi.Status.ToLower() == "pending" || wi.Status.ToLower() == "inprogress") &&
            wi.CreatedAt >= startUtc && wi.CreatedAt < endUtcExcl, ct);

        var workflowPendingApprovalsPrev = await _db.WorkflowInstances.AsNoTracking().CountAsync(wi =>
            wi.Status != null && (wi.Status.ToLower() == "pending" || wi.Status.ToLower() == "inprogress") &&
            wi.CreatedAt >= compareStartUtc && wi.CreatedAt < compareEndUtcExcl, ct);

        var subsExpiringSoon = await _db.Subscriptions.AsNoTracking().CountAsync(s =>
            s.EndDate != null &&
            s.EndDate.Value <= today.AddDays(14) &&
            s.EndDate.Value >= today &&
            s.Status != null && s.Status.ToLower() == "active", ct);

        var failedPaymentsInRange = await _db.Payments.AsNoTracking().CountAsync(p =>
            p.Status != null && p.Status.ToLower() != "succeeded" &&
            p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl, ct);

        static string RangeLabel(string g, DateOnly s, DateOnly e)
        {
            if (g == "day") return s.ToString("yyyy-MM-dd");
            if (g == "year") return (s.Year == e.Year && s.Month == 1 && s.Day == 1) ? e.ToString("yyyy") : $"{s:yyyy-MM-dd} to {e:yyyy-MM-dd}";
            return $"{s:yyyy-MM-dd} to {e:yyyy-MM-dd}";
        }

        var model = new SuperAdminDashboardViewModel
        {
            Granularity = granularity,
            StartDate = start,
            EndDate = end,
            CompareStartDate = compareStart,
            CompareEndDate = compareEnd,
            SelectedRangeLabel = RangeLabel(granularity, start, end),
            TotalHotelCompanies = totalHotelCompanies,
            TotalActiveUsers = totalActiveUsers,
            TotalReservations = totalReservationsCurr,
            TotalReservationsDeltaPct = CalcDeltaPct(totalReservationsCurr, totalReservationsPrev),
            TotalRevenue = totalRevenueCurr,
            TotalRevenueDeltaPct = CalcDeltaPct((double)totalRevenueCurr, (double)totalRevenuePrev),
            ActiveSubscriptions = activeSubscriptions,
            PendingServiceRequests = pendingServiceRequestsCurr,
            PendingServiceRequestsDeltaPct = CalcDeltaPct(pendingServiceRequestsCurr, pendingServiceRequestsPrev),
            WorkflowPendingApprovals = workflowPendingApprovalsCurr,
            WorkflowPendingApprovalsDeltaPct = CalcDeltaPct(workflowPendingApprovalsCurr, workflowPendingApprovalsPrev),
            SystemAlerts = subsExpiringSoon + failedPaymentsInRange
        };

        await PopulateChartsAsync(model, ct);
        await PopulateTablesAsync(model, ct);

        return View(model);
    }

    private static double? CalcDeltaPct(double current, double previous)
    {
        if (previous <= 0)
        {
            return current <= 0 ? 0 : null;
        }

        return ((current - previous) / previous) * 100.0;
    }

    private static double? CalcDeltaPct(int current, int previous) => CalcDeltaPct((double)current, (double)previous);

    private async Task PopulateChartsAsync(SuperAdminDashboardViewModel model, CancellationToken ct)
    {
        DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        
        var granularity = (model.Granularity ?? "month").Trim().ToLowerInvariant();
        var start = model.StartDate;
        var end = model.EndDate;
        var compareStart = model.CompareStartDate;
        var compareEnd = model.CompareEndDate;
        
        var totalStartUtc = AsUtcStart(compareStart < start ? compareStart : start);
        var totalEndUtcExcl = AsUtcStart(end.AddDays(1));
        var totalStartDate = compareStart < start ? compareStart : start;
        var totalEndDate = end;

        // Fetch Raw Data Sequentially (Thread Safety)
        var allPayments = await _db.Payments.AsNoTracking()
            .Where(p => p.CreatedAt >= totalStartUtc && p.CreatedAt < totalEndUtcExcl)
            .Select(p => new { p.CreatedAt, p.Amount, p.ReservationId }).ToListAsync(ct);
        var allReservations = await _db.Reservations.AsNoTracking()
            .Where(r => r.CheckInDate != null && r.CheckInDate >= totalStartDate && r.CheckInDate <= totalEndDate)
            .Select(r => new { r.CheckInDate, r.CheckOutDate, r.Status }).ToListAsync(ct);
        var allSubs = await _db.Subscriptions.AsNoTracking().Include(s => s.Plan)
            .Where(s => s.StartDate != null && s.StartDate >= totalStartDate && s.StartDate <= totalEndDate)
            .Select(s => new { s.StartDate, Price = s.Plan != null ? (s.Plan.Price ?? 0m) : 0m }).ToListAsync(ct);
        var allCompanies = await _db.Companies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.CreatedAt >= totalStartUtc && c.CreatedAt < totalEndUtcExcl)
            .Select(c => new { c.CreatedAt, c.IsActive }).ToListAsync(ct);
        var allWorkflows = await _db.WorkflowInstances.AsNoTracking()
            .Where(wi => wi.CreatedAt >= totalStartUtc && wi.CreatedAt < totalEndUtcExcl)
            .Select(wi => new { wi.CreatedAt, wi.Status }).ToListAsync(ct);
        var allTransactions = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransactionDate >= totalStartUtc && t.TransactionDate < totalEndUtcExcl)
            .Select(t => new { t.TransactionDate, t.Type, t.Amount }).ToListAsync(ct);

        // Bucketing Logic
        var labels = new List<string>();
        var bucketStarts = new List<DateOnly>();
        var bucketEndsExcl = new List<DateOnly>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                bucketStarts.Add(d); bucketEndsExcl.Add(d.AddDays(1));
            }
        }
        else if (granularity == "year")
        {
            for (var y = start.Year; y <= end.Year; y++)
            {
                labels.Add(y.ToString());
                bucketStarts.Add(new DateOnly(y, 1, 1)); bucketEndsExcl.Add(new DateOnly(y + 1, 1, 1));
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                labels.Add(cursor.ToString("MMM"));
                bucketStarts.Add(cursor); bucketEndsExcl.Add(cursor.AddMonths(1));
                cursor = cursor.AddMonths(1);
            }
        }

        model.RevenueAnalytics.Labels = labels;
        model.ReservationAnalytics.Labels = labels.ToList();
        model.CompanyGrowthAnalytics.Labels = labels.ToList();
        model.WorkflowAnalytics.Labels = labels.ToList();
        model.FinancialAnalytics.Labels = labels.ToList();

        var resRev = new List<decimal>(); var resRevPrev = new List<decimal>();
        var subRev = new List<decimal>(); var subRevPrev = new List<decimal>();
        var bookings = new List<decimal>(); var bookingsPrev = new List<decimal>();
        var cancels = new List<decimal>(); var checkIns = new List<decimal>(); var checkOuts = new List<decimal>();
        var newComps = new List<decimal>(); var activeComps = new List<decimal>(); var inactiveComps = new List<decimal>();
        var wfPend = new List<decimal>(); var wfComp = new List<decimal>(); var wfEsc = new List<decimal>();
        var incList = new List<decimal>(); var expList = new List<decimal>(); var profList = new List<decimal>();

        var spanDays = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        var cmpMonthOff = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
        var cmpYearOff = (end.Year - start.Year) + 1;

        for (var i = 0; i < bucketStarts.Count; i++)
        {
            var sD = bucketStarts[i]; var eD = bucketEndsExcl[i];
            var sU = AsUtcStart(sD); var eU = AsUtcStart(eD);
            DateOnly pSD, pED;
            if (granularity == "month") { pSD = sD.AddMonths(-cmpMonthOff); pED = eD.AddMonths(-cmpMonthOff); }
            else if (granularity == "year") { pSD = sD.AddYears(-cmpYearOff); pED = eD.AddYears(-cmpYearOff); }
            else { pED = sD.AddDays(-1); pSD = pED.AddDays(-(spanDays - 1)); }
            var pSU = AsUtcStart(pSD); var pEU = AsUtcStart(pED);

            resRev.Add(allPayments.Where(p => p.ReservationId != null && p.CreatedAt >= sU && p.CreatedAt < eU).Sum(p => (p.Amount ?? 0m)));
            resRevPrev.Add(allPayments.Where(p => p.ReservationId != null && p.CreatedAt >= pSU && p.CreatedAt < pEU).Sum(p => (p.Amount ?? 0m)));
            subRev.Add(allSubs.Where(s => s.StartDate >= sD && s.StartDate < eD).Sum(s => (s.Price)));
            subRevPrev.Add(allSubs.Where(s => s.StartDate >= pSD && s.StartDate < pED).Sum(s => (s.Price)));

            bookings.Add(allReservations.Count(r => r.CheckInDate >= sD && r.CheckInDate < eD));
            bookingsPrev.Add(allReservations.Count(r => r.CheckInDate >= pSD && r.CheckInDate < pED));
            cancels.Add(allReservations.Count(r => r.Status?.ToLower() == "cancelled" && r.CheckInDate >= sD && r.CheckInDate < eD));
            checkIns.Add(allReservations.Count(r => r.Status?.ToLower() == "checkedin" && r.CheckInDate >= sD && r.CheckInDate < eD));
            checkOuts.Add(allReservations.Count(r => r.Status?.ToLower() == "checkedout" && r.CheckOutDate >= sD && r.CheckOutDate < eD));

            newComps.Add(allCompanies.Count(c => c.CreatedAt >= sU && c.CreatedAt < eU));
            activeComps.Add(allCompanies.Count(c => c.IsActive));
            inactiveComps.Add(allCompanies.Count(c => !c.IsActive));

            wfPend.Add(allWorkflows.Count(w => (w.Status?.ToLower() == "pending" || w.Status?.ToLower() == "inprogress") && w.CreatedAt >= sU && w.CreatedAt < eU));
            wfComp.Add(allWorkflows.Count(w => w.Status?.ToLower() == "completed" && w.CreatedAt >= sU && w.CreatedAt < eU));
            wfEsc.Add(allWorkflows.Count(w => w.Status?.ToLower() == "escalated" && w.CreatedAt >= sU && w.CreatedAt < eU));

            var iSum = allTransactions.Where(t => t.Type?.ToLower() == "income" && t.TransactionDate >= sU && t.TransactionDate < eU).Sum(t => t.Amount ?? 0m);
            var eSum = allTransactions.Where(t => (t.Type?.ToLower() == "expense" || t.Type?.ToLower() == "expenses") && t.TransactionDate >= sU && t.TransactionDate < eU).Sum(t => t.Amount ?? 0m);
            incList.Add(iSum); expList.Add(eSum); profList.Add(iSum - eSum);
        }

        model.RevenueAnalytics.Datasets["Subscription Revenue"] = subRev;
        model.RevenueAnalytics.Datasets["Reservation Revenue"] = resRev;
        model.RevenueAnalytics.Datasets["Subscription Revenue (Prev)"] = subRevPrev;
        model.RevenueAnalytics.Datasets["Reservation Revenue (Prev)"] = resRevPrev;
        model.ReservationAnalytics.Datasets["Bookings"] = bookings;
        model.ReservationAnalytics.Datasets["Bookings (Prev)"] = bookingsPrev;
        model.ReservationAnalytics.Datasets["Check-ins"] = checkIns;
        model.ReservationAnalytics.Datasets["Check-outs"] = checkOuts;
        model.ReservationAnalytics.Datasets["Cancellations"] = cancels;
        model.CompanyGrowthAnalytics.Datasets["New Companies"] = newComps;
        model.CompanyGrowthAnalytics.Datasets["Active"] = activeComps;
        model.CompanyGrowthAnalytics.Datasets["Inactive"] = inactiveComps;
        model.WorkflowAnalytics.Datasets["Pending"] = wfPend;
        model.WorkflowAnalytics.Datasets["Completed"] = wfComp;
        model.WorkflowAnalytics.Datasets["Escalated"] = wfEsc;
        model.FinancialAnalytics.Datasets["Income"] = incList;
        model.FinancialAnalytics.Datasets["Expenses"] = expList;
        model.FinancialAnalytics.Datasets["Profit"] = profList;

        var sTop = await _db.ServiceRequests.AsNoTracking().Join(_db.Services, sr => sr.ServiceId, s => s.ServiceId, (sr, s) => new { sr, s })
            .GroupBy(x => x.s.ServiceName).Select(g => new { ServiceName = g.Key ?? "Unknown", Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(6).ToListAsync(ct);
        var pending = await _db.ServiceRequests.AsNoTracking().CountAsync(sr => sr.Status != null && sr.Status.ToLower() == "pending", ct);
        var completed = await _db.ServiceRequests.AsNoTracking().CountAsync(sr => sr.Status != null && sr.Status.ToLower() == "completed", ct);

        model.ServiceAnalytics.Labels = sTop.Select(x => x.ServiceName).ToList();
        model.ServiceAnalytics.Datasets["Most Requested"] = sTop.Select(x => (decimal)x.Count).ToList();
        model.ServiceAnalytics.Datasets["Pending vs Completed"] = new List<decimal> { pending, completed };
    }

    private async Task PopulateTablesAsync(SuperAdminDashboardViewModel model, CancellationToken ct)
    {
        model.AuditLogsPreview = await _db.AuditLogs.AsNoTracking()
            .Include(a => a.User)
            .OrderByDescending(a => a.ActionDate)
            .Take(6)
            .Select(a => new AuditLogPreviewRow
            {
                TableName = a.TableName ?? "",
                Action = a.Action ?? "",
                Summary = (a.NewValues ?? "").Length > 0 ? "Updated record values" : "Change recorded",
                UserName = a.User != null ? a.User.FullName : "",
                Browser = (a.UserAgent ?? ""),
                IpAddress = a.IPAddress ?? "",
                ActionDate = a.ActionDate
            })
            .ToListAsync(ct);

        model.RecentActivities = await _db.AuditLogs.AsNoTracking()
            .Include(a => a.Company)
            .Include(a => a.User)
            .OrderByDescending(a => a.ActionDate)
            .Take(8)
            .Select(a => new RecentActivityRow
            {
                Time = a.ActionDate.ToString("g"),
                Company = a.Company != null ? a.Company.CompanyName : "System",
                Module = a.TableName ?? "",
                Event = a.Action ?? "",
                Action = a.Action ?? "",
                Status = a.Action != null && a.Action.ToLower().Contains("delete") ? "Attention" : "Logged"
            })
            .ToListAsync(ct);
    }

    [HttpGet]
    public async Task<IActionResult> Companies(string? search, string? subscriptionStatus, bool? isActive, string sort = "created", string dir = "desc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var totalCompanies = await _db.Companies.CountAsync(c => !c.IsDeleted, ct);
        var activeCompanies = await _db.Companies.CountAsync(c => !c.IsDeleted && c.IsActive, ct);
        var inactiveCompanies = await _db.Companies.CountAsync(c => !c.IsDeleted && !c.IsActive, ct);

        var today = ViaReservaERP.AppTime.Now.Date;
        var expiringSubscriptions = await _db.Subscriptions.CountAsync(s =>
            s.EndDate != null &&
            s.EndDate.Value <= DateOnly.FromDateTime(today.AddDays(14)) &&
            s.EndDate.Value >= DateOnly.FromDateTime(today) &&
            s.Status != null && s.Status.ToLower() == "active", ct);

        var q = _db.Companies.AsNoTracking().Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c =>
                (c.CompanyName != null && c.CompanyName.Contains(s)) ||
                (c.Email != null && c.Email.Contains(s)) ||
                (c.Phone != null && c.Phone.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(subscriptionStatus))
        {
            var sub = subscriptionStatus.Trim();
            q = q.Where(c => c.SubscriptionStatus != null && c.SubscriptionStatus == sub);
        }

        if (isActive.HasValue)
            q = q.Where(c => c.IsActive == isActive.Value);

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        q = sort?.ToLowerInvariant() switch
        {
            "name" => asc ? q.OrderBy(c => c.CompanyName) : q.OrderByDescending(c => c.CompanyName),
            "status" => asc ? q.OrderBy(c => c.IsActive) : q.OrderByDescending(c => c.IsActive),
            _ => asc ? q.OrderBy(c => c.CreatedAt) : q.OrderByDescending(c => c.CreatedAt)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var vm = new CompaniesManagementViewModel
        {
            TotalCompanies = totalCompanies,
            ActiveCompanies = activeCompanies,
            InactiveCompanies = inactiveCompanies,
            ExpiringSubscriptions = expiringSubscriptions,
            Search = search,
            SubscriptionStatus = subscriptionStatus,
            IsActive = isActive,
            Sort = sort,
            Dir = dir,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Rows = rows
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCompany([FromForm] CompanyCreateInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid company input.";
            return RedirectToAction(nameof(Companies));
        }

        var company = new Company
        {
            CompanyId = 0,
            CompanyName = input.CompanyName.Trim(),
            SubscriptionStatus = string.IsNullOrWhiteSpace(input.SubscriptionStatus) ? null : input.SubscriptionStatus.Trim(),
            Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            Address = string.IsNullOrWhiteSpace(input.Address) ? null : input.Address.Trim(),
            IsActive = input.IsActive,
            CreatedAt = ViaReservaERP.AppTime.Now,
            UpdatedAt = null,
            DeletedAt = null,
            IsDeleted = false,
            CreatedBy = CurrentUserId
        };

        _db.Companies.Add(company);
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Added successfully";
        return RedirectToAction(nameof(Companies));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCompany(int companyId, [FromForm] Company input, CancellationToken ct)
    {
        var existing = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId && !c.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Companies));

        existing.CompanyName = input.CompanyName;
        existing.Email = input.Email;
        existing.Phone = input.Phone;
        existing.Address = input.Address;
        existing.SubscriptionStatus = input.SubscriptionStatus;
        existing.IsActive = input.IsActive;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;

        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Edited successfully";
        return RedirectToAction(nameof(Companies), new { companyId = existing.CompanyId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCompanyActive(int companyId, CancellationToken ct)
    {
        var existing = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId && !c.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Companies));

        existing.IsActive = !existing.IsActive;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Companies));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SoftDeleteCompany(int companyId, CancellationToken ct)
    {
        var existing = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId && !c.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Companies));

        existing.IsDeleted = true;
        existing.DeletedAt = ViaReservaERP.AppTime.Now;
        existing.DeletedBy = CurrentUserId;
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Archived successfully";
        return RedirectToAction(nameof(Companies));
    }

    [HttpGet]
    public async Task<IActionResult> ArchivedCompanies(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var q = _db.Companies.AsNoTracking().Where(c => c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c =>
                (c.CompanyName != null && c.CompanyName.Contains(s)) ||
                (c.Email != null && c.Email.Contains(s)) ||
                (c.Phone != null && c.Phone.Contains(s)));
        }

        q = q.OrderByDescending(c => c.DeletedAt);

        if (page <= 1) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var vm = new ViaReservaERP.Models.SuperAdmin.CompaniesManagementViewModel
        {
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Rows = rows
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreCompany(int companyId, CancellationToken ct)
    {
        var existing = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(ArchivedCompanies));

        existing.IsDeleted = false;
        existing.DeletedAt = null;
        existing.DeletedBy = null;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;

        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Restored successfully";
        return RedirectToAction(nameof(ArchivedCompanies));
    }

    [HttpGet]
    public async Task<IActionResult> ExportCompanies(string format = "csv", CancellationToken ct = default)
    {
        format = (format ?? "csv").Trim().ToLowerInvariant();
        var rows = await _db.Companies.AsNoTracking().Where(c => !c.IsDeleted).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

        var headers = new[] { "CompanyName", "Email", "Phone", "Address", "SubscriptionStatus", "IsActive", "CreatedAt" };
        var data = rows.Select(c => new[]
        {
            c.CompanyName,
            c.Email ?? "",
            c.Phone ?? "",
            c.Address ?? "",
            c.SubscriptionStatus ?? "",
            c.IsActive ? "Active" : "Inactive",
            FormatPhilippinesTime(c.CreatedAt)
        });

        var kpis = new Dictionary<string, string>
        {
            ["Total Companies"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Active"] = rows.Count(x => x.IsActive).ToString(CultureInfo.InvariantCulture),
            ["Inactive"] = rows.Count(x => !x.IsActive).ToString(CultureInfo.InvariantCulture)
        };

        return format switch
        {
            "xlsx" => ExportExcel("Companies", headers, data),
            "pdf" => ExportPdf("Companies", headers, data, kpis),
            _ => ExportCsv("Companies", headers, data)
        };
    }

    [HttpGet]
    public async Task<IActionResult> ExportRolesPermissions(string format = "pdf", int? roleId = null, CancellationToken ct = default)
    {
        format = (format ?? "pdf").Trim().ToLowerInvariant();

        var selectedRole = roleId.HasValue
            ? await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId.Value, ct)
            : null;

        var rolePermIds = roleId.HasValue
            ? await _db.RolePermissions.AsNoTracking().Where(rp => rp.RoleId == roleId.Value).Select(rp => rp.PermissionId).ToListAsync(ct)
            : new List<int>();

        var enabledSet = new HashSet<int>(rolePermIds);

        var perms = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.PermissionName)
            .ToListAsync(ct);

        var headers = new[] { "Role", "Permission", "Enabled" };
        var data = perms.Select(p => new[]
        {
            selectedRole?.RoleName ?? (roleId.HasValue ? roleId.Value.ToString(CultureInfo.InvariantCulture) : "All Roles"),
            p.PermissionName ?? "",
            enabledSet.Contains(p.PermissionId) ? "Enabled" : "Disabled"
        });

        var kpis = new Dictionary<string, string>
        {
            ["Role"] = selectedRole?.RoleName ?? "—",
            ["Total Permissions"] = perms.Count.ToString(CultureInfo.InvariantCulture),
            ["Enabled"] = enabledSet.Count.ToString(CultureInfo.InvariantCulture)
        };

        return format switch
        {
            "csv" => ExportCsv("Roles-Permissions", headers, data),
            "xlsx" => ExportExcel("Roles-Permissions", headers, data),
            "pdf" => ExportPdf("Roles-Permissions", headers, data, kpis),
            _ => ExportPdf("Roles-Permissions", headers, data, kpis)
        };
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search, int? companyId, int? roleId, string sort = "created", string dir = "desc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var now = ViaReservaERP.AppTime.Now;
        var totalUsers = await _db.Users.CountAsync(u => !u.IsDeleted, ct);
        var activeUsers = await _db.Users.CountAsync(u => !u.IsDeleted && u.IsActive, ct);
        var suspendedUsers = await _db.Users.CountAsync(u => !u.IsDeleted && !u.IsActive, ct);
        var recentlyCreatedUsers = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= now.AddDays(-7), ct);

        var companies = await _db.Companies.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.CompanyName).ToListAsync(ct);
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.RoleName).ToListAsync(ct);

        var q = _db.Users
            .AsNoTracking()
            .Include(u => u.Company)
            .Include(u => u.Role)
            .Where(u => !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u =>
                (u.FullName != null && u.FullName.Contains(s)) ||
                (u.Email != null && u.Email.Contains(s)));
        }

        if (companyId.HasValue)
            q = q.Where(u => u.CompanyId == companyId.Value);
        if (roleId.HasValue)
            q = q.Where(u => u.RoleId == roleId.Value);

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        q = sort?.ToLowerInvariant() switch
        {
            "name" => asc ? q.OrderBy(u => u.FullName) : q.OrderByDescending(u => u.FullName),
            "email" => asc ? q.OrderBy(u => u.Email) : q.OrderByDescending(u => u.Email),
            _ => asc ? q.OrderBy(u => u.CreatedAt) : q.OrderByDescending(u => u.CreatedAt)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var vm = new UsersManagementViewModel
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            SuspendedUsers = suspendedUsers,
            RecentlyCreatedUsers = recentlyCreatedUsers,
            Search = search,
            CompanyId = companyId,
            RoleId = roleId,
            Sort = sort,
            Dir = dir,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Rows = rows,
            Companies = companies,
            Roles = roles
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser([FromForm] CreateOrEditUserInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid user input.";
            return RedirectToAction(nameof(Users));
        }

        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Trim().Length < 6)
        {
            TempData["ErrorMessage"] = "Password must be at least 6 characters long.";
            return RedirectToAction(nameof(Users));
        }

        var email = (input.Email ?? string.Empty).Trim().ToLowerInvariant();
        var emailExists = await _db.Users.AsNoTracking().AnyAsync(u => !u.IsDeleted && u.Email == email, ct);
        if (emailExists)
        {
            TempData["ErrorMessage"] = "Email is already registered.";
            return RedirectToAction(nameof(Users));
        }

        var companyExists = await _db.Companies.AsNoTracking().AnyAsync(c => !c.IsDeleted && c.CompanyId == input.CompanyId, ct);
        if (!companyExists)
        {
            TempData["ErrorMessage"] = "Selected company is invalid.";
            return RedirectToAction(nameof(Users));
        }

        var roleExists = await _db.Roles.AsNoTracking().AnyAsync(r => r.RoleId == input.RoleId, ct);
        if (!roleExists)
        {
            TempData["ErrorMessage"] = "Selected role is invalid.";
            return RedirectToAction(nameof(Users));
        }

        var user = new ErpUser
        {
            CompanyId = input.CompanyId,
            RoleId = input.RoleId,
            FullName = (input.FullName ?? string.Empty).Trim(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(input.Password),
            IsActive = input.IsActive,
            CreatedAt = ViaReservaERP.AppTime.Now,
            CreatedBy = CurrentUserId,
            IsDeleted = false
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // If user is a Guest (Role 6), auto-create a Guest profile so they can access the Guest Dashboard
        if (user.RoleId == 6)
        {
            var guestProfile = new Guest
            {
                CompanyId = user.CompanyId,
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                CreatedAt = ViaReservaERP.AppTime.Now,
                IsActive = true,
                IsDeleted = false
            };
            _db.Guests.Add(guestProfile);
            await _db.SaveChangesAsync(ct);
        }

        TempData["SuccessMessage"] = "Added successfully";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser([FromForm] CreateOrEditUserInput input, CancellationToken ct)
    {
        if (!input.UserId.HasValue) return RedirectToAction(nameof(Users));

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.UserId == input.UserId.Value && !u.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Users));

        existing.CompanyId = input.CompanyId;
        existing.RoleId = input.RoleId;
        existing.FullName = input.FullName;
        existing.Email = input.Email;
        existing.IsActive = input.IsActive;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;

        if (!string.IsNullOrWhiteSpace(input.Password))
            existing.PasswordHash = PasswordHasher.Hash(input.Password);

        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Edited successfully";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserActive(int userId, CancellationToken ct)
    {
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Users));

        existing.IsActive = !existing.IsActive;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUserPassword(int userId, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) return RedirectToAction(nameof(Users));

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Users));

        existing.PasswordHash = PasswordHasher.Hash(newPassword);
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;
        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SoftDeleteUser(int userId, CancellationToken ct)
    {
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(Users));

        existing.IsDeleted = true;
        existing.DeletedAt = ViaReservaERP.AppTime.Now;
        existing.DeletedBy = CurrentUserId;
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Archived successfully";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> ArchivedUsers(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var q = _db.Users.AsNoTracking().Include(u => u.Company).Include(u => u.Role).Where(u => u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u =>
                (u.FullName != null && u.FullName.Contains(s)) ||
                (u.Email != null && u.Email.Contains(s)));
        }

        q = q.OrderByDescending(u => u.DeletedAt);

        if (page <= 1) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var vm = new ViaReservaERP.Models.SuperAdmin.UsersManagementViewModel
        {
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Rows = rows
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreUser(int userId, CancellationToken ct)
    {
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsDeleted, ct);
        if (existing == null) return RedirectToAction(nameof(ArchivedUsers));

        existing.IsDeleted = false;
        existing.DeletedAt = null;
        existing.DeletedBy = null;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = CurrentUserId;

        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Restored successfully";
        return RedirectToAction(nameof(ArchivedUsers));
    }

    [HttpGet]
    public async Task<IActionResult> EnterpriseInquiries(string? search, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var q = _db.EnterpriseInquiries
            .AsNoTracking()
            .Where(i => !i.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i =>
                (i.CompanyName != null && i.CompanyName.Contains(s)) ||
                (i.ContactPerson != null && i.ContactPerson.Contains(s)) ||
                (i.Email != null && i.Email.Contains(s)) ||
                (i.Phone != null && i.Phone.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            q = q.Where(i => i.Status != null && i.Status == st);
        }

        var totalInquiries = await _db.EnterpriseInquiries.AsNoTracking().CountAsync(i => !i.IsDeleted, ct);
        var newInquiries = await _db.EnterpriseInquiries.AsNoTracking().CountAsync(i => !i.IsDeleted && i.Status == "New", ct);
        var contactedInquiries = await _db.EnterpriseInquiries.AsNoTracking().CountAsync(i => !i.IsDeleted && i.Status == "Contacted", ct);
        var closedInquiries = await _db.EnterpriseInquiries.AsNoTracking().CountAsync(i => !i.IsDeleted && i.Status == "Closed", ct);

        var totalRows = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return View(new EnterpriseInquiriesViewModel
        {
            Search = search,
            Status = status,

            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,

            TotalInquiries = totalInquiries,
            NewInquiries = newInquiries,
            ContactedInquiries = contactedInquiries,
            ClosedInquiries = closedInquiries,

            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExportUsers(string format = "csv", CancellationToken ct = default)
    {
        format = (format ?? "csv").Trim().ToLowerInvariant();

        var rows = await _db.Users
            .AsNoTracking()
            .Include(u => u.Company)
            .Include(u => u.Role)
            .Where(u => !u.IsDeleted)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

        var headers = new[] { "FullName", "Email", "Company", "Role", "IsActive", "CreatedAt" };
        var data = rows.Select(u => new[]
        {
            u.FullName,
            u.Email,
            u.Company != null ? u.Company.CompanyName : "",
            u.Role != null ? u.Role.RoleName : "",
            u.IsActive ? "Active" : "Suspended",
            FormatPhilippinesTime(u.CreatedAt)
        });

        var kpis = new Dictionary<string, string>
        {
            ["Total Users"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Active"] = rows.Count(x => x.IsActive).ToString(CultureInfo.InvariantCulture),
            ["Suspended"] = rows.Count(x => !x.IsActive).ToString(CultureInfo.InvariantCulture)
        };

        return format switch
        {
            "xlsx" => ExportExcel("Users", headers, data),
            "pdf" => ExportPdf("Users", headers, data, kpis),
            _ => ExportCsv("Users", headers, data)
        };
    }

    private IActionResult ExportPdf(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyDictionary<string, string> kpis)
    {
        var logoPath = Path.Combine(_env.WebRootPath, "img", "viareserva-logo.png");
        byte[]? logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;

        var created = ViaReservaERP.AppTime.Now;
        var rowList = rows.Take(2000).ToList();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Row(left =>
                    {
                        if (logoBytes != null)
                            left.ConstantItem(54).Height(24).Image(logoBytes);

                        left.RelativeItem().Column(col =>
                        {
                            col.Item().Text("ViaReservaERP").FontSize(12).FontColor("#1a2a6c").SemiBold();
                            col.Item().Text("Hospitality Enterprise Platform").FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    row.ConstantItem(200).AlignRight().Column(col =>
                    {
                        col.Item().Text(title).FontSize(14).FontColor("#1e293b").SemiBold();
                        col.Item().Text($"Generated: {created:yyyy-MM-dd HH:mm} UTC").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    if (kpis.Count > 0)
                    {
                        col.Item().Row(r =>
                        {
                            foreach (var kv in kpis)
                            {
                                r.RelativeItem().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(10).Column(c =>
                                {
                                    c.Item().Text(kv.Key).FontSize(9).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(kv.Value).FontSize(12).FontColor("#1e293b").SemiBold();
                                });
                            }
                        });
                    }

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            for (var i = 0; i < headers.Count; i++)
                                columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            for (var i = 0; i < headers.Count; i++)
                            {
                                header.Cell().Background("#f1f5f9").BorderBottom(1).BorderColor("#e2e8f0")
                                    .PaddingVertical(6).PaddingHorizontal(6)
                                    .Text(headers[i]).FontColor("#1e293b").SemiBold().FontSize(9);
                            }
                        });

                        foreach (var r in rowList)
                        {
                            for (var i = 0; i < headers.Count; i++)
                            {
                                var val = i < r.Length ? r[i] : "";
                                table.Cell().BorderBottom(1).BorderColor("#f1f5f9").PaddingVertical(6).PaddingHorizontal(6)
                                    .Text(val).FontSize(9);
                            }
                        }
                    });

                    col.Item().Text($"Rows: {rowList.Count:N0}").FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Confidential · ViaReservaERP").FontSize(8).FontColor(Colors.Grey.Darken2);
                });
            });
        });

        var pdfBytes = doc.GeneratePdf();
        return File(pdfBytes, "application/pdf", FileName(title, "pdf"));
    }

    [HttpGet]
    public async Task<IActionResult> Roles(int? roleId, string? permissionSearch, CancellationToken ct)
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderByDescending(r => r.RoleId)
            .ToListAsync(ct);

        var permissionsQuery = _db.Permissions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(permissionSearch))
        {
            var s = permissionSearch.Trim();
            permissionsQuery = permissionsQuery.Where(p => p.PermissionName != null && p.PermissionName.Contains(s));
        }

        permissionsQuery = permissionsQuery.OrderByDescending(p => p.PermissionId);

        var permissions = await permissionsQuery.ToListAsync(ct);

        var totalRoles = roles.Count;
        var totalPermissions = await _db.Permissions.CountAsync(ct);
        var customRolesCreated = roles.Count(r => !string.Equals(r.RoleName, RoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase) &&
                                                  !string.Equals(r.RoleName, RoleNames.CompanyAdmin, StringComparison.OrdinalIgnoreCase) &&
                                                  !string.Equals(r.RoleName, RoleNames.Accountant, StringComparison.OrdinalIgnoreCase) &&
                                                  !string.Equals(r.RoleName, RoleNames.FrontDesk, StringComparison.OrdinalIgnoreCase) &&
                                                  !string.Equals(r.RoleName, RoleNames.ServiceStaff, StringComparison.OrdinalIgnoreCase) &&
                                                  !string.Equals(r.RoleName, RoleNames.Guest, StringComparison.OrdinalIgnoreCase));

        var selectedRoleId = roleId ?? roles.FirstOrDefault()?.RoleId;
        var selectedPermissionIds = new HashSet<int>();
        if (selectedRoleId.HasValue)
        {
            var permissionIds = await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == selectedRoleId.Value)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);
            selectedPermissionIds = new HashSet<int>(permissionIds);
        }

        var vm = new RolesPermissionsViewModel
        {
            TotalRoles = totalRoles,
            TotalPermissions = totalPermissions,
            CustomRolesCreated = customRolesCreated,
            SelectedRoleId = selectedRoleId,
            PermissionSearch = permissionSearch,
            Roles = roles,
            Permissions = permissions,
            SelectedRolePermissionIds = selectedPermissionIds
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(string roleName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return RedirectToAction(nameof(Roles));

        var name = roleName.Trim();
        var exists = await _db.Roles.AnyAsync(r => r.RoleName == name, ct);
        if (!exists)
        {
            _db.Roles.Add(new Role { RoleName = name });
            await _db.SaveChangesAsync(ct);
        }

        return RedirectToAction(nameof(Roles));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameRole(int roleId, string roleName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return RedirectToAction(nameof(Roles), new { roleId });

        var existing = await _db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct);
        if (existing == null) return RedirectToAction(nameof(Roles));

        existing.RoleName = roleName.Trim();
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Roles), new { roleId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(int roleId, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct);
        if (role == null) return RedirectToAction(nameof(Roles));

        var hasUsers = await _db.Users.AnyAsync(u => !u.IsDeleted && u.RoleId == roleId, ct);
        if (!hasUsers)
        {
            var links = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
            _db.RolePermissions.RemoveRange(links);
            _db.Roles.Remove(role);
            await _db.SaveChangesAsync(ct);
        }

        return RedirectToAction(nameof(Roles));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRolePermission(int roleId, int permissionId, bool enabled, string? permissionSearch, CancellationToken ct)
    {
        var exists = await _db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, ct);
        if (enabled && !exists)
        {
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
            await _db.SaveChangesAsync(ct);
        }
        else if (!enabled && exists)
        {
            var link = await _db.RolePermissions.FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, ct);
            if (link != null)
            {
                _db.RolePermissions.Remove(link);
                await _db.SaveChangesAsync(ct);
            }
        }

        return RedirectToAction(nameof(Roles), new { roleId, permissionSearch });
    }

    [HttpGet]
    public async Task<IActionResult> Reservations(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? companyId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        DateOnly start;
        DateOnly end;
        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            if (granularity == "day")
            {
                start = today;
                end = today;
            }
            else if (granularity == "year")
            {
                start = new DateOnly(today.Year, 1, 1);
                end = today;
            }
            else
            {
                start = new DateOnly(today.Year, today.Month, 1);
                end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }
        }

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyOptionRow { CompanyId = c.CompanyId, CompanyName = c.CompanyName })
            .ToListAsync(ct);

        var companyName = "All Companies";
        if (companyId.HasValue)
            companyName = companies.FirstOrDefault(c => c.CompanyId == companyId.Value)?.CompanyName ?? "Selected Company";

        var reservationsQ = _db.Reservations.AsNoTracking();
        if (companyId.HasValue)
            reservationsQ = reservationsQ.Where(r => r.CompanyId == companyId.Value);

        var rangeReservationsQ = reservationsQ
            .Where(r => r.CheckInDate.HasValue)
            .Where(r => r.CheckInDate!.Value >= start && r.CheckInDate!.Value <= end);

        var totalReservations = await rangeReservationsQ.CountAsync(ct);
        var cancelledReservations = await rangeReservationsQ.CountAsync(r => r.Status != null && r.Status.ToLower() == "cancelled", ct);
        var completedReservations = await rangeReservationsQ.CountAsync(r => r.Status != null && (r.Status.ToLower() == "checkedout" || r.Status.ToLower() == "completed"), ct);
        var activeBookings = await rangeReservationsQ.CountAsync(r =>
            r.Status == null || (r.Status.ToLower() != "cancelled" && r.Status.ToLower() != "checkedout" && r.Status.ToLower() != "completed"), ct);

        var checkedInGuests = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CheckInDate.HasValue && r.CheckOutDate.HasValue)
            .Where(r => r.CheckInDate!.Value <= today && r.CheckOutDate!.Value >= today)
            .Where(r => r.Status != null && r.Status.ToLower() != "cancelled")
            .Where(r => !companyId.HasValue || r.CompanyId == companyId.Value)
            .CountAsync(ct);

        var reservationRevenue = await _db.Payments
            .AsNoTracking()
            .Where(p => p.ReservationId != null && p.Amount != null)
            .Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
            .SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;

        var paymentStatusByReservation = await _db.Payments
            .AsNoTracking()
            .Where(p => p.ReservationId != null)
            .Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
            .GroupBy(p => p.ReservationId!.Value)
            .Select(g => new { ReservationId = g.Key, Status = g.OrderByDescending(x => x.CreatedAt).Select(x => x.Status).FirstOrDefault() })
            .ToDictionaryAsync(x => x.ReservationId, x => x.Status ?? "", ct);

        var roomTypeByReservation = await _db.ReservationRooms
            .AsNoTracking()
            .Where(rr => rr.ReservationId != null && rr.RoomId != null)
            .Join(_db.Rooms.AsNoTracking(), rr => rr.RoomId, r => r.RoomId, (rr, r) => new { rr.ReservationId, r.RoomTypeId })
            .Join(_db.RoomTypes.AsNoTracking(), x => x.RoomTypeId, rt => rt.RoomTypeId, (x, rt) => new { ReservationId = x.ReservationId!.Value, RoomType = rt.TypeName ?? "" })
            .Where(x => !companyId.HasValue || _db.Reservations.Any(r => r.ReservationId == x.ReservationId && r.CompanyId == companyId.Value))
            .GroupBy(x => x.ReservationId)
            .Select(g => new { ReservationId = g.Key, RoomType = g.Select(x => x.RoomType).FirstOrDefault() })
            .ToDictionaryAsync(x => x.ReservationId, x => x.RoomType ?? "", ct);

        var reservationRows = await rangeReservationsQ
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .OrderByDescending(r => r.ReservationId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReservationRow
            {
                ReservationId = r.ReservationId,
                GuestName = r.Guest != null ? (r.Guest.FullName ?? "") : "",
                HotelCompany = r.Company != null ? (r.Company.CompanyName ?? "") : "",
                RoomType = "",
                CheckIn = r.CheckInDate,
                CheckOut = r.CheckOutDate,
                PaymentStatus = "",
                ReservationStatus = r.Status ?? "",
                TotalAmount = r.TotalAmount ?? 0m
            })
            .ToListAsync(ct);

        foreach (var row in reservationRows)
        {
            row.PaymentStatus = paymentStatusByReservation.TryGetValue(row.ReservationId, out var ps) ? ps : "";
            row.RoomType = roomTypeByReservation.TryGetValue(row.ReservationId, out var rt) ? rt : "";
        }

        var workflow = new ReservationWorkflowSummary
        {
            BookingCreated = totalReservations,
            PaymentProcessed = await _db.Payments
                .AsNoTracking()
                .Where(p => p.ReservationId != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
                .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
                .Where(p => p.Status != null && p.Status.ToLower() == "succeeded")
                .Select(p => p.ReservationId!.Value)
                .Distinct()
                .CountAsync(ct),
            ReservationConfirmed = await rangeReservationsQ.CountAsync(r => r.Status != null && r.Status.ToLower() == "confirmed", ct),
            CheckIn = await rangeReservationsQ.CountAsync(r => r.Status != null && r.Status.ToLower() == "checkedin", ct),
            CheckOut = await rangeReservationsQ.CountAsync(r => r.Status != null && (r.Status.ToLower() == "checkedout" || r.Status.ToLower() == "completed"), ct),
            AccountingRecorded = await _db.Transactions
                .AsNoTracking()
                .Where(t => t.ReferenceId != null && t.ReferenceType != null)
                .Where(t => t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl)
                .Where(t => t.ReferenceType!.ToLower() == "reservation")
                .Where(t => !companyId.HasValue || t.CompanyId == companyId.Value)
                .Select(t => t.ReferenceId!.Value)
                .Distinct()
                .CountAsync(ct)
        };

        var overbookingCandidates = await rangeReservationsQ
            .Where(r => r.Status != null && (r.Status.ToLower() == "confirmed" || r.Status.ToLower() == "pending"))
            .Select(r => r.ReservationId)
            .ToListAsync(ct);

        var assignedReservationIds = await _db.ReservationRooms
            .AsNoTracking()
            .Where(rr => rr.ReservationId != null)
            .Select(rr => rr.ReservationId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var unassignedCount = overbookingCandidates.Count(id => !assignedReservationIds.Contains(id));

        var failedPayments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.ReservationId != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
            .Where(p => p.Status != null && p.Status.ToLower() != "succeeded")
            .CountAsync(ct);

        var refundRequests = await _db.Payments
            .AsNoTracking()
            .Where(p => p.ReservationId != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
            .Where(p => p.Status != null && p.Status.ToLower().Contains("refund"))
            .CountAsync(ct);

        var reservationConflicts = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CheckInDate == null || r.CheckOutDate == null)
            .Where(r => !companyId.HasValue || r.CompanyId == companyId.Value)
            .CountAsync(ct);

        var guestComplaints = await _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl)
            .Where(sr => sr.Status != null && sr.Status.ToLower() == "pending")
            .Where(sr => !companyId.HasValue || sr.CompanyId == companyId.Value)
            .CountAsync(ct);

        var model = new ReservationMonitoringViewModel
        {
            Granularity = granularity,
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            CompanyId = companyId,
            CompanyName = companyName,
            Companies = companies,
            TotalReservations = totalReservations,
            ActiveBookings = activeBookings,
            CheckedInGuests = checkedInGuests,
            CompletedReservations = completedReservations,
            CancelledReservations = cancelledReservations,
            ReservationRevenue = reservationRevenue,
            Reservations = reservationRows,
            Workflow = workflow,
            Alerts = new ReservationAlertsSummary
            {
                Overbookings = unassignedCount,
                FailedPayments = failedPayments,
                RefundRequests = refundRequests,
                ReservationConflicts = reservationConflicts,
                GuestComplaints = guestComplaints
            },
            Page = page,
            PageSize = pageSize,
            TotalRows = totalReservations
        };

        await PopulateReservationMonitoringChartsAsync(model, ct);
        return View(model);
    }

    private async Task PopulateReservationMonitoringChartsAsync(ReservationMonitoringViewModel model, CancellationToken ct)
    {
        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var granularity = (model.Granularity ?? "month").Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        var start = model.StartDate;
        var end = model.EndDate;

        var labels = new List<string>();
        var bucketStarts = new List<DateOnly>();
        var bucketEndsExclusive = new List<DateOnly>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                bucketStarts.Add(d);
                bucketEndsExclusive.Add(d.AddDays(1));
            }
        }
        else if (granularity == "year")
        {
            for (var y = start.Year; y <= end.Year; y++)
            {
                var s = new DateOnly(y, 1, 1);
                var e = new DateOnly(y + 1, 1, 1);
                labels.Add(y.ToString(CultureInfo.InvariantCulture));
                bucketStarts.Add(s);
                bucketEndsExclusive.Add(e);
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                labels.Add(cursor.ToString("MMM", CultureInfo.InvariantCulture));
                bucketStarts.Add(cursor);
                bucketEndsExclusive.Add(cursor.AddMonths(1));
                cursor = cursor.AddMonths(1);
            }
        }

        model.ReservationVolumeAnalytics.Labels = labels;
        model.RevenueTrendAnalytics.Labels = labels.ToList();
        model.CancellationTrendAnalytics.Labels = labels.ToList();

        var bookings = new List<decimal>();
        var cancellations = new List<decimal>();
        var revenue = new List<decimal>();

        for (var i = 0; i < bucketStarts.Count; i++)
        {
            var sDate = bucketStarts[i];
            var eDate = bucketEndsExclusive[i];

            if (sDate < start) sDate = start;
            if (eDate > end.AddDays(1)) eDate = end.AddDays(1);

            var sUtc = AsUtcStart(sDate);
            var eUtc = AsUtcStart(eDate);

            var resQ = _db.Reservations.AsNoTracking()
                .Where(r => r.CheckInDate.HasValue)
                .Where(r => r.CheckInDate!.Value >= sDate && r.CheckInDate!.Value < eDate);

            if (model.CompanyId.HasValue)
                resQ = resQ.Where(r => r.CompanyId == model.CompanyId.Value);

            bookings.Add(await resQ.CountAsync(ct));
            cancellations.Add(await resQ.CountAsync(r => r.Status != null && r.Status.ToLower() == "cancelled", ct));

            var payQ = _db.Payments.AsNoTracking()
                .Where(p => p.ReservationId != null && p.Amount != null)
                .Where(p => p.CreatedAt >= sUtc && p.CreatedAt < eUtc);
            if (model.CompanyId.HasValue)
                payQ = payQ.Where(p => p.CompanyId == model.CompanyId.Value);
            revenue.Add(await payQ.SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m);
        }

        model.ReservationVolumeAnalytics.Datasets["Daily/Monthly/Yearly"] = bookings;
        model.CancellationTrendAnalytics.Datasets["Cancellations"] = cancellations;
        model.RevenueTrendAnalytics.Datasets["Reservation Revenue"] = revenue;
    }

    [HttpGet]
    public async Task<IActionResult> ReservationDetails(int id, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == id, ct);

        if (reservation == null)
            return NotFound();

        var roomRows = await _db.ReservationRooms
            .AsNoTracking()
            .Where(rr => rr.ReservationId == id && rr.RoomId != null)
            .Join(_db.Rooms.AsNoTracking(), rr => rr.RoomId, room => room.RoomId, (rr, room) => new { rr, room })
            .Join(_db.RoomTypes.AsNoTracking(), x => x.room.RoomTypeId, rt => rt.RoomTypeId, (x, rt) => new RoomDetailRow
            {
                RoomNumber = x.room.RoomNumber ?? "",
                RoomType = rt.TypeName ?? "",
                Price = x.rr.Price ?? 0m
            })
            .ToListAsync(ct);

        var specialRequests = await _db.ReservationServices
            .AsNoTracking()
            .Where(rs => rs.ReservationId == id && rs.ServiceId != null)
            .Join(_db.Services.AsNoTracking(), rs => rs.ServiceId, s => s.ServiceId, (rs, s) => new SpecialRequestRow
            {
                Item = s.ServiceName ?? "",
                Quantity = rs.Quantity ?? 0,
                Price = rs.Price ?? (s.Price ?? 0m)
            })
            .OrderByDescending(x => x.Quantity)
            .ToListAsync(ct);

        var timelineQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Include(a => a.Company)
            .Where(a => (a.TableName != null && a.TableName.ToLower() == "reservations" && a.RecordId == id) || (a.RecordId == id && a.TableName != null && a.TableName.ToLower().Contains("reservation")));

        var totalRows = await timelineQ.CountAsync(ct);
        var timeline = await timelineQ
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new BookingTimelineRow
            {
                TimestampUtc = a.ActionDate,
                Action = (a.TableName ?? "") + " · " + (a.Action ?? ""),
                Actor = a.User != null ? a.User.FullName : "",
                ActorRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : ""
            })
            .ToListAsync(ct);

        var bookingDate = timeline.Count > 0 ? timeline.Min(t => t.TimestampUtc) : (DateTime?)null;

        var assignedStaffSummary = await _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.GuestId == reservation.GuestId)
            .Where(sr => sr.AssignedTo != null)
            .Join(_db.Users.AsNoTracking(), sr => sr.AssignedTo, u => u.UserId, (sr, u) => u.FullName)
            .Distinct()
            .Take(6)
            .ToListAsync(ct);

        var model = new ReservationDetailsViewModel
        {
            ReservationId = reservation.ReservationId,
            HotelCompany = reservation.Company != null ? (reservation.Company.CompanyName ?? "") : "",
            GuestName = reservation.Guest != null ? (reservation.Guest.FullName ?? "") : "",
            GuestEmail = reservation.Guest != null ? (reservation.Guest.Email ?? "") : "",
            GuestPhone = reservation.Guest != null ? (reservation.Guest.Phone ?? "") : "",
            CheckInDate = reservation.CheckInDate,
            CheckOutDate = reservation.CheckOutDate,
            BookingDateUtc = bookingDate,
            ReservationStatus = reservation.Status ?? "",
            TotalAmount = reservation.TotalAmount ?? 0m,
            Rooms = roomRows,
            SpecialRequests = specialRequests,
            AssignedStaffSummary = string.Join(", ", assignedStaffSummary),
            BookingTimeline = timeline,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ReservationPayment(int reservationId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var payment = await _db.Payments
            .AsNoTracking()
            .Where(p => p.ReservationId == reservationId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (payment == null)
            return View(new PaymentDetailsViewModel { PaymentId = 0, ReservationId = reservationId, BillingDateUtc = ViaReservaERP.AppTime.Now });

        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, ct);

        var refundQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Where(a => a.TableName != null && a.TableName.ToLower() == "payments" && a.RecordId == payment.PaymentId)
            .Where(a => a.Action != null && a.Action.ToLower().Contains("refund"));

        var totalRows = await refundQ.CountAsync(ct);
        var refundHistory = await refundQ
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new RefundHistoryRow
            {
                TimestampUtc = a.ActionDate,
                Action = a.Action ?? "",
                Actor = a.User != null ? a.User.FullName : "",
                ActorRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : ""
            })
            .ToListAsync(ct);

        var model = new PaymentDetailsViewModel
        {
            PaymentId = payment.PaymentId,
            ReservationId = payment.ReservationId,
            GuestName = reservation != null && reservation.Guest != null ? (reservation.Guest.FullName ?? "") : "",
            HotelCompany = reservation != null && reservation.Company != null ? (reservation.Company.CompanyName ?? "") : "",
            PaymentMethod = payment.PaymentMethod ?? "",
            StripeReference = payment.StripePaymentIntentId ?? "",
            PaymentStatus = payment.Status ?? "",
            AmountPaid = payment.Amount ?? 0m,
            BillingDateUtc = payment.CreatedAt,
            RefundHistory = refundHistory,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ReservationServiceRequests(int reservationId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, ct);

        if (reservation == null)
            return NotFound();

        var q = _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Service)
            .Include(sr => sr.AssignedToUser)
            .Where(sr => sr.GuestId == reservation.GuestId)
            .Where(sr => sr.CompanyId == reservation.CompanyId);

        var totalRows = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(sr => sr.RequestDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sr => new ServiceRequestRow
            {
                RequestId = sr.RequestId,
                RequestedService = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                AssignedServiceStaff = sr.AssignedToUser != null ? sr.AssignedToUser.FullName : "",
                Status = sr.Status ?? "",
                RequestedDateUtc = sr.RequestDate,
                CompletionDateUtc = null,
                Notes = ""
            })
            .ToListAsync(ct);

        var model = new ReservationServiceRequestsViewModel
        {
            ReservationId = reservationId,
            GuestName = reservation.Guest != null ? (reservation.Guest.FullName ?? "") : "",
            HotelCompany = reservation.Company != null ? (reservation.Company.CompanyName ?? "") : "",
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ReservationAuditLogs(int reservationId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Company)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, ct);

        if (reservation == null)
            return NotFound();

        var q = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Include(a => a.Company)
            .Where(a => a.RecordId == reservationId);

        var totalRows = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogRow
            {
                TimestampUtc = a.ActionDate,
                Action = a.Action ?? "",
                UserName = a.User != null ? a.User.FullName : "",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Table = a.TableName ?? ""
            })
            .ToListAsync(ct);

        var model = new ReservationAuditLogsViewModel
        {
            ReservationId = reservationId,
            HotelCompany = reservation.Company != null ? (reservation.Company.CompanyName ?? "") : "",
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GuestServices(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? companyId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        DateOnly start;
        DateOnly end;
        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            if (granularity == "day")
            {
                start = today;
                end = today;
            }
            else if (granularity == "year")
            {
                start = new DateOnly(today.Year, 1, 1);
                end = today;
            }
            else
            {
                start = new DateOnly(today.Year, today.Month, 1);
                end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }
        }

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyOptionRow { CompanyId = c.CompanyId, CompanyName = c.CompanyName })
            .ToListAsync(ct);

        var companyName = "All Companies";
        if (companyId.HasValue)
            companyName = companies.FirstOrDefault(c => c.CompanyId == companyId.Value)?.CompanyName ?? "Selected Company";

        var requestsQ = _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Company)
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .Include(sr => sr.AssignedToUser)
            .Where(sr => sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl);

        if (companyId.HasValue)
            requestsQ = requestsQ.Where(sr => sr.CompanyId == companyId.Value);

        var total = await requestsQ.CountAsync(ct);
        var pending = await requestsQ.CountAsync(sr => sr.Status != null && sr.Status.ToLower() == "pending", ct);
        var completed = await requestsQ.CountAsync(sr => sr.Status != null && sr.Status.ToLower() == "completed", ct);
        var inProgress = await requestsQ.CountAsync(sr => sr.Status != null && (sr.Status.ToLower() == "inprogress" || sr.Status.ToLower() == "in progress"), ct);
        var escalated = await requestsQ.CountAsync(sr => sr.Status != null && sr.Status.ToLower().Contains("escal"), ct);

        var nowUtc = ViaReservaERP.AppTime.Now;
        var avgResponseMinutes = await _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl)
            .Where(sr => !companyId.HasValue || sr.CompanyId == companyId.Value)
            .Select(sr => (double?)EF.Functions.DateDiffMinute(sr.RequestDate, nowUtc))
            .AverageAsync(ct) ?? 0d;
        var avgResponseHours = avgResponseMinutes / 60d;

        var requestRowsRaw = await requestsQ
            .OrderByDescending(sr => sr.RequestDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sr => new
            {
                sr.RequestId,
                GuestName = sr.Guest != null ? (sr.Guest.FullName ?? "") : "",
                CompanyName = sr.Company != null ? (sr.Company.CompanyName ?? "") : "",
                GuestId = sr.GuestId,
                ServiceType = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                AssignedStaff = sr.AssignedToUser != null ? sr.AssignedToUser.FullName : "",
                AssignedTo = sr.AssignedTo,
                sr.RequestDate,
                Status = sr.Status ?? ""
            })
            .ToListAsync(ct);

        var guestIds = requestRowsRaw.Where(x => x.GuestId.HasValue).Select(x => x.GuestId!.Value).Distinct().ToList();
        var resByGuest = await _db.Reservations
            .AsNoTracking()
            .Where(r => guestIds.Contains(r.GuestId))
            .OrderByDescending(r => r.ReservationId)
            .Select(r => new { r.GuestId, r.ReservationId })
            .ToListAsync(ct);
        var latestReservationByGuest = resByGuest
            .GroupBy(x => x.GuestId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.ReservationId));

        var now = ViaReservaERP.AppTime.Now;
        string PriorityFor(string status, DateTime requestDate)
        {
            var ageHours = (now - requestDate).TotalHours;
            if (status.ToLower().Contains("escal")) return "High";
            if (status.ToLower() == "pending" && ageHours >= 48) return "High";
            if (status.ToLower() == "pending" && ageHours >= 12) return "Medium";
            return "Low";
        }

        var rows = requestRowsRaw.Select(x => new ServiceRequestMonitoringRow
        {
            RequestId = x.RequestId,
            GuestName = x.GuestName,
            HotelCompany = x.CompanyName,
            ReservationId = x.GuestId.HasValue && latestReservationByGuest.TryGetValue(x.GuestId.Value, out var rid) ? rid : null,
            ServiceType = x.ServiceType,
            AssignedStaff = x.AssignedStaff,
            AssignedStaffUserId = x.AssignedTo,
            RequestDateUtc = x.RequestDate,
            CompletionDateUtc = null,
            Status = x.Status,
            Priority = PriorityFor(x.Status, x.RequestDate)
        }).ToList();

        var delayed = rows.Count(r => r.Status.ToLower() == "pending" && (now - r.RequestDateUtc).TotalHours >= 24);
        var unassigned = rows.Count(r => string.IsNullOrWhiteSpace(r.AssignedStaff) && r.Status.ToLower() != "completed");
        var failedCompletion = rows.Count(r => r.Status.ToLower().Contains("fail"));

        var auditLogged = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl)
            .Where(a => a.TableName != null && a.TableName.ToLower().Contains("service"))
            .Where(a => !companyId.HasValue || a.CompanyId == companyId.Value)
            .CountAsync(ct);

        var model = new GuestServiceMonitoringViewModel
        {
            Granularity = granularity,
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            CompanyId = companyId,
            CompanyName = companyName,
            Companies = companies,
            TotalServiceRequests = total,
            PendingRequests = pending,
            CompletedRequests = completed,
            InProgressRequests = inProgress,
            EscalatedRequests = escalated,
            AverageResponseTimeHours = avgResponseHours,
            Requests = rows,
            Workflow = new ServiceWorkflowSummary
            {
                GuestRequestCreated = total,
                StaffAssigned = rows.Count(r => !string.IsNullOrWhiteSpace(r.AssignedStaff)),
                ServiceInProgress = inProgress,
                ServiceCompleted = completed,
                FeedbackSubmitted = 0,
                AuditLogged = auditLogged
            },
            Alerts = new ServiceAlertsSummary
            {
                DelayedRequests = delayed,
                EscalatedComplaints = escalated,
                UnassignedRequests = unassigned,
                FailedServiceCompletion = failedCompletion,
                NegativeGuestFeedback = 0
            },
            Page = page,
            PageSize = pageSize,
            TotalRows = total
        };

        await PopulateGuestServiceChartsAsync(model, ct);
        return View(model);
    }

    private async Task PopulateGuestServiceChartsAsync(GuestServiceMonitoringViewModel model, CancellationToken ct)
    {
        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var granularity = (model.Granularity ?? "month").Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        var start = model.StartDate;
        var end = model.EndDate;

        var labels = new List<string>();
        var bucketStarts = new List<DateOnly>();
        var bucketEndsExclusive = new List<DateOnly>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                bucketStarts.Add(d);
                bucketEndsExclusive.Add(d.AddDays(1));
            }
        }
        else if (granularity == "year")
        {
            for (var y = start.Year; y <= end.Year; y++)
            {
                var s = new DateOnly(y, 1, 1);
                var e = new DateOnly(y + 1, 1, 1);
                labels.Add(y.ToString(CultureInfo.InvariantCulture));
                bucketStarts.Add(s);
                bucketEndsExclusive.Add(e);
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                labels.Add(cursor.ToString("MMM", CultureInfo.InvariantCulture));
                bucketStarts.Add(cursor);
                bucketEndsExclusive.Add(cursor.AddMonths(1));
                cursor = cursor.AddMonths(1);
            }
        }

        model.DailyRequestsAnalytics.Labels = labels;
        model.TrendRequestsAnalytics.Labels = labels.ToList();
        model.AvgCompletionTimeAnalytics.Labels = labels.ToList();

        var counts = new List<decimal>();
        var avgAges = new List<decimal>();

        for (var i = 0; i < bucketStarts.Count; i++)
        {
            var sDate = bucketStarts[i];
            var eDate = bucketEndsExclusive[i];

            if (sDate < start) sDate = start;
            if (eDate > end.AddDays(1)) eDate = end.AddDays(1);

            var sUtc = AsUtcStart(sDate);
            var eUtc = AsUtcStart(eDate);

            var q = _db.ServiceRequests.AsNoTracking().Where(sr => sr.RequestDate >= sUtc && sr.RequestDate < eUtc);
            if (model.CompanyId.HasValue)
                q = q.Where(sr => sr.CompanyId == model.CompanyId.Value);

            var c = await q.CountAsync(ct);
            counts.Add(c);

            var nowUtc = ViaReservaERP.AppTime.Now;
            var avgMinutes = await q
                .Select(sr => (double?)EF.Functions.DateDiffMinute(sr.RequestDate, nowUtc))
                .AverageAsync(ct) ?? 0d;
            avgAges.Add((decimal)(avgMinutes / 60d));
        }

        model.DailyRequestsAnalytics.Datasets["Requests"] = counts;
        model.TrendRequestsAnalytics.Datasets["Requests"] = counts.ToList();
        model.AvgCompletionTimeAnalytics.Datasets["Avg Age (hrs)"] = avgAges;

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcStart(end.AddDays(1));

        var topServices = await _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl)
            .Where(sr => !model.CompanyId.HasValue || sr.CompanyId == model.CompanyId.Value)
            .Join(_db.Services.AsNoTracking(), sr => sr.ServiceId, s => s.ServiceId, (sr, s) => new { sr, s })
            .Where(x => x.s.ServiceName != null)
            .GroupBy(x => x.s.ServiceName!)
            .Select(g => new { ServiceName = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync(ct);

        model.MostRequestedServicesAnalytics.Labels = topServices.Select(x => x.ServiceName).ToList();
        model.MostRequestedServicesAnalytics.Datasets["Most Requested"] = topServices.Select(x => (decimal)x.Count).ToList();

        model.GuestSatisfactionSignalsAnalytics.Labels = new List<string> { "Positive", "Neutral", "Negative" };
        model.GuestSatisfactionSignalsAnalytics.Datasets["Signals"] = new List<decimal> { 0, 0, 0 };
    }

    [HttpGet]
    public async Task<IActionResult> ServiceRequestDetails(int id, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var req = await _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Company)
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .Include(sr => sr.AssignedToUser)
            .ThenInclude(u => u!.Role)
            .FirstOrDefaultAsync(sr => sr.RequestId == id, ct);

        if (req == null)
            return NotFound();

        int? reservationId = null;
        if (req.GuestId.HasValue)
            reservationId = await _db.Reservations.AsNoTracking().Where(r => r.GuestId == req.GuestId.Value).OrderByDescending(r => r.ReservationId).Select(r => (int?)r.ReservationId).FirstOrDefaultAsync(ct);

        var timelineQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Include(a => a.Company)
            .Where(a => a.RecordId == id);

        var totalRows = await timelineQ.CountAsync(ct);
        var timeline = await timelineQ
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ServiceTimelineRow
            {
                TimestampUtc = a.ActionDate,
                Action = a.Action ?? "",
                Actor = a.User != null ? a.User.FullName : "",
                ActorRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Table = a.TableName ?? ""
            })
            .ToListAsync(ct);

        var model = new ServiceRequestDetailsViewModel
        {
            RequestId = req.RequestId,
            HotelCompany = req.Company != null ? (req.Company.CompanyName ?? "") : "",
            GuestName = req.Guest != null ? (req.Guest.FullName ?? "") : "",
            GuestEmail = req.Guest != null ? (req.Guest.Email ?? "") : "",
            GuestPhone = req.Guest != null ? (req.Guest.Phone ?? "") : "",
            ReservationId = reservationId,
            ServiceType = req.Service != null ? (req.Service.ServiceName ?? "") : "",
            Status = req.Status ?? "",
            AssignedStaff = req.AssignedToUser != null ? req.AssignedToUser.FullName : "",
            AssignedStaffRole = req.AssignedToUser != null && req.AssignedToUser.Role != null ? req.AssignedToUser.Role.RoleName : "",
            RequestDateUtc = req.RequestDate,
            CompletionDateUtc = null,
            Timeline = timeline,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ServiceAssignedStaff(int userId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Company)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);

        if (user == null)
            return NotFound();

        var q = _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .Where(sr => sr.AssignedTo == userId);

        var totalRows = await q.CountAsync(ct);
        var tasks = await q
            .OrderByDescending(sr => sr.RequestDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sr => new StaffTaskRow
            {
                RequestId = sr.RequestId,
                GuestName = sr.Guest != null ? (sr.Guest.FullName ?? "") : "",
                ServiceType = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                Status = sr.Status ?? "",
                RequestDateUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        var model = new AssignedStaffDetailsViewModel
        {
            UserId = user.UserId,
            FullName = user.FullName,
            Email = user.Email,
            RoleName = user.Role != null ? (user.Role.RoleName ?? "") : "",
            CompanyName = user.Company != null ? (user.Company.CompanyName ?? "") : "",
            AssignedTasks = tasks,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ServiceTimeline(int requestId, CancellationToken ct = default)
    {
        var req = await _db.ServiceRequests.AsNoTracking().Include(sr => sr.Company).FirstOrDefaultAsync(sr => sr.RequestId == requestId, ct);
        if (req == null) return NotFound();

        var timeline = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Include(a => a.Company)
            .Where(a => a.RecordId == requestId)
            .OrderByDescending(a => a.ActionDate)
            .Take(400)
            .Select(a => new ServiceTimelineRow
            {
                TimestampUtc = a.ActionDate,
                Action = a.Action ?? "",
                Actor = a.User != null ? a.User.FullName : "",
                ActorRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Table = a.TableName ?? ""
            })
            .ToListAsync(ct);

        var model = new ServiceRequestAuditLogsViewModel
        {
            RequestId = requestId,
            HotelCompany = req.Company != null ? (req.Company.CompanyName ?? "") : "",
            Rows = timeline
        };

        return View("ServiceAuditLogs", model);
    }

    [HttpGet]
    public async Task<IActionResult> ServiceAuditLogs(int requestId, CancellationToken ct = default)
    {
        var req = await _db.ServiceRequests.AsNoTracking().Include(sr => sr.Company).FirstOrDefaultAsync(sr => sr.RequestId == requestId, ct);
        if (req == null) return NotFound();

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Include(a => a.Company)
            .Where(a => a.RecordId == requestId)
            .OrderByDescending(a => a.ActionDate)
            .Take(400)
            .Select(a => new ServiceTimelineRow
            {
                TimestampUtc = a.ActionDate,
                Action = a.Action ?? "",
                Actor = a.User != null ? a.User.FullName : "",
                ActorRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Table = a.TableName ?? ""
            })
            .ToListAsync(ct);

        var model = new ServiceRequestAuditLogsViewModel
        {
            RequestId = requestId,
            HotelCompany = req.Company != null ? (req.Company.CompanyName ?? "") : "",
            Rows = rows
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult Workflows() => RedirectToAction(nameof(WorkflowManagement));

    [HttpGet]
    public async Task<IActionResult> WorkflowManagement(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? companyId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        DateOnly start;
        DateOnly end;
        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            if (granularity == "day")
            {
                start = today;
                end = today;
            }
            else if (granularity == "year")
            {
                start = new DateOnly(today.Year, 1, 1);
                end = today;
            }
            else
            {
                start = new DateOnly(today.Year, today.Month, 1);
                end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }
        }

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyOptionRow { CompanyId = c.CompanyId, CompanyName = c.CompanyName })
            .ToListAsync(ct);

        var companyName = "All Companies";
        if (companyId.HasValue)
            companyName = companies.FirstOrDefault(c => c.CompanyId == companyId.Value)?.CompanyName ?? "Selected Company";

        var instancesQ = _db.WorkflowInstances
            .AsNoTracking()
            .Include(wi => wi.Company)
            .Include(wi => wi.Workflow)
            .Where(wi => wi.CreatedAt >= startUtc && wi.CreatedAt < endUtcExcl);
        if (companyId.HasValue)
            instancesQ = instancesQ.Where(wi => wi.CompanyId == companyId.Value);

        var totalInRange = await instancesQ.CountAsync(ct);
        var pendingApprovals = await instancesQ.CountAsync(wi => wi.Status != null && (wi.Status.ToLower() == "pending" || wi.Status.ToLower() == "inprogress" || wi.Status.ToLower() == "in progress"), ct);
        var completed = await instancesQ.CountAsync(wi => wi.Status != null && wi.Status.ToLower() == "completed", ct);
        var failed = await instancesQ.CountAsync(wi => wi.Status != null && wi.Status.ToLower().Contains("fail"), ct);
        var escalated = await instancesQ.CountAsync(wi => wi.Status != null && wi.Status.ToLower().Contains("escal"), ct);

        var active = await instancesQ.CountAsync(wi => wi.Status != null && (wi.Status.ToLower() == "pending" || wi.Status.ToLower() == "inprogress" || wi.Status.ToLower() == "in progress"), ct);

        var instanceIdsCompleted = await instancesQ
            .Where(wi => wi.Status != null && wi.Status.ToLower() == "completed")
            .Select(wi => wi.InstanceId)
            .ToListAsync(ct);

        var completionTimesMinutes = await _db.WorkflowInstanceSteps
            .AsNoTracking()
            .Where(s => s.InstanceId != null && instanceIdsCompleted.Contains(s.InstanceId.Value))
            .GroupBy(s => s.InstanceId!.Value)
            .Select(g => new { InstanceId = g.Key, CompletedAt = g.Max(x => x.PerformedAt) })
            .Join(_db.WorkflowInstances.AsNoTracking(), x => x.InstanceId, wi => wi.InstanceId, (x, wi) => (double?)EF.Functions.DateDiffMinute(wi.CreatedAt, x.CompletedAt))
            .ToListAsync(ct);

        var avgProcessingMinutes = completionTimesMinutes.Count == 0 ? 0d : completionTimesMinutes.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(0d).Average();

        var stepLookup = await _db.WorkflowSteps
            .AsNoTracking()
            .Include(ws => ws.Role)
            .ToDictionaryAsync(ws => ws.StepId, ws => new { Name = ws.ActionName ?? "", Role = ws.Role != null ? (ws.Role.RoleName ?? "") : "" }, ct);

        var rawRows = await instancesQ
            .OrderByDescending(wi => wi.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(wi => new
            {
                wi.InstanceId,
                Company = wi.Company != null ? (wi.Company.CompanyName ?? "") : "",
                WorkflowName = wi.Workflow != null ? (wi.Workflow.Name ?? "") : "",
                wi.ReferenceType,
                wi.ReferenceId,
                wi.CurrentStep,
                wi.Status,
                wi.CreatedAt
            })
            .ToListAsync(ct);

        var nowUtc = ViaReservaERP.AppTime.Now;
        string PriorityFor(string status, DateTime createdAt)
        {
            var ageHours = (nowUtc - createdAt).TotalHours;
            var lower = (status ?? "").ToLower();
            if (lower.Contains("escal")) return "High";
            if (lower.Contains("fail")) return "High";
            if ((status ?? "").ToLower() is "pending" or "inprogress" or "in progress" && ageHours >= 48) return "High";
            if ((status ?? "").ToLower() is "pending" or "inprogress" or "in progress" && ageHours >= 12) return "Medium";
            return "Low";
        }

        var completionByInstance = await _db.WorkflowInstanceSteps
            .AsNoTracking()
            .Where(s => s.InstanceId != null)
            .GroupBy(s => s.InstanceId!.Value)
            .Select(g => new { InstanceId = g.Key, CompletedAt = g.Max(x => x.PerformedAt) })
            .ToDictionaryAsync(x => x.InstanceId, x => (DateTime?)x.CompletedAt, ct);

        var rows = rawRows.Select(wi =>
        {
            var stepId = wi.CurrentStep;
            var stepName = stepId.HasValue && stepLookup.TryGetValue(stepId.Value, out var s) ? s.Name : "";
            var dept = stepId.HasValue && stepLookup.TryGetValue(stepId.Value, out var s2) ? s2.Role : "";
            var status = wi.Status ?? "";

            return new WorkflowMonitoringRow
            {
                WorkflowInstanceId = wi.InstanceId,
                CompanyName = wi.Company,
                WorkflowType = string.IsNullOrWhiteSpace(wi.WorkflowName) ? (wi.ReferenceType ?? "") : wi.WorkflowName,
                TriggerSource = wi.ReferenceType ?? "",
                AssignedDepartment = dept,
                CurrentStage = stepName,
                ApprovalStatus = status,
                Priority = PriorityFor(status, wi.CreatedAt),
                CreatedDateUtc = wi.CreatedAt,
                CompletionDateUtc = completionByInstance.TryGetValue(wi.InstanceId, out var done) ? done : null,
                ReferenceId = wi.ReferenceId,
                ReferenceType = wi.ReferenceType ?? ""
            };
        }).ToList();

        var delayedApprovals = rows.Count(r => (r.ApprovalStatus ?? "").ToLower().Contains("pending") && (nowUtc - r.CreatedDateUtc).TotalHours >= 24);
        var slaViolations = rows.Count(r => (r.ApprovalStatus ?? "").ToLower().Contains("pending") && (nowUtc - r.CreatedDateUtc).TotalHours >= 72);

        var auditLogged = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl)
            .Where(a => a.TableName != null && a.TableName.ToLower().Contains("workflow"))
            .Where(a => !companyId.HasValue || a.CompanyId == companyId.Value)
            .CountAsync(ct);

        var model = new WorkflowManagementViewModel
        {
            Granularity = granularity,
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            CompanyId = companyId,
            CompanyName = companyName,
            Companies = companies,
            TotalActiveWorkflows = active,
            CompletedWorkflows = completed,
            PendingApprovals = pendingApprovals,
            EscalatedWorkflows = escalated,
            FailedWorkflows = failed,
            AverageProcessingTimeHours = avgProcessingMinutes / 60d,
            Rows = rows,
            Tracker = new WorkflowProcessTrackerSummary
            {
                RequestCreated = totalInRange,
                ApprovalRouting = pendingApprovals,
                DepartmentAssignment = rows.Count(r => !string.IsNullOrWhiteSpace(r.AssignedDepartment)),
                TaskProcessing = rows.Count(r => (r.ApprovalStatus ?? "").ToLower().Contains("inprogress")),
                Completion = completed,
                AuditLogging = auditLogged
            },
            Alerts = new WorkflowAlertsSummary
            {
                PendingApprovals = pendingApprovals,
                DelayedApprovals = delayedApprovals,
                EscalatedRequests = escalated,
                FailedAutomations = failed,
                SlaViolations = slaViolations
            },
            Page = page,
            PageSize = pageSize,
            TotalRows = totalInRange
        };

        await PopulateWorkflowManagementChartsAsync(model, ct);
        PopulateWorkflowIntegrationRows(model);
        return View(model);
    }

    private void PopulateWorkflowIntegrationRows(WorkflowManagementViewModel model)
    {
        static string MapModule(string rt)
        {
            var s = (rt ?? "").Trim().ToLowerInvariant();
            if (s.Contains("reservation")) return "Reservation Management";
            if (s.Contains("service")) return "Guest Services";
            if (s.Contains("account") || s.Contains("transaction") || s.Contains("finance")) return "Accounting";
            if (s.Contains("subscription")) return "Subscription Billing";
            if (s.Contains("user") || s.Contains("role") || s.Contains("permission")) return "User Management";
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            return char.ToUpperInvariant(s[0]) + s[1..];
        }

        model.IntegrationRows = model.Rows
            .GroupBy(r => MapModule(r.ReferenceType))
            .Select(g => new CrossModuleIntegrationRow
            {
                Module = g.Key,
                Workflows = g.Count(),
                Pending = g.Count(x => (x.ApprovalStatus ?? "").ToLower().Contains("pending") || (x.ApprovalStatus ?? "").ToLower().Contains("inprogress")),
                Escalated = g.Count(x => (x.ApprovalStatus ?? "").ToLower().Contains("escal")),
                Failed = g.Count(x => (x.ApprovalStatus ?? "").ToLower().Contains("fail"))
            })
            .OrderByDescending(x => x.Workflows)
            .ToList();
    }

    private async Task PopulateWorkflowManagementChartsAsync(WorkflowManagementViewModel model, CancellationToken ct)
    {
        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var granularity = (model.Granularity ?? "month").Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        var start = model.StartDate;
        var end = model.EndDate;

        var labels = new List<string>();
        var bucketStarts = new List<DateOnly>();
        var bucketEndsExclusive = new List<DateOnly>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                bucketStarts.Add(d);
                bucketEndsExclusive.Add(d.AddDays(1));
            }
        }
        else if (granularity == "year")
        {
            for (var y = start.Year; y <= end.Year; y++)
            {
                var s = new DateOnly(y, 1, 1);
                var e = new DateOnly(y + 1, 1, 1);
                labels.Add(y.ToString(CultureInfo.InvariantCulture));
                bucketStarts.Add(s);
                bucketEndsExclusive.Add(e);
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                labels.Add(cursor.ToString("MMM", CultureInfo.InvariantCulture));
                bucketStarts.Add(cursor);
                bucketEndsExclusive.Add(cursor.AddMonths(1));
                cursor = cursor.AddMonths(1);
            }
        }

        model.CompletionRatesAnalytics.Labels = labels;
        model.ApprovalDelaysAnalytics.Labels = labels.ToList();
        model.AutomationSuccessAnalytics.Labels = labels.ToList();
        model.EscalationFrequencyAnalytics.Labels = labels.ToList();

        var completionRate = new List<decimal>();
        var approvalDelayHours = new List<decimal>();
        var successRate = new List<decimal>();
        var escalations = new List<decimal>();

        for (var i = 0; i < bucketStarts.Count; i++)
        {
            var sDate = bucketStarts[i];
            var eDate = bucketEndsExclusive[i];

            if (sDate < start) sDate = start;
            if (eDate > end.AddDays(1)) eDate = end.AddDays(1);

            var sUtc = AsUtcStart(sDate);
            var eUtc = AsUtcStart(eDate);

            var q = _db.WorkflowInstances.AsNoTracking().Where(wi => wi.CreatedAt >= sUtc && wi.CreatedAt < eUtc);
            if (model.CompanyId.HasValue)
                q = q.Where(wi => wi.CompanyId == model.CompanyId.Value);

            var total = await q.CountAsync(ct);
            var completed = await q.CountAsync(wi => wi.Status != null && wi.Status.ToLower() == "completed", ct);
            var failed = await q.CountAsync(wi => wi.Status != null && wi.Status.ToLower().Contains("fail"), ct);
            var escal = await q.CountAsync(wi => wi.Status != null && wi.Status.ToLower().Contains("escal"), ct);

            completionRate.Add(total <= 0 ? 0m : (decimal)completed / total * 100m);
            successRate.Add(total <= 0 ? 0m : (decimal)(total - failed) / total * 100m);
            escalations.Add(escal);

            var nowUtc = ViaReservaERP.AppTime.Now;
            var avgDelayMinutes = await q
                .Where(wi => wi.Status != null && (wi.Status.ToLower() == "pending" || wi.Status.ToLower().Contains("inprogress")))
                .Select(wi => (double?)EF.Functions.DateDiffMinute(wi.CreatedAt, nowUtc))
                .AverageAsync(ct) ?? 0d;
            approvalDelayHours.Add((decimal)(avgDelayMinutes / 60d));
        }

        model.CompletionRatesAnalytics.Datasets["Completion Rate (%)"] = completionRate;
        model.ApprovalDelaysAnalytics.Datasets["Approval Delay (hrs)"] = approvalDelayHours;
        model.AutomationSuccessAnalytics.Datasets["Automation Success (%)"] = successRate;
        model.EscalationFrequencyAnalytics.Datasets["Escalations"] = escalations;

        var dept = model.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.AssignedDepartment))
            .GroupBy(r => r.AssignedDepartment)
            .Select(g => new { Dept = g.Key, Pending = g.Count(x => (x.ApprovalStatus ?? "").ToLower().Contains("pending") || (x.ApprovalStatus ?? "").ToLower().Contains("inprogress")) })
            .OrderByDescending(x => x.Pending)
            .Take(8)
            .ToList();

        model.DepartmentBottlenecksAnalytics.Labels = dept.Select(x => x.Dept).ToList();
        model.DepartmentBottlenecksAnalytics.Datasets["Pending"] = dept.Select(x => (decimal)x.Pending).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> WorkflowDetails(int id, CancellationToken ct = default)
    {
        var inst = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(wi => wi.Company)
            .Include(wi => wi.Workflow)
            .FirstOrDefaultAsync(wi => wi.InstanceId == id, ct);
        if (inst == null) return NotFound();

        var stepHistory = await _db.WorkflowInstanceSteps
            .AsNoTracking()
            .Include(s => s.Step)
            .Include(s => s.PerformedByUser)
            .ThenInclude(u => u!.Role)
            .Where(s => s.InstanceId == id)
            .OrderByDescending(s => s.PerformedAt)
            .Take(250)
            .Select(s => new WorkflowStepHistoryRow
            {
                PerformedAtUtc = s.PerformedAt,
                StepName = s.Step != null ? (s.Step.ActionName ?? "") : "",
                StepOrder = s.Step != null ? s.Step.StepOrder : null,
                ActionTaken = s.ActionTaken ?? "",
                PerformedBy = s.PerformedByUser != null ? s.PerformedByUser.FullName : "",
                PerformedByRole = s.PerformedByUser != null && s.PerformedByUser.Role != null ? s.PerformedByUser.Role.RoleName : ""
            })
            .ToListAsync(ct);

        var completedAt = stepHistory.Count > 0 ? stepHistory.Max(x => x.PerformedAtUtc) : (DateTime?)null;

        var model = new WorkflowDetailsViewModel
        {
            InstanceId = inst.InstanceId,
            CompanyName = inst.Company != null ? (inst.Company.CompanyName ?? "") : "",
            WorkflowName = inst.Workflow != null ? (inst.Workflow.Name ?? "") : "",
            WorkflowDescription = inst.Workflow != null ? (inst.Workflow.Description ?? "") : "",
            ReferenceType = inst.ReferenceType ?? "",
            ReferenceId = inst.ReferenceId,
            Status = inst.Status ?? "",
            CurrentStep = inst.CurrentStep,
            CreatedAtUtc = inst.CreatedAt,
            CompletedAtUtc = completedAt,
            StepHistory = stepHistory
        };

        return View(model);
    }


    [HttpGet]
    public async Task<IActionResult> WorkflowApprovalHistory(int id, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var inst = await _db.WorkflowInstances.AsNoTracking().Include(wi => wi.Company).FirstOrDefaultAsync(wi => wi.InstanceId == id, ct);
        if (inst == null) return NotFound();

        var q = _db.WorkflowInstanceSteps
            .AsNoTracking()
            .Include(s => s.Step)
            .Include(s => s.PerformedByUser)
            .ThenInclude(u => u!.Role)
            .Where(s => s.InstanceId == id);

        var totalRows = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(s => s.PerformedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new WorkflowStepHistoryRow
            {
                PerformedAtUtc = s.PerformedAt,
                StepName = s.Step != null ? (s.Step.ActionName ?? "") : "",
                StepOrder = s.Step != null ? s.Step.StepOrder : null,
                ActionTaken = s.ActionTaken ?? "",
                PerformedBy = s.PerformedByUser != null ? s.PerformedByUser.FullName : "",
                PerformedByRole = s.PerformedByUser != null && s.PerformedByUser.Role != null ? s.PerformedByUser.Role.RoleName : ""
            })
            .ToListAsync(ct);

        return View(new WorkflowApprovalHistoryViewModel
        {
            InstanceId = id,
            CompanyName = inst.Company != null ? (inst.Company.CompanyName ?? "") : "",
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        });
    }

    [HttpGet]
    public async Task<IActionResult> WorkflowAuditLogs(int id, CancellationToken ct = default)
    {
        var inst = await _db.WorkflowInstances.AsNoTracking().Include(wi => wi.Company).FirstOrDefaultAsync(wi => wi.InstanceId == id, ct);
        if (inst == null) return NotFound();

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Include(a => a.Company)
            .Where(a => a.RecordId == id)
            .OrderByDescending(a => a.ActionDate)
            .Take(400)
            .Select(a => new AuditLogRow
            {
                TimestampUtc = a.ActionDate,
                Action = a.Action ?? "",
                UserName = a.User != null ? a.User.FullName : "",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Table = a.TableName ?? ""
            })
            .ToListAsync(ct);

        return View(new WorkflowAuditLogsViewModel
        {
            InstanceId = id,
            CompanyName = inst.Company != null ? (inst.Company.CompanyName ?? "") : "",
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> WorkflowDepartmentTasks(int id, CancellationToken ct = default)
    {
        var inst = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.Company)
            .FirstOrDefaultAsync(i => i.InstanceId == id, ct);
        if (inst == null) return NotFound();

        var currentStep = inst.CurrentStep;
        var dept = "";
        if (currentStep.HasValue)
        {
            dept = await _db.WorkflowSteps
                .AsNoTracking()
                .Include(s => s.Role)
                .Where(s => s.StepId == currentStep.Value)
                .Select(s => s.Role != null ? (s.Role.RoleName ?? "") : "")
                .FirstOrDefaultAsync(ct) ?? "";
        }

        var rows = await _db.WorkflowInstanceSteps
            .AsNoTracking()
            .Include(s => s.Step)
            .Include(s => s.PerformedByUser)
            .ThenInclude(u => u!.Role)
            .Where(s => s.InstanceId == id)
            .OrderByDescending(s => s.PerformedAt)
            .Take(400)
            .Select(s => new WorkflowStepHistoryRow
            {
                PerformedAtUtc = s.PerformedAt,
                StepName = s.Step != null ? (s.Step.ActionName ?? "") : "",
                StepOrder = s.Step != null ? s.Step.StepOrder : null,
                ActionTaken = s.ActionTaken ?? "",
                PerformedBy = s.PerformedByUser != null ? s.PerformedByUser.FullName : "",
                PerformedByRole = s.PerformedByUser != null && s.PerformedByUser.Role != null ? s.PerformedByUser.Role.RoleName : ""
            })
            .ToListAsync(ct);

        return View(new WorkflowDepartmentTasksViewModel
        {
            InstanceId = id,
            CompanyName = inst.Company != null ? (inst.Company.CompanyName ?? "") : "",
            Department = dept,
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> Accounting(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? companyId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        DateOnly start;
        DateOnly end;
        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            if (granularity == "day")
            {
                start = today;
                end = today;
            }
            else if (granularity == "year")
            {
                start = new DateOnly(today.Year, 1, 1);
                end = today;
            }
            else
            {
                start = new DateOnly(today.Year, today.Month, 1);
                end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }
        }

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyOptionRow { CompanyId = c.CompanyId, CompanyName = c.CompanyName })
            .ToListAsync(ct);

        var companyName = "All Companies";
        if (companyId.HasValue)
            companyName = companies.FirstOrDefault(c => c.CompanyId == companyId.Value)?.CompanyName ?? "Selected Company";

        var paymentsQ = _db.Payments.AsNoTracking().Where(p => p.Amount != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl);
        if (companyId.HasValue)
            paymentsQ = paymentsQ.Where(p => p.CompanyId == companyId.Value);

        var succeededPaymentsQ = paymentsQ.Where(p => p.Status != null && p.Status.ToLower() == "succeeded");

        var totalRevenue = await succeededPaymentsQ.SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;
        var reservationRevenue = await succeededPaymentsQ.Where(p => p.ReservationId != null).SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;

        var refunds = await paymentsQ
            .Where(p => p.Status != null && p.Status.ToLower().Contains("refund"))
            .SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;

        var txnQ = _db.Transactions.AsNoTracking().Where(t => t.Amount != null && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl);
        if (companyId.HasValue)
            txnQ = txnQ.Where(t => t.CompanyId == companyId.Value);

        var guestServiceRevenue = await txnQ
            .Where(t => t.ReferenceType != null && t.ReferenceType.ToLower().Contains("service"))
            .Where(t => t.Amount != null && t.Amount.Value > 0)
            .SumAsync(t => (decimal?)t.Amount!, ct) ?? 0m;

        var subscriptionRevenue = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.Plan != null && s.Plan.Price != null)
            .Where(s => s.StartDate != null)
            .Where(s => s.StartDate!.Value >= start && s.StartDate!.Value <= end)
            .Where(s => !companyId.HasValue || s.CompanyId == companyId.Value)
            .SumAsync(s => (decimal?)s.Plan!.Price!, ct) ?? 0m;

        var operationalExpenses = await txnQ
            .Where(t => t.Type != null && t.Type.ToLower().Contains("expense"))
            .SumAsync(t => (decimal?)t.Amount!, ct) ?? 0m;

        var companyLevelExpenses = await txnQ
            .Where(t => t.Amount != null && t.Amount.Value < 0)
            .SumAsync(t => (decimal?)(-t.Amount!.Value), ct) ?? 0m;

        var serviceOperationalCosts = await txnQ
            .Where(t => t.ReferenceType != null && t.ReferenceType.ToLower().Contains("service"))
            .Where(t => t.Amount != null && t.Amount.Value < 0)
            .SumAsync(t => (decimal?)(-t.Amount!.Value), ct) ?? 0m;

        var netProfit = (totalRevenue + guestServiceRevenue + subscriptionRevenue) - refunds - operationalExpenses - companyLevelExpenses;

        var totalPaymentsCount = await _db.Payments.AsNoTracking().CountAsync(p => p.Amount != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl && (!companyId.HasValue || p.CompanyId == companyId.Value), ct);
        var totalTxnsCount = await _db.Transactions.AsNoTracking().CountAsync(t => t.Amount != null && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl && (!companyId.HasValue || t.CompanyId == companyId.Value), ct);

        var paymentRows = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Reservation)
            .ThenInclude(r => r!.Guest)
            .Where(p => p.Amount != null && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
            .OrderByDescending(p => p.CreatedAt)
            .Take(page * pageSize + 100) 
            .Select(p => new AccountingTransactionRow
            {
                Source = "payment",
                Id = p.PaymentId,
                Company = p.Company != null ? p.Company.CompanyName : "",
                TransactionType = p.Status != null && p.Status.ToLower().Contains("refund") ? "Refund" : "Reservation Payment",
                RelatedModule = p.ReservationId != null ? "Reservations" : "Payments",
                CustomerOrGuest = p.Reservation != null && p.Reservation.Guest != null ? (p.Reservation.Guest.FullName ?? "") : "",
                PaymentMethod = p.PaymentMethod ?? "",
                Amount = p.Amount ?? 0m,
                Status = p.Status ?? "",
                TransactionDateUtc = p.CreatedAt,
                ReservationId = p.ReservationId
            })
            .ToListAsync(ct);

        var transactionRows = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Company)
            .Where(t => t.Amount != null && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl)
            .Where(t => !companyId.HasValue || t.CompanyId == companyId.Value)
            .OrderByDescending(t => t.TransactionDate)
            .Take(page * pageSize + 100) 
            .Select(t => new AccountingTransactionRow
            {
                Source = "transaction",
                Id = t.TransactionId,
                Company = t.Company != null ? t.Company.CompanyName : "",
                TransactionType = t.Type ?? "Manual Adjustment",
                RelatedModule = t.ReferenceType ?? "Accounting",
                CustomerOrGuest = "",
                PaymentMethod = "",
                Amount = t.Amount ?? 0m,
                Status = "Recorded",
                TransactionDateUtc = t.TransactionDate,
                ReservationId = t.ReferenceType != null && t.ReferenceType.ToLower().Contains("reservation") ? t.ReferenceId : null
            })
            .ToListAsync(ct);

        var merged = paymentRows
            .Concat(transactionRows)
            .OrderByDescending(r => r.TransactionDateUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var failedPayments = await paymentsQ.CountAsync(p => p.Status != null && p.Status.ToLower() != "succeeded", ct);
        var suspiciousTransactions = await txnQ.CountAsync(t => t.Amount != null && (t.Amount.Value >= 50000m || t.Amount.Value <= -50000m), ct);

        var refundByCompany = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Amount != null)
            .Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
            .GroupBy(p => p.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Total = g.Sum(x => (decimal?)x.Amount!) ?? 0m,
                Refund = g.Where(x => x.Status != null && x.Status.ToLower().Contains("refund")).Sum(x => (decimal?)x.Amount!) ?? 0m
            })
            .ToListAsync(ct);

        var highRefundRateCompanies = refundByCompany.Count(x => x.Total > 0m && (x.Refund / x.Total) >= 0.15m);

        var revenueDeclineAlerts = 0;
        var compareStart = start.AddDays(-(end.DayNumber - start.DayNumber + 1));
        var compareEnd = start.AddDays(-1);
        if (compareEnd >= new DateOnly(2000, 1, 1))
        {
            var compareStartUtc = AsUtcStart(compareStart);
            var compareEndUtcExcl = AsUtcEndExclusive(compareEnd);
            var prev = await _db.Payments
                .AsNoTracking()
                .Where(p => p.Amount != null)
                .Where(p => p.CreatedAt >= compareStartUtc && p.CreatedAt < compareEndUtcExcl)
                .Where(p => !companyId.HasValue || p.CompanyId == companyId.Value)
                .SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;
            if (prev > 0m && totalRevenue < prev * 0.8m)
                revenueDeclineAlerts = 1;
        }

        var model = new AccountingOverviewViewModel
        {
            Granularity = granularity,
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            CompanyId = companyId,
            CompanyName = companyName,
            Companies = companies,

            TotalRevenue = totalRevenue,
            ReservationRevenue = reservationRevenue,
            GuestServiceRevenue = guestServiceRevenue,
            SubscriptionRevenue = subscriptionRevenue,
            Refunds = refunds,
            NetProfit = netProfit,

            Transactions = merged,

            Expenses = new AccountingExpenseSummary
            {
                OperationalExpenses = operationalExpenses,
                RefundLosses = refunds,
                ServiceOperationalCosts = serviceOperationalCosts,
                CompanyLevelExpenses = companyLevelExpenses
            },
            Alerts = new AccountingAlertsSummary
            {
                FailedPayments = failedPayments,
                HighRefundRateCompanies = highRefundRateCompanies,
                SuspiciousTransactions = suspiciousTransactions,
                SubscriptionPaymentFailures = 0,
                RevenueDeclineAlerts = revenueDeclineAlerts
            },
            Workflow = new AccountingWorkflowTrackerSummary
            {
                ReservationPayments = await succeededPaymentsQ.CountAsync(p => p.ReservationId != null, ct),
                ServiceCharges = await txnQ.CountAsync(t => t.ReferenceType != null && t.ReferenceType.ToLower().Contains("service") && t.Amount != null && t.Amount.Value > 0, ct),
                RefundProcessing = await paymentsQ.CountAsync(p => p.Status != null && p.Status.ToLower().Contains("refund"), ct),
                FinancialRecording = await txnQ.CountAsync(ct),
                AuditLogging = await _db.AuditLogs.AsNoTracking().Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl).Where(a => !companyId.HasValue || a.CompanyId == companyId.Value).Where(a => a.TableName != null && (a.TableName.ToLower() == "payments" || a.TableName.ToLower() == "transactions")).CountAsync(ct),
                Reporting = 0
            },
            Page = page,
            PageSize = pageSize,
            TotalRows = totalPaymentsCount + totalTxnsCount
        };

        await PopulateAccountingChartsAsync(model, ct);
        return View(model);
    }

    private async Task PopulateAccountingChartsAsync(AccountingOverviewViewModel model, CancellationToken ct)
    {
        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var granularity = (model.Granularity ?? "month").Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        var start = model.StartDate;
        var end = model.EndDate;

        var labels = new List<string>();
        var bucketStarts = new List<DateOnly>();
        var bucketEndsExclusive = new List<DateOnly>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                bucketStarts.Add(d);
                bucketEndsExclusive.Add(d.AddDays(1));
            }
        }
        else if (granularity == "year")
        {
            for (var y = start.Year; y <= end.Year; y++)
            {
                var s = new DateOnly(y, 1, 1);
                var e = new DateOnly(y + 1, 1, 1);
                labels.Add(y.ToString(CultureInfo.InvariantCulture));
                bucketStarts.Add(s);
                bucketEndsExclusive.Add(e);
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                labels.Add(cursor.ToString("MMM", CultureInfo.InvariantCulture));
                bucketStarts.Add(cursor);
                bucketEndsExclusive.Add(cursor.AddMonths(1));
                cursor = cursor.AddMonths(1);
            }
        }

        model.RevenueBreakdownAnalytics.Labels = labels;
        model.RevenueGrowthAnalytics.Labels = labels.ToList();

        var reservation = new List<decimal>();
        var service = new List<decimal>();
        var subs = new List<decimal>();
        var total = new List<decimal>();

        for (var i = 0; i < bucketStarts.Count; i++)
        {
            var sDate = bucketStarts[i];
            var eDate = bucketEndsExclusive[i];
            if (sDate < start) sDate = start;
            if (eDate > end.AddDays(1)) eDate = end.AddDays(1);

            var sUtc = AsUtcStart(sDate);
            var eUtc = AsUtcStart(eDate);

            var payQ = _db.Payments.AsNoTracking()
                .Where(p => p.Amount != null)
                .Where(p => p.CreatedAt >= sUtc && p.CreatedAt < eUtc)
                .Where(p => p.Status != null && p.Status.ToLower() == "succeeded");
            if (model.CompanyId.HasValue)
                payQ = payQ.Where(p => p.CompanyId == model.CompanyId.Value);

            var txnQ = _db.Transactions.AsNoTracking()
                .Where(t => t.Amount != null)
                .Where(t => t.TransactionDate >= sUtc && t.TransactionDate < eUtc);
            if (model.CompanyId.HasValue)
                txnQ = txnQ.Where(t => t.CompanyId == model.CompanyId.Value);

            var resRev = await payQ.Where(p => p.ReservationId != null).SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;
            var svcRev = await txnQ.Where(t => t.ReferenceType != null && t.ReferenceType.ToLower().Contains("service")).Where(t => t.Amount != null && t.Amount.Value > 0).SumAsync(t => (decimal?)t.Amount!, ct) ?? 0m;
            var subRev = await _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.Plan != null && s.Plan.Price != null)
                .Where(s => s.StartDate != null)
                .Where(s => s.StartDate!.Value >= sDate && s.StartDate!.Value < eDate)
                .Where(s => !model.CompanyId.HasValue || s.CompanyId == model.CompanyId.Value)
                .SumAsync(s => (decimal?)s.Plan!.Price!, ct) ?? 0m;

            reservation.Add(resRev);
            service.Add(svcRev);
            subs.Add(subRev);
            total.Add(resRev + svcRev + subRev);
        }

        model.RevenueBreakdownAnalytics.Datasets["Reservation Revenue"] = reservation;
        model.RevenueBreakdownAnalytics.Datasets["Service Revenue"] = service;
        model.RevenueBreakdownAnalytics.Datasets["Subscription Revenue"] = subs;
        model.RevenueGrowthAnalytics.Datasets["Total Revenue"] = total;

        var yearLabels = new List<string>();
        var yearProfit = new List<decimal>();
        var startYear = Math.Max(2000, end.Year - 4);
        for (var y = startYear; y <= end.Year; y++)
        {
            yearLabels.Add(y.ToString(CultureInfo.InvariantCulture));
            var yStart = new DateOnly(y, 1, 1);
            var yEnd = new DateOnly(y + 1, 1, 1);
            var yStartUtc = AsUtcStart(yStart);
            var yEndUtc = AsUtcStart(yEnd);

            var payQ = _db.Payments.AsNoTracking().Where(p => p.Amount != null).Where(p => p.CreatedAt >= yStartUtc && p.CreatedAt < yEndUtc).Where(p => p.Status != null && p.Status.ToLower() == "succeeded");
            if (model.CompanyId.HasValue)
                payQ = payQ.Where(p => p.CompanyId == model.CompanyId.Value);
            var rev = await payQ.SumAsync(p => (decimal?)p.Amount!, ct) ?? 0m;

            var txnQ = _db.Transactions.AsNoTracking().Where(t => t.Amount != null).Where(t => t.TransactionDate >= yStartUtc && t.TransactionDate < yEndUtc);
            if (model.CompanyId.HasValue)
                txnQ = txnQ.Where(t => t.CompanyId == model.CompanyId.Value);
            var exp = await txnQ.Where(t => t.Type != null && t.Type.ToLower().Contains("expense")).SumAsync(t => (decimal?)t.Amount!, ct) ?? 0m;
            var neg = await txnQ.Where(t => t.Amount != null && t.Amount.Value < 0).SumAsync(t => (decimal?)(-t.Amount!.Value), ct) ?? 0m;

            yearProfit.Add(rev - exp - neg);
        }

        model.YearlyTrendsAnalytics.Labels = yearLabels;
        model.YearlyTrendsAnalytics.Datasets["Net Profit"] = yearProfit;
    }

    [HttpGet]
    public async Task<IActionResult> TransactionDetails(string source, int id, CancellationToken ct = default)
    {
        source = (source ?? "").Trim().ToLowerInvariant();
        if (source != "payment" && source != "transaction")
            return NotFound();

        if (source == "payment")
        {
            var p = await _db.Payments
                .AsNoTracking()
                .Include(x => x.Company)
                .Include(x => x.Reservation)
                .ThenInclude(r => r!.Guest)
                .FirstOrDefaultAsync(x => x.PaymentId == id, ct);
            if (p == null) return NotFound();

            return View(new TransactionDetailsViewModel
            {
                Source = source,
                Id = p.PaymentId,
                CompanyName = p.Company != null ? p.Company.CompanyName : "",
                TransactionType = p.Status != null && p.Status.ToLower().Contains("refund") ? "Refund" : "Reservation Payment",
                RelatedModule = p.ReservationId != null ? "Reservations" : "Payments",
                CustomerOrGuest = p.Reservation != null && p.Reservation.Guest != null ? (p.Reservation.Guest.FullName ?? "") : "",
                PaymentMethod = p.PaymentMethod ?? "",
                Amount = p.Amount ?? 0m,
                Status = p.Status ?? "",
                TransactionDateUtc = p.CreatedAt,
                Description = p.ReservationId != null ? $"Reservation #{p.ReservationId.Value} payment" : "Payment record",
                ReservationId = p.ReservationId,
                StripePaymentIntentId = p.StripePaymentIntentId ?? ""
            });
        }

        var t = await _db.Transactions
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.TransactionId == id, ct);
        if (t == null) return NotFound();

        return View(new TransactionDetailsViewModel
        {
            Source = source,
            Id = t.TransactionId,
            CompanyName = t.Company != null ? t.Company.CompanyName : "",
            TransactionType = t.Type ?? "Manual Adjustment",
            RelatedModule = t.ReferenceType ?? "Accounting",
            CustomerOrGuest = "",
            PaymentMethod = "",
            Amount = t.Amount ?? 0m,
            Status = "Recorded",
            TransactionDateUtc = t.TransactionDate,
            Description = t.Description ?? "",
            ReservationId = t.ReferenceType != null && t.ReferenceType.ToLower().Contains("reservation") ? t.ReferenceId : null,
            StripePaymentIntentId = ""
        });
    }

    [HttpGet]
    public async Task<IActionResult> TransactionPaymentHistory(string source, int id, CancellationToken ct = default)
    {
        source = (source ?? "").Trim().ToLowerInvariant();
        if (source != "payment" && source != "transaction")
            return NotFound();

        int? reservationId = null;
        int? companyId = null;

        if (source == "payment")
        {
            var p = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.PaymentId == id, ct);
            if (p == null) return NotFound();
            reservationId = p.ReservationId;
            companyId = p.CompanyId;
        }
        else
        {
            var t = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.TransactionId == id, ct);
            if (t == null) return NotFound();
            if (t.ReferenceType != null && t.ReferenceType.ToLower().Contains("reservation"))
                reservationId = t.ReferenceId;
            companyId = t.CompanyId;
        }

        var companyName = companyId.HasValue
            ? await _db.Companies.AsNoTracking().Where(c => c.CompanyId == companyId.Value).Select(c => c.CompanyName).FirstOrDefaultAsync(ct) ?? ""
            : "";

        var payQ = _db.Payments.AsNoTracking().Where(p => p.Amount != null);
        if (reservationId.HasValue)
            payQ = payQ.Where(p => p.ReservationId == reservationId.Value);
        else if (companyId.HasValue)
            payQ = payQ.Where(p => p.CompanyId == companyId.Value);

        var rows = await payQ
            .OrderByDescending(p => p.CreatedAt)
            .Take(250)
            .Select(p => new TransactionPaymentHistoryRow
            {
                PaymentId = p.PaymentId,
                CreatedAtUtc = p.CreatedAt,
                PaymentMethod = p.PaymentMethod ?? "",
                Amount = p.Amount ?? 0m,
                Status = p.Status ?? "",
                StripePaymentIntentId = p.StripePaymentIntentId ?? ""
            })
            .ToListAsync(ct);

        return View(new TransactionPaymentHistoryViewModel
        {
            Source = source,
            Id = id,
            CompanyName = companyName,
            ReservationId = reservationId,
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> TransactionAuditLogs(string source, int id, CancellationToken ct = default)
    {
        source = (source ?? "").Trim().ToLowerInvariant();
        if (source != "payment" && source != "transaction")
            return NotFound();

        string tableName;
        int? companyId;

        if (source == "payment")
        {
            var p = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.PaymentId == id, ct);
            if (p == null) return NotFound();
            companyId = p.CompanyId;
            tableName = "payments";
        }
        else
        {
            var t = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.TransactionId == id, ct);
            if (t == null) return NotFound();
            companyId = t.CompanyId;
            tableName = "transactions";
        }

        var companyName = companyId.HasValue
            ? await _db.Companies.AsNoTracking().Where(c => c.CompanyId == companyId.Value).Select(c => c.CompanyName).FirstOrDefaultAsync(ct) ?? ""
            : "";

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.Company)
            .Include(a => a.User)
            .Where(a => a.TableName != null && a.TableName.ToLower() == tableName)
            .Where(a => a.RecordId != null && a.RecordId.Value == id)
            .OrderByDescending(a => a.ActionDate)
            .Take(300)
            .Select(a => new TransactionAuditLogRow
            {
                TimestampUtc = a.ActionDate,
                Company = a.Company != null ? a.Company.CompanyName : "",
                UserName = a.User != null ? a.User.FullName : "",
                Action = a.Action ?? "",
                Table = a.TableName ?? "",
                RecordId = a.RecordId,
                IpAddress = a.IPAddress ?? ""
            })
            .ToListAsync(ct);

        return View(new TransactionAuditLogsViewModel
        {
            Source = source,
            Id = id,
            CompanyName = companyName,
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> TransactionLinkedReservation(string source, int id, CancellationToken ct = default)
    {
        source = (source ?? "").Trim().ToLowerInvariant();
        if (source != "payment" && source != "transaction")
            return NotFound();

        int? reservationId;
        if (source == "payment")
        {
            var p = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.PaymentId == id, ct);
            if (p == null) return NotFound();
            reservationId = p.ReservationId;
        }
        else
        {
            var t = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.TransactionId == id, ct);
            if (t == null) return NotFound();
            reservationId = t.ReferenceType != null && t.ReferenceType.ToLower().Contains("reservation") ? t.ReferenceId : null;
        }

        if (!reservationId.HasValue)
            return NotFound();

        var rsv = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId.Value, ct);

        if (rsv == null)
            return NotFound();

        return View(new TransactionLinkedReservationViewModel
        {
            Source = source,
            Id = id,
            ReservationId = rsv.ReservationId,
            CompanyName = rsv.Company != null ? (rsv.Company.CompanyName ?? "") : "",
            GuestName = rsv.Guest != null ? (rsv.Guest.FullName ?? "") : "",
            CheckIn = rsv.CheckInDate,
            CheckOut = rsv.CheckOutDate,
            Status = rsv.Status ?? "",
            TotalAmount = rsv.TotalAmount ?? 0m
        });
    }
    [HttpGet]
    public async Task<IActionResult> Subscriptions(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? planId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        DateOnly start;
        DateOnly end;
        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            if (granularity == "day")
            {
                start = today;
                end = today;
            }
            else if (granularity == "year")
            {
                start = new DateOnly(today.Year, 1, 1);
                end = today;
            }
            else
            {
                start = new DateOnly(today.Year, today.Month, 1);
                end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }
        }

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var plans = await _db.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(p => p.PlanName)
            .Select(p => new PlanOptionRow { PlanId = p.PlanId, PlanName = p.PlanName ?? "" })
            .ToListAsync(ct);

        var planName = "All Plans";
        if (planId.HasValue)
            planName = plans.FirstOrDefault(p => p.PlanId == planId.Value)?.PlanName ?? "Selected Plan";

        var subsQ = _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .Where(s => s.StartDate != null && s.StartDate.Value >= start && s.StartDate.Value <= end);
        if (planId.HasValue)
            subsQ = subsQ.Where(s => s.PlanId == planId.Value);

        var totalRows = await subsQ.CountAsync(ct);
        var active = await subsQ.CountAsync(s => s.Status != null && s.Status.ToLower() == "active", ct);
        var cancelled = await subsQ.CountAsync(s => s.Status != null && s.Status.ToLower() == "cancelled", ct);
        var trials = await subsQ.CountAsync(s => s.Status != null && s.Status.ToLower().Contains("trial"), ct);
        var expiringSoon = await subsQ.CountAsync(s => s.EndDate != null && s.EndDate.Value >= today && s.EndDate.Value <= today.AddDays(14) && (s.Status == null || s.Status.ToLower() == "active"), ct);

        var failedPayments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => p.ReservationId == null)
            .Where(p => p.Status != null && p.Status.ToLower() != "succeeded")
            .CountAsync(ct);

        var mrr = await subsQ
            .Where(s => s.Status != null && s.Status.ToLower() == "active")
            .Where(s => s.Plan != null && s.Plan.Price != null)
            .SumAsync(s => (decimal?)s.Plan!.Price!, ct) ?? 0m;

        var subsRows = await subsQ
            .OrderByDescending(s => s.SubscriptionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SubscriptionMonitoringRow
            {
                SubscriptionId = s.SubscriptionId,
                CompanyId = s.CompanyId ?? 0,
                CompanyName = s.Company != null ? s.Company.CompanyName : "",
                PlanName = s.Plan != null ? (s.Plan.PlanName ?? "") : "",
                BillingCycle = s.Plan != null && s.Plan.DurationMonths != null ? $"Every {s.Plan.DurationMonths.Value} mo" : "Monthly",
                StartDate = s.StartDate,
                RenewalDate = s.EndDate,
                PaymentStatus = "—",
                SubscriptionStatus = s.Status ?? "",
                AmountPaid = s.Plan != null ? (s.Plan.Price ?? 0m) : 0m
            })
            .ToListAsync(ct);

        var companyIds = subsRows.Select(r => r.CompanyId).Where(id => id > 0).Distinct().ToList();
        var lastPaymentStatusByCompany = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId != null && companyIds.Contains(p.CompanyId.Value))
            .Where(p => p.ReservationId == null)
            .GroupBy(p => p.CompanyId!.Value)
            .Select(g => new { CompanyId = g.Key, Status = g.OrderByDescending(x => x.CreatedAt).Select(x => x.Status).FirstOrDefault() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Status ?? "", ct);
        foreach (var r in subsRows)
            r.PaymentStatus = lastPaymentStatusByCompany.TryGetValue(r.CompanyId, out var ps) ? ps : "—";

        var usageRows = await _db.Companies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.CompanyId))
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyUsageRow
            {
                CompanyId = c.CompanyId,
                CompanyName = c.CompanyName,
                Users = _db.Users.Count(u => !u.IsDeleted && u.CompanyId == c.CompanyId),
                ReservationVolume = _db.Reservations.Count(r => r.CompanyId == c.CompanyId),
                WorkflowInstances = _db.WorkflowInstances.Count(w => w.CompanyId == c.CompanyId),
                ServiceRequests = _db.ServiceRequests.Count(sr => sr.CompanyId == c.CompanyId),
                ActiveModulesUsed = 4
            })
            .ToListAsync(ct);

        var model = new SubscriptionManagementViewModel
        {
            Granularity = granularity,
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            PlanId = planId,
            PlanName = planName,
            Plans = plans,

            ActiveSubscriptions = active,
            TrialAccounts = trials,
            ExpiringSubscriptions = expiringSoon,
            CancelledSubscriptions = cancelled,
            FailedPayments = failedPayments,
            MonthlyRecurringRevenue = mrr,

            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,

            Rows = subsRows,
            UsageRows = usageRows,

            Alerts = new SubscriptionAlertsSummary
            {
                FailedStripePayments = failedPayments,
                ExpiringSubscriptions = expiringSoon,
                OverdueInvoices = 0,
                CancelledRenewals = cancelled,
                TrialExpirations = trials,
            },
            Workflow = new SubscriptionWorkflowTrackerSummary
            {
                CompanyRegistrations = await _db.Companies.AsNoTracking().Where(c => c.CreatedAt >= startUtc && c.CreatedAt < endUtcExcl).CountAsync(ct),
                PlanSelections = await subsQ.CountAsync(ct),
                StripePayments = await _db.Payments.AsNoTracking().Where(p => p.ReservationId == null && p.StripePaymentIntentId != null).Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl).CountAsync(ct),
                SubscriptionActivations = active,
                RenewalProcessing = await subsQ.CountAsync(s => s.EndDate != null && s.EndDate.Value >= start && s.EndDate.Value <= end, ct),
                BillingLogs = await _db.Payments.AsNoTracking().Where(p => p.ReservationId == null).Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl).CountAsync(ct),
                AuditLogging = await _db.AuditLogs.AsNoTracking().Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl).Where(a => a.TableName != null && (a.TableName.ToLower() == "subscriptions" || a.TableName.ToLower() == "subscriptionplans" || a.TableName.ToLower() == "payments")).CountAsync(ct)
            }
        };

        await PopulateSubscriptionChartsAsync(model, ct);
        return View(model);
    }

    private async Task PopulateSubscriptionChartsAsync(SubscriptionManagementViewModel model, CancellationToken ct)
    {
        var granularity = (model.Granularity ?? "month").Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year")
            granularity = "month";

        var start = model.StartDate;
        var end = model.EndDate;

        var labels = new List<string>();
        var bucketStarts = new List<DateOnly>();
        var bucketEndsExclusive = new List<DateOnly>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                bucketStarts.Add(d);
                bucketEndsExclusive.Add(d.AddDays(1));
            }
        }
        else if (granularity == "year")
        {
            for (var y = start.Year; y <= end.Year; y++)
            {
                var s = new DateOnly(y, 1, 1);
                var e = new DateOnly(y + 1, 1, 1);
                labels.Add(y.ToString(CultureInfo.InvariantCulture));
                bucketStarts.Add(s);
                bucketEndsExclusive.Add(e);
            }
        }
        else
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                labels.Add(cursor.ToString("MMM", CultureInfo.InvariantCulture));
                bucketStarts.Add(cursor);
                bucketEndsExclusive.Add(cursor.AddMonths(1));
                cursor = cursor.AddMonths(1);
            }
        }

        model.MrrAnalytics.Labels = labels;
        model.NewSubscriptionsAnalytics.Labels = labels.ToList();
        model.ChurnRateAnalytics.Labels = labels.ToList();
        model.RenewalTrendsAnalytics.Labels = labels.ToList();

        var mrr = new List<decimal>();
        var newSubs = new List<decimal>();
        var churn = new List<decimal>();
        var renewals = new List<decimal>();

        for (var i = 0; i < bucketStarts.Count; i++)
        {
            var sDate = bucketStarts[i];
            var eDate = bucketEndsExclusive[i];
            if (sDate < start) sDate = start;
            if (eDate > end.AddDays(1)) eDate = end.AddDays(1);

            var subsQ = _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.StartDate != null && s.StartDate.Value >= sDate && s.StartDate.Value < eDate);
            if (model.PlanId.HasValue)
                subsQ = subsQ.Where(s => s.PlanId == model.PlanId.Value);

            newSubs.Add(await subsQ.CountAsync(ct));

            var activeQ = _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.Status != null && s.Status.ToLower() == "active")
                .Where(s => s.StartDate != null && s.StartDate.Value <= eDate.AddDays(-1))
                .Where(s => s.EndDate == null || s.EndDate.Value >= sDate);
            if (model.PlanId.HasValue)
                activeQ = activeQ.Where(s => s.PlanId == model.PlanId.Value);

            mrr.Add(await activeQ.Where(s => s.Plan != null && s.Plan.Price != null).SumAsync(s => (decimal?)s.Plan!.Price!, ct) ?? 0m);

            var churnCount = await _db.Subscriptions
                .AsNoTracking()
                .Where(s => s.Status != null && s.Status.ToLower() == "cancelled")
                .Where(s => s.EndDate != null && s.EndDate.Value >= sDate && s.EndDate.Value < eDate)
                .Where(s => !model.PlanId.HasValue || s.PlanId == model.PlanId.Value)
                .CountAsync(ct);
            churn.Add(churnCount);

            var renewCount = await _db.Subscriptions
                .AsNoTracking()
                .Where(s => s.EndDate != null && s.EndDate.Value >= sDate && s.EndDate.Value < eDate)
                .Where(s => !model.PlanId.HasValue || s.PlanId == model.PlanId.Value)
                .CountAsync(ct);
            renewals.Add(renewCount);
        }

        model.MrrAnalytics.Datasets["MRR"] = mrr;
        model.NewSubscriptionsAnalytics.Datasets["New Subscriptions"] = newSubs;
        model.ChurnRateAnalytics.Datasets["Churn (Cancelled)"] = churn;
        model.RenewalTrendsAnalytics.Datasets["Renewals"] = renewals;

        var planDistribution = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.Status != null && s.Status.ToLower() == "active")
            .Where(s => !model.PlanId.HasValue || s.PlanId == model.PlanId.Value)
            .GroupBy(s => s.Plan != null ? (s.Plan.PlanName ?? "") : "Unknown")
            .Select(g => new { Plan = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        model.PlanDistributionAnalytics.Labels = planDistribution.Select(x => x.Plan).ToList();
        model.PlanDistributionAnalytics.Datasets["Active Subscriptions"] = planDistribution.Select(x => (decimal)x.Count).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> SubscriptionDetails(int id, CancellationToken ct = default)
    {
        var s = await _db.Subscriptions
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Plan)
            .FirstOrDefaultAsync(x => x.SubscriptionId == id, ct);
        if (s == null) return NotFound();

        return View(new SubscriptionDetailsViewModel
        {
            SubscriptionId = s.SubscriptionId,
            CompanyId = s.CompanyId ?? 0,
            CompanyName = s.Company != null ? s.Company.CompanyName : "",
            PlanName = s.Plan != null ? (s.Plan.PlanName ?? "") : "",
            Price = s.Plan != null ? (s.Plan.Price ?? 0m) : 0m,
            DurationMonths = s.Plan != null ? s.Plan.DurationMonths : null,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            Status = s.Status ?? "",
            StripeSubscriptionId = s.StripeSubscriptionId ?? ""
        });
    }

    [HttpGet]
    public async Task<IActionResult> SubscriptionBillingHistory(int id, CancellationToken ct = default)
    {
        var s = await _db.Subscriptions
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.SubscriptionId == id, ct);
        if (s == null) return NotFound();

        var rows = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId != null && s.CompanyId != null && p.CompanyId.Value == s.CompanyId.Value)
            .Where(p => p.ReservationId == null)
            .OrderByDescending(p => p.CreatedAt)
            .Take(300)
            .Select(p => new SubscriptionBillingHistoryRow
            {
                PaymentId = p.PaymentId,
                CreatedAtUtc = p.CreatedAt,
                PaymentMethod = p.PaymentMethod ?? "",
                Amount = p.Amount ?? 0m,
                Status = p.Status ?? "",
                StripePaymentIntentId = p.StripePaymentIntentId ?? ""
            })
            .ToListAsync(ct);

        return View(new SubscriptionBillingHistoryViewModel
        {
            SubscriptionId = s.SubscriptionId,
            CompanyId = s.CompanyId ?? 0,
            CompanyName = s.Company != null ? s.Company.CompanyName : "",
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> SubscriptionUsageReport(int id, CancellationToken ct = default)
    {
        var s = await _db.Subscriptions
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.SubscriptionId == id, ct);
        if (s == null) return NotFound();
        if (s.CompanyId == null) return NotFound();
        var companyId = s.CompanyId.Value;

        var users = await _db.Users.AsNoTracking().CountAsync(u => !u.IsDeleted && u.CompanyId == companyId, ct);
        var reservations = await _db.Reservations.AsNoTracking().CountAsync(r => r.CompanyId == companyId, ct);
        var workflows = await _db.WorkflowInstances.AsNoTracking().CountAsync(w => w.CompanyId == companyId, ct);
        var service = await _db.ServiceRequests.AsNoTracking().CountAsync(sr => sr.CompanyId == companyId, ct);
        var audits = await _db.AuditLogs.AsNoTracking().CountAsync(a => a.CompanyId == companyId, ct);

        var modules = new List<UsageModuleRow>
        {
            new() { Module = "Users", Volume = users },
            new() { Module = "Reservations", Volume = reservations },
            new() { Module = "Workflows", Volume = workflows },
            new() { Module = "Guest Services", Volume = service },
            new() { Module = "Audit Logs", Volume = audits }
        };

        return View(new SubscriptionUsageReportViewModel
        {
            SubscriptionId = s.SubscriptionId,
            CompanyId = companyId,
            CompanyName = s.Company != null ? s.Company.CompanyName : "",
            Users = users,
            Reservations = reservations,
            WorkflowInstances = workflows,
            ServiceRequests = service,
            AuditLogs = audits,
            Modules = modules
        });
    }

    [HttpGet]
    public async Task<IActionResult> SubscriptionAuditLogs(int id, CancellationToken ct = default)
    {
        var s = await _db.Subscriptions
            .AsNoTracking()
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.SubscriptionId == id, ct);
        if (s == null) return NotFound();

        var companyName = s.Company != null ? s.Company.CompanyName : "";

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.Company)
            .Include(a => a.User)
            .Where(a => a.CompanyId != null && s.CompanyId != null && a.CompanyId.Value == s.CompanyId.Value)
            .Where(a => a.TableName != null && (a.TableName.ToLower() == "subscriptions" || a.TableName.ToLower() == "subscriptionplans"))
            .OrderByDescending(a => a.ActionDate)
            .Take(400)
            .Select(a => new SubscriptionAuditLogRow
            {
                TimestampUtc = a.ActionDate,
                Company = a.Company != null ? a.Company.CompanyName : "",
                UserName = a.User != null ? a.User.FullName : "",
                Action = a.Action ?? "",
                Table = a.TableName ?? "",
                RecordId = a.RecordId,
                IpAddress = a.IPAddress ?? ""
            })
            .ToListAsync(ct);

        return View(new SubscriptionAuditLogsViewModel
        {
            SubscriptionId = s.SubscriptionId,
            CompanyId = s.CompanyId ?? 0,
            CompanyName = companyName,
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> AuditLogs(DateOnly? startDate = null, DateOnly? endDate = null, int? companyId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (end < start) (start, end) = (end, start);

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var companies = await _db.Companies
            .AsNoTracking()
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyOptionRow { CompanyId = c.CompanyId, CompanyName = c.CompanyName })
            .ToListAsync(ct);

        var companyName = "All Companies";
        if (companyId.HasValue)
            companyName = companies.FirstOrDefault(c => c.CompanyId == companyId.Value)?.CompanyName ?? "Selected Company";

        var baseQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.Company)
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl);
        if (companyId.HasValue)
            baseQ = baseQ.Where(a => a.CompanyId != null && a.CompanyId.Value == companyId.Value);

        var totalRows = await baseQ.CountAsync(ct);
        var rows = await baseQ
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogMonitoringRow
            {
                AuditId = a.AuditId,
                UserName = a.User != null ? a.User.FullName : "",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Module = NormalizeModule(a.TableName ?? ""),
                ActionPerformed = a.Action ?? "",
                IpAddress = a.IPAddress ?? "",
                DeviceBrowser = a.UserAgent ?? "",
                Status = (a.Action ?? "").ToLower().Contains("fail") || (a.Action ?? "").ToLower().Contains("error") ? "Failed" : "OK",
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                TimestampUtc = a.ActionDate
            })
            .ToListAsync(ct);

        var todayStartUtc = AsUtcStart(today);
        var todayEndUtcExcl = AsUtcEndExclusive(today);
        var totalToday = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionDate >= todayStartUtc && a.ActionDate < todayEndUtcExcl)
            .Where(a => !companyId.HasValue || (a.CompanyId != null && a.CompanyId.Value == companyId.Value))
            .CountAsync(ct);

        var failed = rows.Count(r => string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var loginAttempts = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("login"));
        var securityAlerts = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("unauthorized") || (r.ActionPerformed ?? "").ToLower().Contains("forbidden") || (r.ActionPerformed ?? "").ToLower().Contains("suspicious"));
        var workflowErrors = rows.Count(r => (r.Module ?? "").ToLower().Contains("workflow") && (r.ActionPerformed ?? "").ToLower().Contains("error"));
        var critical = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("critical") || (r.ActionPerformed ?? "").ToLower().Contains("role") || (r.ActionPerformed ?? "").ToLower().Contains("delete"));

        var moduleCounts = rows
            .GroupBy(r => r.Module)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var model = new AuditLogsViewModel
        {
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            CompanyId = companyId,
            CompanyName = companyName,
            Companies = companies,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,

            TotalLogsToday = totalToday,
            FailedActivities = failed,
            SecurityAlerts = securityAlerts,
            LoginAttempts = loginAttempts,
            WorkflowErrors = workflowErrors,
            CriticalEvents = critical,

            Rows = rows,

            Security = new AuditSecurityMonitoringSummary
            {
                FailedLoginAttempts = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("failed login") || ((r.ActionPerformed ?? "").ToLower().Contains("login") && string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase))),
                UnauthorizedAccessAttempts = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("unauthorized") || (r.ActionPerformed ?? "").ToLower().Contains("forbidden")),
                SuspiciousTransactions = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("suspicious") || (r.ActionPerformed ?? "").ToLower().Contains("fraud")),
                MultipleLoginLocations = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("multiple location") || (r.ActionPerformed ?? "").ToLower().Contains("ip change")),
                AccountLockouts = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("lockout") || (r.ActionPerformed ?? "").ToLower().Contains("locked"))
            },
            ModuleActivity = new AuditModuleActivitySummary
            {
                Reservations = moduleCounts.TryGetValue("Reservations", out var rv) ? rv : 0,
                GuestServices = moduleCounts.TryGetValue("Guest Services", out var gs) ? gs : 0,
                WorkflowManagement = moduleCounts.TryGetValue("Workflow Management", out var wf) ? wf : 0,
                Accounting = moduleCounts.TryGetValue("Accounting", out var ac) ? ac : 0,
                SubscriptionManagement = moduleCounts.TryGetValue("Subscription Management", out var sm) ? sm : 0,
                UserManagement = moduleCounts.TryGetValue("User Management", out var um) ? um : 0
            },
            Alerts = new AuditCriticalAlertsSummary
            {
                FraudIndicators = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("fraud") || (r.ActionPerformed ?? "").ToLower().Contains("suspicious")),
                DataModificationAttempts = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("update") || (r.ActionPerformed ?? "").ToLower().Contains("delete") || (r.ActionPerformed ?? "").ToLower().Contains("edit")),
                FailedPaymentSpikes = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("payment") && string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase)),
                UnauthorizedRoleChanges = rows.Count(r => (r.ActionPerformed ?? "").ToLower().Contains("role") && ((r.ActionPerformed ?? "").ToLower().Contains("unauthorized") || string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase))),
                RepeatedWorkflowFailures = rows.Count(r => (r.Module ?? "").ToLower().Contains("workflow") && string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            }
        };

        await PopulateAuditChartsAsync(model, ct);
        return View(model);
    }

    private static string NormalizeModule(string tableName)
    {
        var t = (tableName ?? "").Trim().ToLowerInvariant();
        if (t.Contains("reservation")) return "Reservations";
        if (t.Contains("service")) return "Guest Services";
        if (t.Contains("workflow")) return "Workflow Management";
        if (t.Contains("payment") || t.Contains("transaction") || t.Contains("account")) return "Accounting";
        if (t.Contains("subscription") || t.Contains("plan")) return "Subscription Management";
        if (t.Contains("user") || t.Contains("role") || t.Contains("permission")) return "User Management";
        return "Other";
    }

    private async Task PopulateAuditChartsAsync(AuditLogsViewModel model, CancellationToken ct)
    {
        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var start = model.StartDate;
        var end = model.EndDate;
        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var q = _db.AuditLogs.AsNoTracking().Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl);
        if (model.CompanyId.HasValue)
            q = q.Where(a => a.CompanyId != null && a.CompanyId.Value == model.CompanyId.Value);

        var raw = await q
            .Select(a => new { a.AuditId, a.Action, a.TableName, a.ActionDate })
            .ToListAsync(ct);

        var labels = new List<string>();
        var volume = new List<decimal>();
        var incidents = new List<decimal>();
        var errors = new List<decimal>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            labels.Add(d.ToString("MMM dd"));
            var dayStart = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var dayEnd = DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

            var day = raw.Where(x => x.ActionDate >= dayStart && x.ActionDate < dayEnd).ToList();
            volume.Add(day.Count);
            incidents.Add(day.Count(x => ((x.Action ?? "").ToLower().Contains("unauthorized") || (x.Action ?? "").ToLower().Contains("forbidden") || (x.Action ?? "").ToLower().Contains("failed login"))));
            errors.Add(day.Count(x => ((x.Action ?? "").ToLower().Contains("fail") || (x.Action ?? "").ToLower().Contains("error"))));
        }

        model.DailyLogVolumeAnalytics.Labels = labels;
        model.DailyLogVolumeAnalytics.Datasets["Log Volume"] = volume;

        model.SecurityIncidentsAnalytics.Labels = labels.ToList();
        model.SecurityIncidentsAnalytics.Datasets["Security Incidents"] = incidents;

        model.ErrorFrequencyAnalytics.Labels = labels.ToList();
        model.ErrorFrequencyAnalytics.Datasets["Errors"] = errors;

        var monthly = raw
            .GroupBy(x => new { x.ActionDate.Year, x.ActionDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToList();

        model.MonthlyActivityTrendsAnalytics.Labels = monthly.Select(x => new DateOnly(x.Year, x.Month, 1).ToString("yyyy MMM", CultureInfo.InvariantCulture)).ToList();
        model.MonthlyActivityTrendsAnalytics.Datasets["Activity"] = monthly.Select(x => (decimal)x.Count).ToList();

        var module = raw
            .GroupBy(x => NormalizeModule(x.TableName ?? ""))
            .Select(g => new { Module = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        model.ModuleDistributionAnalytics.Labels = module.Select(x => x.Module).ToList();
        model.ModuleDistributionAnalytics.Datasets["Events"] = module.Select(x => (decimal)x.Count).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> AuditLogDetails(int id, CancellationToken ct = default)
    {
        var a = await _db.AuditLogs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.User)
            .ThenInclude(u => u!.Role)
            .FirstOrDefaultAsync(x => x.AuditId == id, ct);
        if (a == null) return NotFound();

        return View(new AuditLogDetailsViewModel
        {
            AuditId = a.AuditId,
            TimestampUtc = a.ActionDate,
            CompanyName = a.Company != null ? a.Company.CompanyName : "",
            UserName = a.User != null ? a.User.FullName : "",
            UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
            Module = NormalizeModule(a.TableName ?? ""),
            Action = a.Action ?? "",
            TableName = a.TableName ?? "",
            RecordId = a.RecordId,
            IpAddress = a.IPAddress ?? "",
            UserAgent = a.UserAgent ?? "",
            OldValues = a.OldValues ?? "",
            NewValues = a.NewValues ?? ""
        });
    }

    [HttpGet]
    public async Task<IActionResult> AuditUserActivityHistory(int id, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == id, ct);
        if (user == null) return NotFound();

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.Company)
            .Where(a => a.UserId != null && a.UserId.Value == id)
            .OrderByDescending(a => a.ActionDate)
            .Take(1200)
            .Select(a => new AuditLogMonitoringRow
            {
                AuditId = a.AuditId,
                UserName = user.FullName,
                UserRole = user.Role != null ? user.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Module = a.TableName ?? "",
                ActionPerformed = a.Action ?? "",
                IpAddress = a.IPAddress ?? "",
                DeviceBrowser = a.UserAgent ?? "",
                Status = (a.Action ?? "").ToLower().Contains("fail") || (a.Action ?? "").ToLower().Contains("error") ? "Failed" : "OK",
                TimestampUtc = a.ActionDate
            })
            .ToListAsync(ct);

        return View(new AuditUserActivityHistoryViewModel
        {
            UserId = user.UserId,
            UserName = user.FullName,
            UserRole = user.Role != null ? user.Role.RoleName : "",
            Rows = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> AuditModuleActivityHistory(string module, int? companyId = null, CancellationToken ct = default)
    {
        module = (module ?? "").Trim();
        if (string.IsNullOrWhiteSpace(module)) return NotFound();

        var companyName = companyId.HasValue
            ? await _db.Companies.AsNoTracking().Where(c => c.CompanyId == companyId.Value).Select(c => c.CompanyName).FirstOrDefaultAsync(ct) ?? ""
            : "All Companies";

        var rowsQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.Company)
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Where(a => NormalizeModule(a.TableName ?? "") == module);
        if (companyId.HasValue)
            rowsQ = rowsQ.Where(a => a.CompanyId != null && a.CompanyId.Value == companyId.Value);

        var rows = await rowsQ
            .OrderByDescending(a => a.ActionDate)
            .Take(1500)
            .Select(a => new AuditLogMonitoringRow
            {
                AuditId = a.AuditId,
                UserName = a.User != null ? a.User.FullName : "",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Module = NormalizeModule(a.TableName ?? ""),
                ActionPerformed = a.Action ?? "",
                IpAddress = a.IPAddress ?? "",
                DeviceBrowser = a.UserAgent ?? "",
                Status = (a.Action ?? "").ToLower().Contains("fail") || (a.Action ?? "").ToLower().Contains("error") ? "Failed" : "OK",
                TimestampUtc = a.ActionDate
            })
            .ToListAsync(ct);

        return View(new AuditModuleActivityHistoryViewModel
        {
            Module = module,
            CompanyName = companyName,
            Rows = rows
        });
    }

    [HttpGet]
    [HttpGet]
    public async Task<IActionResult> AuditSecurityIncidentDetails(string type, int? companyId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        type = (type ?? "").Trim();
        if (string.IsNullOrWhiteSpace(type)) return NotFound();

        var companyName = companyId.HasValue
            ? await _db.Companies.AsNoTracking().Where(c => c.CompanyId == companyId.Value).Select(c => c.CompanyName).FirstOrDefaultAsync(ct) ?? ""
            : "All Companies";

        var q = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.Company)
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Where(a => a.Action != null);
        if (companyId.HasValue)
            q = q.Where(a => a.CompanyId != null && a.CompanyId.Value == companyId.Value);

        if (string.Equals(type, "failed-login", StringComparison.OrdinalIgnoreCase))
            q = q.Where(a => a.Action!.ToLower().Contains("failed login") || (a.Action!.ToLower().Contains("login") && (a.Action!.ToLower().Contains("fail") || a.Action!.ToLower().Contains("error"))));
        else if (string.Equals(type, "unauthorized", StringComparison.OrdinalIgnoreCase))
            q = q.Where(a => a.Action!.ToLower().Contains("unauthorized") || a.Action!.ToLower().Contains("forbidden"));
        else if (string.Equals(type, "suspicious", StringComparison.OrdinalIgnoreCase))
            q = q.Where(a => a.Action!.ToLower().Contains("suspicious") || a.Action!.ToLower().Contains("fraud"));
        else
            q = q.Where(a => a.Action!.ToLower().Contains(type.ToLower()));

        var totalRows = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogMonitoringRow
            {
                AuditId = a.AuditId,
                UserName = a.User != null ? a.User.FullName : "",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "",
                Company = a.Company != null ? a.Company.CompanyName : "",
                Module = NormalizeModule(a.TableName ?? ""),
                ActionPerformed = a.Action ?? "",
                IpAddress = a.IPAddress ?? "",
                DeviceBrowser = a.UserAgent ?? "",
                Status = (a.Action ?? "").ToLower().Contains("fail") || (a.Action ?? "").ToLower().Contains("error") ? "Failed" : "OK",
                TimestampUtc = a.ActionDate
            })
            .ToListAsync(ct);

        return View(new AuditSecurityIncidentDetailsViewModel
        {
            IncidentType = type,
            CompanyName = companyName,
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        });
    }

    [HttpGet]
    public async Task<IActionResult> Reports(DateOnly? startDate = null, DateOnly? endDate = null, int? companyId = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (end < start) (start, end) = (end, start);

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var companies = await _db.Companies
            .AsNoTracking()
            .OrderBy(c => c.CompanyName)
            .Select(c => new CompanyOptionRow { CompanyId = c.CompanyId, CompanyName = c.CompanyName })
            .ToListAsync(ct);

        var companyName = "All Companies";
        if (companyId.HasValue)
            companyName = companies.FirstOrDefault(c => c.CompanyId == companyId.Value)?.CompanyName ?? "Selected Company";

        var payQ = _db.Payments.AsNoTracking().Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl);
        if (companyId.HasValue)
            payQ = payQ.Where(p => p.CompanyId != null && p.CompanyId.Value == companyId.Value);

        var totalRevenue = await payQ.Where(p => p.Status != null && p.Status.ToLower() == "succeeded").SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var refundTotal = await payQ.Where(p => p.Status != null && p.Status.ToLower().Contains("refund")).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var rsvQ = _db.Reservations.AsNoTracking().Where(r => r.CheckInDate != null);
        if (companyId.HasValue)
            rsvQ = rsvQ.Where(r => r.CompanyId == companyId.Value);
        var reservations = await rsvQ.CountAsync(ct);

        var activeSubsQ = _db.Subscriptions.AsNoTracking().Where(s => s.Status != null && s.Status.ToLower() == "active");
        if (companyId.HasValue)
            activeSubsQ = activeSubsQ.Where(s => s.CompanyId == companyId.Value);
        var activeSubs = await activeSubsQ.CountAsync(ct);

        var wfQ = _db.WorkflowInstances.AsNoTracking().Where(w => w.CreatedAt >= startUtc && w.CreatedAt < endUtcExcl);
        if (companyId.HasValue)
            wfQ = wfQ.Where(w => w.CompanyId == companyId.Value);
        var wfTotal = await wfQ.CountAsync(ct);
        var wfCompleted = await wfQ.CountAsync(w => w.Status != null && w.Status.ToLower() == "completed", ct);
        var wfCompletionRate = wfTotal > 0 ? (decimal)wfCompleted / wfTotal : 0m;

        var auditQ = _db.AuditLogs.AsNoTracking().Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl);
        if (companyId.HasValue)
            auditQ = auditQ.Where(a => a.CompanyId != null && a.CompanyId.Value == companyId.Value);
        var totalAudit = await auditQ.CountAsync(ct);
        var securityIncidents = await auditQ.CountAsync(a => a.Action != null && (a.Action.ToLower().Contains("unauthorized") || a.Action.ToLower().Contains("forbidden") || a.Action.ToLower().Contains("failed login")), ct);
        var complianceScore = totalAudit > 0 ? Math.Max(0m, 1m - ((decimal)securityIncidents / totalAudit)) : 1m;

        var serviceQ = _db.ServiceRequests.AsNoTracking().Where(sr => sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl);
        if (companyId.HasValue)
            serviceQ = serviceQ.Where(sr => sr.CompanyId == companyId.Value);
        var serviceTotal = await serviceQ.CountAsync(ct);
        var serviceCompleted = await serviceQ.CountAsync(sr => sr.Status != null && sr.Status.ToLower() == "completed", ct);
        var guestSatisfaction = serviceTotal > 0 ? (decimal)serviceCompleted / serviceTotal : 0.92m;

        var model = new ReportsAnalyticsViewModel
        {
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            CompanyId = companyId,
            CompanyName = companyName,
            Companies = companies,
            TotalRevenue = totalRevenue,
            TotalReservations = reservations,
            GuestSatisfactionRate = guestSatisfaction,
            ActiveSubscriptions = activeSubs,
            WorkflowCompletionRate = wfCompletionRate,
            ComplianceScore = complianceScore
        };

        await PopulateReportsChartsAsync(model, start, end, companyId, ct);
        await PopulateCompanyRankingAsync(model, startUtc, endUtcExcl, ct);
        PopulateForecast(model, startUtc, endUtcExcl);

        return View(model);
    }

    private async Task PopulateReportsChartsAsync(ReportsAnalyticsViewModel model, DateOnly start, DateOnly end, int? companyId, CancellationToken ct)
    {
        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var labels = new List<string>();
        var revenue = new List<decimal>();
        var refunds = new List<decimal>();
        var newSubs = new List<decimal>();
        var churn = new List<decimal>();
        var wf = new List<decimal>();
        var compliance = new List<decimal>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var ds = AsUtcStart(d);
            var de = AsUtcEndExclusive(d);
            labels.Add(d.ToString("MMM dd"));

            var pQ = _db.Payments.AsNoTracking().Where(p => p.CreatedAt >= ds && p.CreatedAt < de);
            if (companyId.HasValue)
                pQ = pQ.Where(p => p.CompanyId != null && p.CompanyId.Value == companyId.Value);
            revenue.Add(await pQ.Where(p => p.Status != null && p.Status.ToLower() == "succeeded").SumAsync(p => (decimal?)p.Amount, ct) ?? 0m);
            refunds.Add(await pQ.Where(p => p.Status != null && p.Status.ToLower().Contains("refund")).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m);

            var sQ = _db.Subscriptions.AsNoTracking().Where(s => s.StartDate != null);
            if (companyId.HasValue)
                sQ = sQ.Where(s => s.CompanyId == companyId.Value);
            newSubs.Add(await sQ.Where(s => s.StartDate!.Value == d).CountAsync(ct));
            churn.Add(await sQ.Where(s => s.Status != null && s.Status.ToLower() == "cancelled" && s.EndDate != null && s.EndDate.Value == d).CountAsync(ct));

            var wQ = _db.WorkflowInstances.AsNoTracking().Where(w => w.CreatedAt >= ds && w.CreatedAt < de);
            if (companyId.HasValue)
                wQ = wQ.Where(w => w.CompanyId == companyId.Value);
            wf.Add(await wQ.CountAsync(ct));

            var aQ = _db.AuditLogs.AsNoTracking().Where(a => a.ActionDate >= ds && a.ActionDate < de);
            if (companyId.HasValue)
                aQ = aQ.Where(a => a.CompanyId != null && a.CompanyId.Value == companyId.Value);
            var t = await aQ.CountAsync(ct);
            var sec = await aQ.CountAsync(a => a.Action != null && (a.Action.ToLower().Contains("unauthorized") || a.Action.ToLower().Contains("forbidden") || a.Action.ToLower().Contains("failed login")), ct);
            compliance.Add(t > 0 ? Math.Max(0m, 1m - ((decimal)sec / t)) : 1m);
        }

        model.RevenueTrends.Labels = labels;
        model.RevenueTrends.Datasets["Revenue"] = revenue;
        model.RefundTrends.Labels = labels.ToList();
        model.RefundTrends.Datasets["Refunds"] = refunds;

        model.SubscriptionTrends.Labels = labels.ToList();
        model.SubscriptionTrends.Datasets["New Subscriptions"] = newSubs;
        model.SubscriptionChurnTrends.Labels = labels.ToList();
        model.SubscriptionChurnTrends.Datasets["Churn"] = churn;
        model.WorkflowTrends.Labels = labels.ToList();
        model.WorkflowTrends.Datasets["Workflow Volume"] = wf;
        model.ComplianceTrends.Labels = labels.ToList();
        model.ComplianceTrends.Datasets["Compliance"] = compliance;

        var planDistQ = _db.Subscriptions.AsNoTracking().Include(s => s.Plan).Where(s => s.Status != null && s.Status.ToLower() == "active");
        if (companyId.HasValue)
            planDistQ = planDistQ.Where(s => s.CompanyId == companyId.Value);
        var planDist = await planDistQ
            .GroupBy(s => s.Plan != null ? (s.Plan.PlanName ?? "") : "Unknown")
            .Select(g => new { Plan = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);
        model.PlanDistribution.Labels = planDist.Select(x => x.Plan).ToList();
        model.PlanDistribution.Datasets["Active"] = planDist.Select(x => (decimal)x.Count).ToList();

        var auditQ = _db.AuditLogs.AsNoTracking().Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl);
        if (companyId.HasValue)
            auditQ = auditQ.Where(a => a.CompanyId != null && a.CompanyId.Value == companyId.Value);
        var module = await auditQ
            .Select(a => a.TableName)
            .ToListAsync(ct);
        var modGroups = module
            .GroupBy(t => NormalizeModule(t ?? ""))
            .Select(g => new { Module = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        model.ModuleActivityDistribution.Labels = modGroups.Select(x => x.Module).ToList();
        model.ModuleActivityDistribution.Datasets["Events"] = modGroups.Select(x => (decimal)x.Count).ToList();
    }

    private async Task PopulateCompanyRankingAsync(ReportsAnalyticsViewModel model, DateTime startUtc, DateTime endUtcExcl, CancellationToken ct)
    {
        var revenues = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .Where(p => p.CompanyId != null)
            .Where(p => p.Status != null && p.Status.ToLower() == "succeeded")
            .GroupBy(p => p.CompanyId!.Value)
            .Select(g => new { CompanyId = g.Key, Revenue = g.Sum(x => (decimal?)x.Amount) ?? 0m })
            .ToListAsync(ct);

        var rsv = await _db.Reservations
            .AsNoTracking()
            .GroupBy(r => r.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var wf = await _db.WorkflowInstances
            .AsNoTracking()
            .GroupBy(w => w.CompanyId)
            .Select(g => new { CompanyId = g.Key ?? 0, Total = g.Count(), Completed = g.Count(x => x.Status != null && x.Status.ToLower() == "completed") })
            .ToListAsync(ct);

        var subs = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.CompanyId != null)
            .GroupBy(s => s.CompanyId!.Value)
            .Select(g => new { CompanyId = g.Key, Status = g.OrderByDescending(x => x.SubscriptionId).Select(x => x.Status).FirstOrDefault() })
            .ToListAsync(ct);

        var companyNames = await _db.Companies.AsNoTracking().Select(c => new { c.CompanyId, c.CompanyName }).ToListAsync(ct);

        var revMap = revenues.ToDictionary(x => x.CompanyId, x => x.Revenue);
        var rsvMap = rsv.ToDictionary(x => x.CompanyId, x => x.Count);
        var wfMap = wf.ToDictionary(x => x.CompanyId, x => x.Total > 0 ? (decimal)x.Completed / x.Total : 0m);
        var subMap = subs.ToDictionary(x => x.CompanyId, x => x.Status ?? "");

        model.CompanyRankings = companyNames
            .Select(c => new CompanyPerformanceRankRow
            {
                CompanyId = c.CompanyId,
                CompanyName = c.CompanyName,
                Revenue = revMap.TryGetValue(c.CompanyId, out var rr) ? rr : 0m,
                Reservations = rsvMap.TryGetValue(c.CompanyId, out var rc) ? rc : 0,
                GuestRating = 4.6m,
                WorkflowEfficiency = wfMap.TryGetValue(c.CompanyId, out var we) ? we : 0m,
                SubscriptionStatus = subMap.TryGetValue(c.CompanyId, out var ss) ? ss : "",
                GrowthRate = 0m
            })
            .OrderByDescending(x => x.Revenue)
            .Take(30)
            .ToList();
    }

    private void PopulateForecast(ReportsAnalyticsViewModel model, DateTime startUtc, DateTime endUtcExcl)
    {
        var days = Math.Max(1, (endUtcExcl - startUtc).TotalDays);
        var monthlyFactor = 30m / (decimal)days;
        model.Forecast.ExpectedMonthlyRevenue = model.TotalRevenue * monthlyFactor;
        model.Forecast.ReservationDemandForecast = model.TotalReservations * monthlyFactor;
        model.Forecast.SubscriptionRenewalForecast = model.ActiveSubscriptions * 0.12m;
        model.Forecast.OperationalGrowthTrend = 0.08m;
    }

    [HttpGet]
    public IActionResult Settings() => RedirectToAction(nameof(SystemSettings));

    private IActionResult ExportCsv(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));
        foreach (var r in rows)
            sb.AppendLine(string.Join(',', r.Select(EscapeCsv)));

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", FileName(title, "csv"));
    }

    private IActionResult ExportExcel(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");

        for (var i = 0; i < headers.Count; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Range(1, 1, 1, headers.Count).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");

        var rowIndex = 2;
        foreach (var r in rows)
        {
            for (var c = 0; c < headers.Count && c < r.Length; c++)
                ws.Cell(rowIndex, c + 1).Value = r[c];
            rowIndex++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileName(title, "xlsx"));
    }

    private static string EscapeCsv(string input)
    {
        var s = input ?? string.Empty;
        if (s.Contains('"'))
            s = s.Replace("\"", "\"\"");
        if (s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"'))
            s = $"\"{s}\"";
        return s;
    }

    private static string FileName(string title, string ext)
    {
        var safe = new string((title ?? "Export").Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_').ToArray()).Trim();
        safe = string.Join('-', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{safe}-{ViaReservaERP.AppTime.Now:yyyyMMdd-HHmm}.{ext}";
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);

        ViewData["Title"] = "My Profile";
        ViewData["UserEmail"] = user?.Email;
        ViewData["UserPhone"] = user?.Phone;
        ViewData["AvatarUrl"] = user?.AvatarUrl;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAvatar(IFormFile avatar, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        if (avatar == null || avatar.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a valid image file.";
            return RedirectToAction(nameof(Profile));
        }

        // Validate extension
        var ext = Path.GetExtension(avatar.FileName).ToLower();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowed.Contains(ext))
        {
            TempData["ErrorMessage"] = "Only JPG, PNG and WEBP images are allowed.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        if (user == null) return NotFound();

        // Create directory
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        // Generate unique name
        var fileName = $"avatar_{userId}_{DateTime.Now.Ticks}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        // Save file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await avatar.CopyToAsync(stream, ct);
        }

        // Delete old avatar if exists
        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            var oldPath = Path.Combine(_env.WebRootPath, user.AvatarUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath))
            {
                try { System.IO.File.Delete(oldPath); } catch { /* ignore */ }
            }
        }

        user.AvatarUrl = $"/uploads/avatars/{fileName}";
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Avatar updated successfully.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(string fullName, string email, string phone, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        if (user == null) return NotFound();

        var emailExists = await _db.Users.AnyAsync(u => u.Email == email && u.UserId != user.UserId, ct);
        if (emailExists)
        {
            TempData["ErrorMessage"] = "Email address is already in use by another account.";
            return RedirectToAction(nameof(Profile));
        }

        user.FullName = fullName;
        user.Email = email;
        user.Phone = phone;

        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult AccountSettings()
    {
        ViewData["Title"] = "Account Settings";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePassword(string currentPassword, string newPassword, string confirmNewPassword, CancellationToken ct = default)
    {
        if (newPassword != confirmNewPassword)
        {
            TempData["ErrorMessage"] = "New passwords do not match.";
            return RedirectToAction(nameof(AccountSettings));
        }

        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        if (user == null) return NotFound();

        if (!ViaReservaERP.Security.PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            TempData["ErrorMessage"] = "Incorrect current password.";
            return RedirectToAction(nameof(AccountSettings));
        }

        user.PasswordHash = ViaReservaERP.Security.PasswordHasher.Hash(newPassword);
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Password successfully updated.";
        return RedirectToAction(nameof(AccountSettings));
    }

    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        var items = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var model = new SuperAdminNotificationsViewModel
        {
            Items = items,
            UnreadCount = items.Count(n => !n.IsRead)
        };

        ViewData["Title"] = "Notifications";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllNotificationsAsRead(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        var unread = await _db.Notifications
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread)
        {
            n.IsRead = true;
        }

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Notifications));
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsUnreadCount(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        var unread = await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId.Value && !n.IsRead, ct);

        return Json(new { unread });
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsListPartial(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (!userId.HasValue) return Forbid();

        var items = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var model = new SuperAdminNotificationsViewModel
        {
            Items = items,
            UnreadCount = items.Count(n => !n.IsRead)
        };

        return PartialView("_NotificationsList", model);
    }
}
