using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ViaReservaERP.Data;
using ViaReservaERP.Models.Enterprise;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

[AllowAnonymous]
public class EnterpriseController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly INotificationService _notify;

    public EnterpriseController(ViaReservaDbContext db, INotificationService notify)
    {
        _db = db;
        _notify = notify;
    }

    [HttpGet]
    public IActionResult Inquiry()
    {
        ViewData["Title"] = "Enterprise Inquiry";
        return View(new EnterpriseInquiryViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitInquiry(EnterpriseInquiryViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Enterprise Inquiry";
            return View("Inquiry", model);
        }

        _db.EnterpriseInquiries.Add(new Models.EnterpriseInquiry
        {
            CompanyName = model.CompanyName,
            ContactPerson = model.ContactPerson,
            Email = model.Email,
            Phone = model.Phone,
            NumberOfBranches = model.NumberOfBranches,
            Requirements = model.Requirements,
            CustomWorkflowNeeds = model.CustomWorkflowNeeds,
            Status = "New",
            CreatedAt = ViaReservaERP.AppTime.Now,
            IsDeleted = false
        });

        _db.AuditLogs.Add(new Models.AuditLog
        {
            Action = "Enterprise Inquiry",
            TableName = "EnterpriseInquiries",
            NewValues = $"CompanyName={model.CompanyName}; Contact={model.ContactPerson}; Email={model.Email}; Phone={model.Phone}; Branches={model.NumberOfBranches}; Requirements={model.Requirements}; CustomWorkflowNeeds={model.CustomWorkflowNeeds}",
            ActionDate = ViaReservaERP.AppTime.Now
        });
        await _db.SaveChangesAsync(ct);

        await _notify.NotifySuperAdminsAsync(
            title: "New Enterprise Inquiry",
            message: $"{model.CompanyName} submitted an enterprise inquiry. Contact: {model.ContactPerson} ({model.Email}).",
            type: "Sales",
            ct);

        TempData["SuccessMessage"] = "Thanks! Your inquiry was submitted. Our team will contact you soon.";
        return RedirectToAction(nameof(Inquiry));
    }
}
