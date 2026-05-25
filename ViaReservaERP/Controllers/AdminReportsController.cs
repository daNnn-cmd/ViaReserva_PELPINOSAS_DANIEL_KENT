using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using ViaReservaERP.Data;
using ViaReservaERP.Security;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.CompanyAdmin)]
public class AdminReportsController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AdminReportsController(ViaReservaDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private int CurrentCompanyId => User.GetCompanyId() ?? 0;

    [HttpGet]
    public async Task<IActionResult> ExportReservations(string format = "pdf", CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var rows = await _db.Reservations
            .Include(r => r.Guest)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.ReservationId)
            .Take(5000)
            .Select(r => new {
                r.ReservationId,
                GuestName = r.Guest != null ? (r.Guest.FullName ?? "") : "",
                CheckInDate = r.CheckInDate,
                CheckOutDate = r.CheckOutDate,
                Status = r.Status ?? "",
                TotalAmount = r.TotalAmount ?? 0m
            })
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Reservations"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Total Amount"] = $"₱ {rows.Sum(r => r.TotalAmount):N0}"
        };

        var headers = new[] { "ID", "Guest", "Check-In", "Check-Out", "Status", "Amount" };
        var data = rows.Select(r => new[]
        {
            r.ReservationId.ToString(CultureInfo.InvariantCulture),
            r.GuestName,
            r.CheckInDate?.ToString("yyyy-MM-dd") ?? "",
            r.CheckOutDate?.ToString("yyyy-MM-dd") ?? "",
            r.Status,
            r.TotalAmount.ToString("N2", CultureInfo.InvariantCulture)
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Reservations Report", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> ExportGuests(string format = "pdf", CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var rows = await _db.Guests
            .Where(g => g.CompanyId == companyId)
            .OrderByDescending(g => g.GuestId)
            .Take(5000)
            .Select(g => new {
                g.GuestId,
                g.FullName,
                g.Email,
                g.Phone,
                CreatedAt = g.CreatedAt
            })
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Guests"] = rows.Count.ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "ID", "Full Name", "Email", "Phone", "Joined" };
        var data = rows.Select(g => new[]
        {
            g.GuestId.ToString(CultureInfo.InvariantCulture),
            g.FullName ?? "",
            g.Email ?? "",
            g.Phone ?? "",
            g.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Guests Report", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> ExportStaff(string format = "pdf", CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var rows = await _db.Users
            .Include(u => u.Role)
            .Where(u => u.CompanyId == companyId && !u.IsDeleted && u.RoleId != 6)
            .OrderByDescending(u => u.UserId)
            .Take(5000)
            .Select(u => new {
                u.UserId,
                u.FullName,
                u.Email,
                RoleName = u.Role != null ? u.Role.RoleName : "No Role",
                u.IsActive,
                u.CreatedAt
            })
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Staff"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Active"] = rows.Count(r => r.IsActive).ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "ID", "Full Name", "Email", "Role", "Status", "Joined" };
        var data = rows.Select(u => new[]
        {
            u.UserId.ToString(CultureInfo.InvariantCulture),
            u.FullName ?? "",
            u.Email ?? "",
            u.RoleName,
            u.IsActive ? "Authorized" : "Revoked",
            u.CreatedAt.ToString("yyyy-MM-dd")
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Staff Directory Report", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> ExportServices(string format = "pdf", CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var rows = await _db.Services
            .Where(s => s.CompanyId == companyId && !s.IsDeleted)
            .OrderByDescending(s => s.ServiceId)
            .Take(5000)
            .Select(s => new {
                s.ServiceId,
                s.ServiceName,
                Price = s.Price ?? 0m
            })
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Services"] = rows.Count.ToString(CultureInfo.InvariantCulture)
        };

        var headers = new[] { "ID", "Service Name", "Base Price" };
        var data = rows.Select(s => new[]
        {
            s.ServiceId.ToString(CultureInfo.InvariantCulture),
            s.ServiceName ?? "",
            s.Price.ToString("N2", CultureInfo.InvariantCulture)
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Service Catalog Report", headers, data, kpis, userName, userRole);
    }

    [HttpGet]
    public async Task<IActionResult> ExportAudit(DateTime? startDate, DateTime? endDate, string format = "pdf", CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var start = startDate ?? ViaReservaERP.AppTime.Now.Date.AddDays(-7);
        var end = endDate ?? ViaReservaERP.AppTime.Now.Date;

        var rows = await _db.AuditLogs
            .Include(a => a.User)
            .Where(a => a.CompanyId == companyId && a.ActionDate >= start && a.ActionDate <= end)
            .OrderByDescending(a => a.ActionDate)
            .Take(5000)
            .Select(a => new {
                a.ActionDate,
                UserName = a.User != null ? a.User.FullName : "System",
                a.Action,
                a.TableName,
                a.IPAddress
            })
            .ToListAsync(ct);

        var kpis = new Dictionary<string, string>
        {
            ["Total Events"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Date Range"] = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}"
        };

        var headers = new[] { "Date", "User", "Action", "Area", "IP Address" };
        var data = rows.Select(a => new[]
        {
            a.ActionDate.ToString("yyyy-MM-dd HH:mm"),
            a.UserName,
            a.Action ?? "",
            a.TableName ?? "",
            a.IPAddress ?? ""
        });

        var userName = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "N/A";

        return Export(format, "Audit Trail Report", headers, data, kpis, userName, userRole);
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
