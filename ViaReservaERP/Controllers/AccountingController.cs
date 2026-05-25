using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Admin;
using ViaReservaERP.Models.Accounting;
using ViaReservaERP.Security;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.Accountant)]
public class AccountingController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AccountingController(ViaReservaDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private int CurrentCompanyId => User.GetCompanyId() ?? 0;
    private int CurrentUserId => User.GetUserId() ?? 0;

    [HttpGet]
    public async Task<IActionResult> Dashboard(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? year = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var now = ViaReservaERP.AppTime.Now;
        var today = DateOnly.FromDateTime(now);

        if (year.HasValue)
        {
            startDate = new DateOnly(year.Value, 1, 1);
            endDate = new DateOnly(year.Value, 12, 31);
        }

        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month") granularity = "month";

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
            start = new DateOnly(today.Year, today.Month, 1);
            end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        }

        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtcExcl = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        // Comparison Range (Previous period of same length)
        var days = end.DayNumber - start.DayNumber;
        var compareEnd = start.AddDays(-1);
        var compareStart = compareEnd.AddDays(-days);
        var cStartUtc = DateTime.SpecifyKind(compareStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var cEndUtcExcl = DateTime.SpecifyKind(compareEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 10 : pageSize;

        var totalRows = await _db.Transactions.CountAsync(t => t.CompanyId == companyId, ct);
        var transactions = await _db.Transactions
            .Where(t => t.CompanyId == companyId)
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var payments = await _db.Payments
            .Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        // Current Period Metrics
        var totalRev = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .SumAsync(p => p.Amount ?? 0m, ct);

        var serviceRevenue = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl)
            .SumAsync(t => t.Amount ?? 0m, ct);

        var pending = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status != "Succeeded" && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .SumAsync(p => p.Amount ?? 0m, ct);

        var expense = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl)
            .SumAsync(t => t.Amount ?? 0m, ct);

        var tax = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl)
            .SumAsync(t => t.TaxAmount ?? 0m, ct);

        var sc = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl)
            .SumAsync(t => t.ServiceCharge ?? 0m, ct);

        // Comparison Metrics
        var pTotalRev = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= cStartUtc && p.CreatedAt < cEndUtcExcl)
            .SumAsync(p => p.Amount ?? 0m, ct);
        
        var pServiceRev = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= cStartUtc && t.TransactionDate < cEndUtcExcl)
            .SumAsync(t => t.Amount ?? 0m, ct);

        var pTax = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= cStartUtc && t.TransactionDate < cEndUtcExcl)
            .SumAsync(t => t.TaxAmount ?? 0m, ct);

        var pSc = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= cStartUtc && t.TransactionDate < cEndUtcExcl)
            .SumAsync(t => t.ServiceCharge ?? 0m, ct);

        var pPending = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status != "Succeeded" && p.CreatedAt >= cStartUtc && p.CreatedAt < cEndUtcExcl)
            .SumAsync(p => p.Amount ?? 0m, ct);

        var pExpense = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= cStartUtc && t.TransactionDate < cEndUtcExcl)
            .SumAsync(t => t.Amount ?? 0m, ct);

        static double? PctDelta(decimal cur, decimal prev) => prev == 0 ? (cur > 0 ? 100 : (double?)null) : (double)((cur - prev) / prev * 100);

        var model = new AccountingDashboardViewModel
        {
            Granularity = granularity,
            SelectedYear = year,
            StartDate = start,
            EndDate = end,
            SelectedRangeLabel = $"{start:MMM dd} - {end:MMM dd, yyyy}",
            TotalRevenue = totalRev,
            TotalRevenueDeltaPct = PctDelta(totalRev, pTotalRev),
            ServiceRevenue = serviceRevenue,
            ServiceRevenueDeltaPct = PctDelta(serviceRevenue, pServiceRev),
            TotalTax = tax,
            TotalTaxDeltaPct = PctDelta(tax, pTax),
            TotalServiceCharge = sc,
            TotalServiceChargeDeltaPct = PctDelta(sc, pSc),
            PendingPayments = pending,
            PendingPaymentsDeltaPct = PctDelta(pending, pPending),
            NetProfit = (totalRev + serviceRevenue) - expense,
            NetProfitDeltaPct = PctDelta((totalRev + serviceRevenue) - expense, (pTotalRev + pServiceRev) - pExpense),
            RecentTransactions = transactions,
            RecentPayments = payments,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        // --- TREND ANALYTICS ---
        var labels = new List<string>();
        var revData = new List<decimal>();
        var incomeData = new List<decimal>();
        var expenseData = new List<decimal>();
        var profitData = new List<decimal>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                var dUtc = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var dUtcExcl = dUtc.AddDays(1);
                
                var r = await _db.Payments.Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= dUtc && p.CreatedAt < dUtcExcl).SumAsync(p => p.Amount ?? 0m, ct);
                var i = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= dUtc && t.TransactionDate < dUtcExcl).SumAsync(t => t.Amount ?? 0m, ct);
                var e = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= dUtc && t.TransactionDate < dUtcExcl).SumAsync(t => t.Amount ?? 0m, ct);
                
                revData.Add(r);
                incomeData.Add(i);
                expenseData.Add(e);
                profitData.Add((r + i) - e);
            }
        }
        else if (granularity == "year")
        {
            for (var i = 4; i >= 0; i--)
            {
                var targetYear = today.Year - i;
                labels.Add(targetYear.ToString());
                var yStart = new DateOnly(targetYear, 1, 1);
                var yEnd = new DateOnly(targetYear, 12, 31);
                var ysUtc = DateTime.SpecifyKind(yStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var yeUtcExcl = DateTime.SpecifyKind(yEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

                var r = await _db.Payments.Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= ysUtc && p.CreatedAt < yeUtcExcl).SumAsync(p => p.Amount ?? 0m, ct);
                var inc = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= ysUtc && t.TransactionDate < yeUtcExcl).SumAsync(t => t.Amount ?? 0m, ct);
                var exp = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= ysUtc && t.TransactionDate < yeUtcExcl).SumAsync(t => t.Amount ?? 0m, ct);

                revData.Add(r);
                incomeData.Add(inc);
                expenseData.Add(exp);
                profitData.Add((r + inc) - exp);
            }
        }
        else // month
        {
            for (var m = 0; m < 6; m++)
            {
                var target = today.AddMonths(-5 + m);
                var mStart = new DateOnly(target.Year, target.Month, 1);
                var mEnd = mStart.AddMonths(1);
                labels.Add(mStart.ToString("MMM yyyy"));

                var msUtc = DateTime.SpecifyKind(mStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var meUtc = DateTime.SpecifyKind(mEnd.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

                var r = await _db.Payments.Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= msUtc && p.CreatedAt < meUtc).SumAsync(p => p.Amount ?? 0m, ct);
                var i = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= msUtc && t.TransactionDate < meUtc).SumAsync(t => t.Amount ?? 0m, ct);
                var e = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= msUtc && t.TransactionDate < meUtc).SumAsync(t => t.Amount ?? 0m, ct);

                revData.Add(r);
                incomeData.Add(i);
                expenseData.Add(e);
                profitData.Add((r + i) - e);
            }
        }

        model.RevenueAnalytics.Labels = labels;
        model.RevenueAnalytics.Datasets["Revenue"] = revData;

        model.FinancialAnalytics.Labels = labels;
        model.FinancialAnalytics.Datasets["Income"] = incomeData;
        model.FinancialAnalytics.Datasets["Expenses"] = expenseData;
        model.FinancialAnalytics.Datasets["Net Profit"] = profitData;

        ViewData["Title"] = "Accounting Dashboard";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Transactions(string? type = null, DateOnly? startDate = null, DateOnly? endDate = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? today;
        if (end < start) (start, end) = (end, start);

        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;
        if (pageSize > 100) pageSize = 100;

        static DateTime AsStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsStart(start);
        var endUtcExcl = AsEndExclusive(end);

        var q = _db.Transactions
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .Where(t => t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim();
            q = q.Where(x => x.Type == t);
        }

        var totalRows = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var model = new AccountantTransactionsViewModel
        {
            FilterType = type,
            StartDate = start,
            EndDate = end,
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            New = new CreateTransactionForm
            {
                Type = string.IsNullOrWhiteSpace(type) ? "Income" : type,
                TransactionDate = today,
                Amount = 0m
            }
        };

        ViewData["Title"] = "Transactions";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTransaction(CreateTransactionForm model, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        if (model.Amount <= 0)
        {
            TempData["ErrorMessage"] = "Amount must be greater than 0.";
            return RedirectToAction(nameof(Transactions));
        }

        if (string.IsNullOrWhiteSpace(model.Type) || (model.Type != "Income" && model.Type != "Expense"))
        {
            TempData["ErrorMessage"] = "Type must be Income or Expense.";
            return RedirectToAction(nameof(Transactions));
        }

        var tx = new AccountingTransaction
        {
            CompanyId = companyId,
            Amount = model.Amount,
            Type = model.Type,
            Description = model.Description,
            ReferenceId = model.ReferenceId,
            ReferenceType = model.ReferenceType,
            TransactionDate = DateTime.SpecifyKind(model.TransactionDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Insert",
            TableName = "Transactions",
            RecordId = tx.TransactionId,
            NewValues = $"Accounting transaction created: {tx.Type} {tx.Amount:N2}",
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Transaction recorded.";
        return RedirectToAction(nameof(Transactions));
    }

    [HttpGet]
    public async Task<IActionResult> Reports(DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (end < start) (start, end) = (end, start);

        static DateTime AsStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsStart(start);
        var endUtcExcl = AsEndExclusive(end);

        var baseQ = _db.Transactions
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .Where(t => t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl);

        var income = await baseQ
            .Where(t => t.Type == "Income")
            .SumAsync(t => t.Amount.HasValue ? t.Amount.Value : 0m, ct);

        var tax = await baseQ
            .Where(t => t.Type == "Income")
            .SumAsync(t => t.TaxAmount ?? 0m, ct);

        var sc = await baseQ
            .Where(t => t.Type == "Income")
            .SumAsync(t => t.ServiceCharge ?? 0m, ct);

        var expense = await baseQ
            .Where(t => t.Type == "Expense")
            .SumAsync(t => t.Amount.HasValue ? t.Amount.Value : 0m, ct);

        var latest = await baseQ
            .OrderByDescending(t => t.TransactionDate)
            .Take(25)
            .ToListAsync(ct);

        // Revenue (from Payments)
        var revenue = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .SumAsync(p => p.Amount ?? 0m, ct);

        // Operational Volume
        var resCount = await _db.Reservations
            .Where(r => r.CompanyId == companyId && r.CheckInDate.HasValue && r.CheckInDate.Value >= start && r.CheckInDate.Value <= end)
            .CountAsync(ct);

        var serviceReqCount = await _db.ServiceRequests
            .Where(sr => sr.CompanyId == companyId && sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl)
            .CountAsync(ct);

        var model = new AccountantReportsViewModel
        {
            StartDate = start,
            EndDate = end,
            TotalIncome = income,
            TotalExpense = expense,
            TotalTax = tax,
            TotalServiceCharge = sc,
            Revenue = revenue,
            TotalReservations = resCount,
            TotalServiceRequests = serviceReqCount,
            Recent = latest
        };

        // --- TREND CALCULATIONS ---
        var dailyRevenue = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl)
            .GroupBy(p => p.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(p => p.Amount ?? 0m) })
            .ToListAsync(ct);

        var dailyReservations = await _db.Reservations
            .Where(r => r.CompanyId == companyId && r.CheckInDate >= start && r.CheckInDate <= end)
            .GroupBy(r => r.CheckInDate)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var dailyServices = await _db.ServiceRequests
            .Where(sr => sr.CompanyId == companyId && sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl)
            .GroupBy(sr => sr.RequestDate.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var labels = new List<string>();
        var revData = new List<decimal>();
        var resData = new List<decimal>();
        var svcData = new List<decimal>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            labels.Add(d.ToString("MMM dd"));
            var dt = d.ToDateTime(TimeOnly.MinValue).Date;
            revData.Add(dailyRevenue.FirstOrDefault(x => x.Date == dt)?.Total ?? 0m);
            resData.Add(dailyReservations.FirstOrDefault(x => x.Date == d)?.Count ?? 0);
            svcData.Add(dailyServices.FirstOrDefault(x => x.Date == dt)?.Count ?? 0);
        }

        model.RevenueAnalytics.Labels = labels;
        model.RevenueAnalytics.Datasets["Revenue"] = revData;

        model.ReservationAnalytics.Labels = labels;
        model.ReservationAnalytics.Datasets["Bookings"] = resData;

        model.ServiceAnalytics.Labels = labels;
        model.ServiceAnalytics.Datasets["Requests"] = svcData;

        // --- FORECASTS ---
        var daysInRange = (end.DayNumber - start.DayNumber) + 1;
        if (daysInRange > 0)
        {
            model.Forecast.ProjectedRevenue = (revenue / daysInRange) * 30;
            model.Forecast.ProjectedReservations = (int)((resCount / (decimal)daysInRange) * 30);
            model.Forecast.GrowthRate = 0.05m;
        }

        ViewData["Title"] = "Financial Reports";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> AuditLogs(string? search, DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? today;
        if (end < start) (start, end) = (end, start);

        static DateTime AsStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsStart(start);
        var endUtcExcl = AsEndExclusive(end);

        var baseQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Where(a => a.CompanyId == companyId)
            .Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl)
            .Where(a => (a.TableName ?? "").ToLower().Contains("transaction") || (a.TableName ?? "").ToLower().Contains("payment") || (a.TableName ?? "").ToLower().Contains("account") || (a.TableName ?? "").ToLower().Contains("reservation"));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQ = baseQ.Where(a => (a.User != null && a.User.FullName.Contains(term)) || (a.Action != null && a.Action.Contains(term)) || (a.TableName != null && a.TableName.Contains(term)));
        }

        var rows = await baseQ
            .OrderByDescending(a => a.ActionDate)
            .Take(200)
            .Select(a => new AdminAuditLogRow
            {
                AuditId = a.AuditId,
                UserName = a.User != null ? a.User.FullName : "System",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "Service",
                Module = a.TableName ?? "Accounting",
                Action = a.Action ?? "Action",
                IpAddress = a.IPAddress ?? "0.0.0.0",
                Status = (a.Action ?? "").ToLower().Contains("fail") || (a.Action ?? "").ToLower().Contains("error") ? "Failed" : "OK",
                Timestamp = a.ActionDate,
                OldValues = a.OldValues,
                NewValues = a.NewValues
            })
            .ToListAsync(ct);

        var model = new AccountantAuditLogsViewModel
        {
            Search = search,
            StartDate = start,
            EndDate = end,
            Rows = rows
        };

        ViewData["Title"] = "Accounting Audit Logs";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct = default)
    {
        ViewData["Title"] = "Profile Settings";
        var userId = CurrentUserId;
        var companyId = CurrentCompanyId;

        var user = await _db.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.UserId == userId && u.CompanyId == companyId, ct);

        if (user == null) return NotFound();

        ViewData["AvatarUrl"] = user.AvatarUrl;

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAvatar(IFormFile avatar, CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (userId <= 0) return Forbid();

        if (avatar == null || avatar.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a valid image file.";
            return RedirectToAction(nameof(Profile));
        }

        var ext = Path.GetExtension(avatar.FileName).ToLower();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowed.Contains(ext))
        {
            TempData["ErrorMessage"] = "Only JPG, PNG and WEBP images are allowed.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user == null) return NotFound();

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var fileName = $"avatar_{userId}_{DateTime.Now.Ticks}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await avatar.CopyToAsync(stream, ct);
        }

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

    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var items = await _db.Notifications
            .Where(n => n.CompanyId == companyId && n.UserId == userId)
            .Where(n => (n.Title ?? "").Contains("Payment") || (n.Title ?? "").Contains("Transaction") || (n.Title ?? "").Contains("Refund") ||
                       (n.Title ?? "").Contains("Invoice") || (n.Title ?? "").Contains("Bill") || (n.Title ?? "").Contains("Accounting") ||
                       (n.Title ?? "").Contains("Cancellation") || (n.Title ?? "").Contains("Extension") || (n.Title ?? "").Contains("Check-out") ||
                       (n.Message ?? "").Contains("Payment") || (n.Message ?? "").Contains("Transaction") || (n.Message ?? "").Contains("Refund") ||
                       (n.Type ?? "") == "Accounting" || (n.Type ?? "") == "Reservation")
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var model = new AdminNotificationsViewModel
        {
            Items = items,
            UnreadCount = items.Count(n => !n.IsRead)
        };

        ViewData["Title"] = "My Notifications";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsUnreadCount(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var unread = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.UserId == userId && !n.IsRead)
            .CountAsync(n => (n.Title ?? "").Contains("Payment") || (n.Title ?? "").Contains("Transaction") || (n.Title ?? "").Contains("Refund") ||
                            (n.Title ?? "").Contains("Invoice") || (n.Title ?? "").Contains("Bill") || (n.Title ?? "").Contains("Accounting") ||
                            (n.Title ?? "").Contains("Cancellation") || (n.Title ?? "").Contains("Extension") || (n.Title ?? "").Contains("Check-out") ||
                            (n.Message ?? "").Contains("Payment") || (n.Message ?? "").Contains("Transaction") || (n.Message ?? "").Contains("Refund") ||
                            (n.Type ?? "") == "Accounting" || (n.Type ?? "") == "Reservation", ct);

        return Json(new { unread });
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsListPartial(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var items = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.UserId == userId)
            .Where(n => (n.Title ?? "").Contains("Payment") || (n.Title ?? "").Contains("Transaction") || (n.Title ?? "").Contains("Refund") ||
                       (n.Title ?? "").Contains("Invoice") || (n.Title ?? "").Contains("Bill") || (n.Title ?? "").Contains("Accounting") ||
                       (n.Title ?? "").Contains("Cancellation") || (n.Title ?? "").Contains("Extension") || (n.Title ?? "").Contains("Check-out") ||
                       (n.Message ?? "").Contains("Payment") || (n.Message ?? "").Contains("Transaction") || (n.Message ?? "").Contains("Refund") ||
                       (n.Type ?? "") == "Accounting" || (n.Type ?? "") == "Reservation")
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var model = new AdminNotificationsViewModel
        {
            Items = items,
            UnreadCount = items.Count(n => !n.IsRead)
        };

        return PartialView("_NotificationsList", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var unread = await _db.Notifications
            .Where(n => n.CompanyId == companyId && n.UserId == userId && !n.IsRead)
            .Where(n => (n.Title ?? "").Contains("Payment") || (n.Title ?? "").Contains("Transaction") || (n.Title ?? "").Contains("Refund") ||
                       (n.Title ?? "").Contains("Invoice") || (n.Title ?? "").Contains("Bill") || (n.Title ?? "").Contains("Accounting") ||
                       (n.Title ?? "").Contains("Cancellation") || (n.Title ?? "").Contains("Extension") || (n.Title ?? "").Contains("Check-out") ||
                       (n.Message ?? "").Contains("Payment") || (n.Message ?? "").Contains("Transaction") || (n.Message ?? "").Contains("Refund") ||
                       (n.Type ?? "") == "Accounting" || (n.Type ?? "") == "Reservation")
            .ToListAsync(ct);

        if (unread.Any())
        {
            foreach (var n in unread)
            {
                n.IsRead = true;
            }
            await _db.SaveChangesAsync(ct);
        }

        TempData["SuccessMessage"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Notifications));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(string fullName, string? newPassword, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.CompanyId == companyId, ct);
        if (user == null) return NotFound();

        user.FullName = fullName.Trim();
        user.UpdatedAt = ViaReservaERP.AppTime.Now;

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (newPassword.Length < 6)
            {
                TempData["ErrorMessage"] = "Password must be at least 6 characters.";
                return RedirectToAction(nameof(Profile));
            }

            user.PasswordHash = PasswordHasher.Hash(newPassword);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Users",
            RecordId = userId,
            NewValues = "Profile updated",
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }
    [HttpGet]
    public async Task<IActionResult> ExportFinancials(DateOnly? startDate, DateOnly? endDate, string format = "pdf", CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (end < start) (start, end) = (end, start);

        static DateTime AsStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var startUtc = AsStart(start);
        var endUtcExcl = AsEndExclusive(end);

        var baseQ = _db.Transactions
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .Where(t => t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl);

        var income = await baseQ.Where(t => t.Type == "Income").SumAsync(t => t.Amount ?? 0m, ct);
        var tax = await baseQ.Where(t => t.Type == "Income").SumAsync(t => t.TaxAmount ?? 0m, ct);
        var sc = await baseQ.Where(t => t.Type == "Income").SumAsync(t => t.ServiceCharge ?? 0m, ct);
        var expense = await baseQ.Where(t => t.Type == "Expense").SumAsync(t => t.Amount ?? 0m, ct);
        var rows = await baseQ.OrderByDescending(t => t.TransactionDate).Take(5000).ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Period"] = $"{start:MMM dd} - {end:MMM dd, yyyy}",
            ["Income"] = $"₱ {income:N2}",
            ["VAT (12%)"] = $"₱ {tax:N2}",
            ["SC (10%)"] = $"₱ {sc:N2}",
            ["Expense"] = $"₱ {expense:N2}",
            ["Net"] = $"₱ {(income - expense):N2}"
        };

        var headers = new[] { "Date", "Type", "Description", "Ref", "VAT", "SC", "Net" };
        var data = rows.Select(t => new[]
        {
            t.TransactionDate.ToString("yyyy-MM-dd HH:mm"),
            t.Type ?? "",
            t.Description ?? "",
            t.ReferenceId != null ? $"{t.ReferenceType} #{t.ReferenceId}" : "Manual",
            t.TaxAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "0.00",
            t.ServiceCharge?.ToString("N2", CultureInfo.InvariantCulture) ?? "0.00",
            t.Amount?.ToString("N2", CultureInfo.InvariantCulture) ?? "0.00"
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Financial Report", headers, data, kpis, userName, userRole);
    }

    private IActionResult Export(string format, string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyDictionary<string, string> kpis, string userName, string userRole)
    {
        format = (format ?? "pdf").Trim().ToLowerInvariant();
        return format switch
        {
            "csv" => ExportCsv(title, headers, rows, userName, userRole),
            "xlsx" => ExportExcel(title, headers, rows, userName, userRole),
            _ => ExportPdf(title, headers, rows, kpis, userName, userRole)
        };
    }

    private IActionResult ExportCsv(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, string userName, string userRole)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\"Report: {title}\"");
        sb.AppendLine($"\"Generated By: {userName} ({userRole})\"");
        sb.AppendLine($"\"Generated At: {ViaReservaERP.AppTime.Now:yyyy-MM-dd HH:mm} UTC\"");
        sb.AppendLine();

        sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));
        foreach (var r in rows) sb.AppendLine(string.Join(',', r.Select(EscapeCsv)));
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"{title.Replace(" ", "-")}-{DateTime.Now:yyyyMMdd}.csv");
    }

    private IActionResult ExportExcel(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, string userName, string userRole)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");

        ws.Cell(1, 1).Value = $"Report: {title}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = $"Generated By: {userName} ({userRole})";
        ws.Cell(3, 1).Value = $"Generated At: {ViaReservaERP.AppTime.Now:yyyy-MM-dd HH:mm} UTC";

        var startRow = 5;
        for (var i = 0; i < headers.Count; i++) ws.Cell(startRow, i + 1).Value = headers[i];
        ws.Range(startRow, 1, startRow, headers.Count).Style.Font.Bold = true;
        ws.Range(startRow, 1, startRow, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
        var rowIndex = startRow + 1;
        foreach (var r in rows)
        {
            for (var c = 0; c < headers.Count && c < r.Length; c++) ws.Cell(rowIndex, c + 1).Value = r[c];
            rowIndex++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{title.Replace(" ", "-")}-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private IActionResult ExportPdf(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyDictionary<string, string> kpis, string userName, string userRole)
    {
        var logoPath = Path.Combine(_env.WebRootPath, "img", "viareserva-logo.png");
        byte[]? logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        if (logoBytes != null) col.Item().Height(24).Image(logoBytes);
                        col.Item().Text("ViaReservaERP").FontSize(12).SemiBold().FontColor("#1a2a6c");
                    });
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text(title).FontSize(14).SemiBold().FontColor("#1e293b");
                        col.Item().Text($"Generated By: {userName} ({userRole})").FontSize(9);
                        col.Item().Text($"Generated At: {ViaReservaERP.AppTime.Now:yyyy-MM-dd HH:mm} UTC").FontSize(9);
                    });
                });
                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Row(r =>
                    {
                        foreach (var kv in kpis)
                        {
                            r.RelativeItem().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(8).Column(c =>
                            {
                                c.Item().Text(kv.Key).FontSize(8).FontColor(Colors.Grey.Darken1);
                                c.Item().Text(kv.Value).FontSize(11).SemiBold();
                            });
                        }
                    });
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { for (int i = 0; i < headers.Count; i++) c.RelativeColumn(); });
                        table.Header(h =>
                        {
                            for (int i = 0; i < headers.Count; i++)
                                h.Cell().Background("#f1f5f9").Padding(5).Text(headers[i]).FontSize(9).SemiBold();
                        });
                        foreach (var r in rows)
                        {
                            for (int i = 0; i < headers.Count; i++)
                                table.Cell().BorderBottom(1).BorderColor("#f1f5f9").Padding(5).Text(i < r.Length ? r[i] : "").FontSize(9);
                        }
                    });
                });
            });
        });
        return File(doc.GeneratePdf(), "application/pdf", $"{title.Replace(" ", "-")}-{DateTime.Now:yyyyMMdd}.pdf");
    }

    private static string EscapeCsv(string input)
    {
        var s = input ?? string.Empty;
        if (s.Contains('"')) s = s.Replace("\"", "\"\"");
        if (s.Contains(',') || s.Contains('\n') || s.Contains('"')) s = $"\"{s}\"";
        return s;
    }
}
