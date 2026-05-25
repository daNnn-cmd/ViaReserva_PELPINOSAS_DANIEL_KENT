using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using ViaReservaERP.Data;
using ViaReservaERP.Models.SuperAdmin;
using ViaReservaERP.Security;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.SuperAdmin)]
public class SuperAdminReportsController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public SuperAdminReportsController(ViaReservaDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> SystemSettings(string format = "pdf", CancellationToken ct = default)
    {
        var nowUtc = ViaReservaERP.AppTime.Now;
        var windowStart = nowUtc.AddDays(-7);

        var activeCompanies = await _db.Companies.AsNoTracking().CountAsync(c => !c.IsDeleted && c.IsActive, ct);
        var activeUsers = await _db.Users.AsNoTracking().CountAsync(u => !u.IsDeleted && u.IsActive, ct);
        var activePlans = await _db.SubscriptionPlans.AsNoTracking().CountAsync(ct);
        var workflows = await _db.Workflows.AsNoTracking().CountAsync(ct);
        var securityAlerts = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionDate >= windowStart)
            .CountAsync(a =>
                (a.Action ?? "").ToLower().Contains("unauthorized") ||
                (a.Action ?? "").ToLower().Contains("forbidden") ||
                (a.Action ?? "").ToLower().Contains("suspicious") ||
                (a.Action ?? "").ToLower().Contains("failed login") ||
                ((a.Action ?? "").ToLower().Contains("login") && (a.Action ?? "").ToLower().Contains("fail")),
                ct);

        var stripeConfigured = !string.IsNullOrWhiteSpace(_config["Stripe:SecretKey"]) || !string.IsNullOrWhiteSpace(_config["Stripe:ApiKey"]);
        var sendGridConfigured = !string.IsNullOrWhiteSpace(_config["SendGrid:ApiKey"]) || !string.IsNullOrWhiteSpace(_config["SendGrid:Key"]);

        var headers = new[] { "Section", "Setting", "Value" };
        var rows = new List<string[]>
        {
            new[] { "Platform Overview", "Active Companies", activeCompanies.ToString(CultureInfo.InvariantCulture) },
            new[] { "Platform Overview", "Active Users", activeUsers.ToString(CultureInfo.InvariantCulture) },
            new[] { "Platform Overview", "Subscription Plans", activePlans.ToString(CultureInfo.InvariantCulture) },
            new[] { "Platform Overview", "Workflow Automations", workflows.ToString(CultureInfo.InvariantCulture) },
            new[] { "Security", "Security Alerts (7 days)", securityAlerts.ToString(CultureInfo.InvariantCulture) },
            new[] { "Security", "Cookie Session Timeout (hours)", "12" },
            new[] { "Integrations", "Stripe", stripeConfigured ? "Configured" : "Not Configured" },
            new[] { "Integrations", "SendGrid", sendGridConfigured ? "Configured" : "Not Configured" },
            new[] { "Compliance", "Audit Retention (days)", "90" },
            new[] { "Backup & Recovery", "Backup Mode", "Managed via SQL Server" }
        };

        var kpis = new Dictionary<string, string>
        {
            ["Active Companies"] = activeCompanies.ToString(CultureInfo.InvariantCulture),
            ["Active Users"] = activeUsers.ToString(CultureInfo.InvariantCulture),
            ["Security Alerts (7d)"] = securityAlerts.ToString(CultureInfo.InvariantCulture)
        };

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "System Settings", headers, rows, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> Reservations(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.Reservations
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .OrderByDescending(r => r.ReservationId)
            .Take(5000)
            .Select(r => new ReservationReportRow(
                r.ReservationId,
                r.Company != null ? r.Company.CompanyName : "",
                r.Guest != null ? (r.Guest.FullName ?? "") : "",
                r.CheckInDate != null ? r.CheckInDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                r.CheckOutDate != null ? r.CheckOutDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                r.Status ?? "",
                r.TotalAmount ?? 0m))
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Reservations"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Total Amount"] = $"₱ {rows.Sum(r => r.TotalAmount):N0}"
        };

        var headers = new[] { "ReservationId", "Company", "Guest", "CheckIn", "CheckOut", "Status", "TotalAmount" };
        var data = rows.Select(r => new[]
        {
            r.ReservationId.ToString(CultureInfo.InvariantCulture),
            r.CompanyName,
            r.GuestName,
            r.CheckInDate?.ToString("yyyy-MM-dd") ?? "",
            r.CheckOutDate?.ToString("yyyy-MM-dd") ?? "",
            r.Status,
            r.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Reservation Reports", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> ServiceRequests(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.ServiceRequests
            .Include(sr => sr.Company)
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .Include(sr => sr.AssignedToUser)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(5000)
            .Select(sr => new ServiceRequestReportRow(
                sr.RequestId,
                sr.Company != null ? sr.Company.CompanyName : "",
                sr.Guest != null ? (sr.Guest.FullName ?? "") : "",
                sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                sr.AssignedToUser != null ? sr.AssignedToUser.FullName : "",
                sr.Status ?? "",
                sr.RequestDate))
            .ToListAsync(ct);

        var pending = rows.Count(r => string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase));
        var completed = rows.Count(r => string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase));

        var kpis = new Dictionary<string, string>
        {
            ["Total Requests"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Pending"] = pending.ToString(CultureInfo.InvariantCulture),
            ["Completed"] = completed.ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "RequestId", "Company", "Guest", "Service", "AssignedStaff", "Status", "RequestDate" };
        var data = rows.Select(r => new[]
        {
            r.RequestId.ToString(CultureInfo.InvariantCulture),
            r.CompanyName,
            r.GuestName,
            r.ServiceType,
            r.AssignedStaff,
            r.Status,
            r.RequestDateUtc.ToString("yyyy-MM-dd HH:mm")
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Guest Service Requests", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> WorkflowManagement(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.WorkflowInstances
            .Include(wi => wi.Company)
            .Include(wi => wi.Workflow)
            .OrderByDescending(wi => wi.CreatedAt)
            .Take(5000)
            .Select(wi => new WorkflowInstanceReportRow(
                wi.InstanceId,
                wi.Company != null ? wi.Company.CompanyName : "",
                wi.Workflow != null ? (wi.Workflow.Name ?? "") : "",
                wi.ReferenceType ?? "",
                wi.ReferenceId,
                wi.Status ?? "",
                wi.CreatedAt))
            .ToListAsync(ct);

        var pending = rows.Count(r => string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Status, "inprogress", StringComparison.OrdinalIgnoreCase));
        var completed = rows.Count(r => string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var escalated = rows.Count(r => (r.Status ?? "").ToLower().Contains("escal"));
        var failed = rows.Count(r => (r.Status ?? "").ToLower().Contains("fail"));

        var kpis = new Dictionary<string, string>
        {
            ["Total Instances"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Pending"] = pending.ToString(CultureInfo.InvariantCulture),
            ["Completed"] = completed.ToString(CultureInfo.InvariantCulture),
            ["Escalated"] = escalated.ToString(CultureInfo.InvariantCulture),
            ["Failed"] = failed.ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "InstanceId", "Company", "Workflow", "ReferenceType", "ReferenceId", "Status", "CreatedAt" };
        var data = rows.Select(r => new[]
        {
            r.InstanceId.ToString(CultureInfo.InvariantCulture),
            r.CompanyName,
            r.WorkflowName,
            r.ReferenceType,
            r.ReferenceId?.ToString(CultureInfo.InvariantCulture) ?? "",
            r.Status,
            r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm")
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Workflow Management", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> Accounting(string format = "pdf", CancellationToken ct = default)
    {
        var paymentRows = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Reservation)
            .ThenInclude(r => r!.Guest)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5000)
            .Select(p => new AccountingTransactionReportRow(
                p.CreatedAt,
                p.Company != null ? p.Company.CompanyName : "",
                p.Status != null && p.Status.ToLower().Contains("refund") ? "Refund" : (p.ReservationId != null ? "Reservation Payment" : "Payment"),
                p.ReservationId != null ? "Reservations" : "Payments",
                p.Reservation != null && p.Reservation.Guest != null ? (p.Reservation.Guest.FullName ?? "") : "",
                p.PaymentMethod ?? "",
                p.Amount ?? 0m,
                p.Status ?? "",
                "payment",
                p.PaymentId))
            .ToListAsync(ct);

        var txnRows = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Company)
            .OrderByDescending(t => t.TransactionDate)
            .Take(5000)
            .Select(t => new AccountingTransactionReportRow(
                t.TransactionDate,
                t.Company != null ? t.Company.CompanyName : "",
                t.Type ?? "Manual Adjustment",
                t.ReferenceType ?? "Accounting",
                "",
                "",
                t.Amount ?? 0m,
                "Recorded",
                "transaction",
                t.TransactionId))
            .ToListAsync(ct);

        var rows = paymentRows
            .Concat(txnRows)
            .OrderByDescending(r => r.DateUtc)
            .Take(5000)
            .ToList();

        var total = rows.Sum(r => r.Amount);
        var refunds = rows.Where(r => (r.TransactionType ?? "").ToLower().Contains("refund")).Sum(r => r.Amount);
        var failed = rows.Count(r => r.Source == "payment" && !string.Equals(r.Status, "succeeded", StringComparison.OrdinalIgnoreCase));

        var kpis = new Dictionary<string, string>
        {
            ["Total Records"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Total Amount"] = $"₱ {total:N0}",
            ["Refunds"] = $"₱ {refunds:N0}",
            ["Failed Payments"] = failed.ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "DateUtc", "Company", "TransactionType", "RelatedModule", "CustomerOrGuest", "PaymentMethod", "Amount", "Status", "Source", "SourceId" };
        var data = rows.Select(r => new[]
        {
            r.DateUtc.ToString("yyyy-MM-dd HH:mm"),
            r.CompanyName,
            r.TransactionType,
            r.RelatedModule,
            r.CustomerOrGuest,
            r.PaymentMethod,
            r.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            r.Status,
            r.Source,
            r.SourceId.ToString(CultureInfo.InvariantCulture)
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Accounting Overview", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> Revenue(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.Payments
            .Include(p => p.Company)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5000)
            .Select(p => new RevenueReportRow(
                p.CreatedAt,
                p.ReservationId != null ? "Reservation" : "General",
                p.Company != null ? p.Company.CompanyName : "",
                p.Amount ?? 0m,
                p.Status ?? ""))
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Payments"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Revenue"] = $"₱ {rows.Sum(r => r.Amount):N0}"
        };

        var headers = new[] { "Date", "Source", "Company", "Amount", "Status" };
        var data = rows.Select(r => new[]
        {
            r.Date.ToString("yyyy-MM-dd HH:mm"),
            r.Source,
            r.CompanyName,
            r.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            r.Status
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Revenue Reports", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> Subscriptions(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.Subscriptions
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .OrderByDescending(s => s.SubscriptionId)
            .Take(5000)
            .Select(s => new SubscriptionReportRow(
                s.SubscriptionId,
                s.Company != null ? s.Company.CompanyName : "",
                s.Plan != null ? (s.Plan.PlanName ?? "") : "",
                s.StartDate != null ? s.StartDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                s.EndDate != null ? s.EndDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                s.Status ?? "",
                s.Plan != null ? (s.Plan.Price ?? 0m) : 0m))
            .ToListAsync(ct);

        var active = rows.Count(r => string.Equals(r.Status, "active", StringComparison.OrdinalIgnoreCase));
        var kpis = new Dictionary<string, string>
        {
            ["Total Subscriptions"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Active"] = active.ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "SubscriptionId", "Company", "Plan", "StartDate", "EndDate", "Status", "Price" };
        var data = rows.Select(r => new[]
        {
            r.SubscriptionId.ToString(CultureInfo.InvariantCulture),
            r.CompanyName,
            r.PlanName,
            r.StartDate?.ToString("yyyy-MM-dd") ?? "",
            r.EndDate?.ToString("yyyy-MM-dd") ?? "",
            r.Status,
            r.Price.ToString("0.00", CultureInfo.InvariantCulture)
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Subscription Reports", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> Financial(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.Transactions
            .Include(t => t.Company)
            .OrderByDescending(t => t.TransactionDate)
            .Take(5000)
            .Select(t => new FinancialReportRow(
                t.TransactionDate,
                t.Company != null ? t.Company.CompanyName : "",
                t.Type ?? "",
                t.Description ?? "",
                t.Amount ?? 0m))
            .ToListAsync(ct);

        var income = rows.Where(r => string.Equals(r.Type, "income", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);
        var expenses = rows.Where(r => string.Equals(r.Type, "expense", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Type, "expenses", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);

        var kpis = new Dictionary<string, string>
        {
            ["Income"] = $"₱ {income:N0}",
            ["Expenses"] = $"₱ {expenses:N0}",
            ["Profit"] = $"₱ {(income - expenses):N0}"
        };

        var headers = new[] { "Date", "Company", "Type", "Description", "Amount" };
        var data = rows.Select(r => new[]
        {
            r.Date.ToString("yyyy-MM-dd"),
            r.CompanyName,
            r.Type,
            r.Description,
            r.Amount.ToString("0.00", CultureInfo.InvariantCulture)
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Financial Reports", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> Audit(string format = "pdf", CancellationToken ct = default)
    {
        var rows = await _db.AuditLogs
            .Include(a => a.Company)
            .Include(a => a.User)
            .OrderByDescending(a => a.ActionDate)
            .Take(5000)
            .Select(a => new AuditLogReportRow(
                a.ActionDate,
                a.Company != null ? a.Company.CompanyName : "",
                a.User != null ? a.User.FullName : "",
                a.Action ?? "",
                a.TableName ?? "",
                a.RecordId,
                a.IPAddress ?? ""))
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Events"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Range"] = rows.Count > 0 ? $"{rows.Last().Date:yyyy-MM-dd} to {rows.First().Date:yyyy-MM-dd}" : "—"
        };

        var headers = new[] { "Date", "Company", "User", "Action", "Table", "RecordId", "IP" };
        var data = rows.Select(r => new[]
        {
            r.Date.ToString("yyyy-MM-dd HH:mm"),
            r.CompanyName,
            r.UserName,
            r.Action,
            r.TableName,
            r.RecordId?.ToString(CultureInfo.InvariantCulture) ?? "",
            r.IpAddress
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Audit Logs Reports", headers, data, kpis, userName, userRole);
    }

    private IActionResult Export(string format, string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyDictionary<string, string> kpis, string userName, string userRole)
    {
        format = (format ?? "pdf").Trim().ToLowerInvariant();

        return format switch
        {
            "csv" => ExportCsv(title, headers, rows, userName, userRole),
            "xlsx" => ExportExcel(title, headers, rows, userName, userRole),
            "pdf" => ExportPdf(title, headers, rows, kpis, userName, userRole),
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

        foreach (var r in rows)
            sb.AppendLine(string.Join(',', r.Select(EscapeCsv)));

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = FileName(title, "csv");
        return File(bytes, "text/csv", fileName);
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
        for (var i = 0; i < headers.Count; i++)
            ws.Cell(startRow, i + 1).Value = headers[i];

        ws.Range(startRow, 1, startRow, headers.Count).Style.Font.Bold = true;
        ws.Range(startRow, 1, startRow, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");

        var rowIndex = startRow + 1;
        foreach (var r in rows)
        {
            for (var c = 0; c < headers.Count && c < r.Length; c++)
                ws.Cell(rowIndex, c + 1).Value = r[c];
            rowIndex++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();
        var fileName = FileName(title, "xlsx");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private IActionResult ExportPdf(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyDictionary<string, string> kpis, string userName, string userRole)
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

                    row.ConstantItem(220).AlignRight().Column(col =>
                    {
                        col.Item().Text(title).FontSize(14).FontColor("#1e293b").SemiBold();
                        col.Item().Text($"Generated By: {userName} ({userRole})").FontSize(9).FontColor(Colors.Grey.Darken2);
                        col.Item().Text($"Generated At: {created:yyyy-MM-dd HH:mm} UTC").FontSize(9).FontColor(Colors.Grey.Darken2);
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
                                header.Cell().Background("#f1f5f9").BorderBottom(1).BorderColor("#e2e8f0").PaddingVertical(6).PaddingHorizontal(6)
                                    .Text(headers[i]).FontColor("#1e293b").SemiBold().FontSize(9);
                            }
                        });

                        foreach (var r in rowList)
                        {
                            for (var i = 0; i < headers.Count; i++)
                            {
                                var val = i < r.Length ? r[i] : "";
                                table.Cell().BorderBottom(1).BorderColor("#f1f5f9").PaddingVertical(6).PaddingHorizontal(6).Text(val).FontSize(9);
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
        var fileName = FileName(title, "pdf");
        return File(pdfBytes, "application/pdf", fileName);
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
        var safe = new string(title.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_').ToArray()).Trim();
        safe = string.Join('-', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{safe}-{ViaReservaERP.AppTime.Now:yyyyMMdd-HHmm}.{ext}";
    }
}
