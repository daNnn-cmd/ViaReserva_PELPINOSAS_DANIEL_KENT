using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ViaReservaERP.Security;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.CompanyAdmin)]
public class CompanyController : Controller
{
    public IActionResult Dashboard()
    {
        ViewData["Title"] = "Company Dashboard";
        return View("~/Views/Shared/Placeholder.cshtml");
    }

    public IActionResult Staff()
    {
        ViewData["Title"] = "Staff Management";
        return View("~/Views/Shared/Placeholder.cshtml");
    }

    public IActionResult Reports()
    {
        ViewData["Title"] = "Reports";
        return View("~/Views/Shared/Placeholder.cshtml");
    }

    public IActionResult Subscription()
    {
        ViewData["Title"] = "Subscription Status";
        return View("~/Views/Shared/Placeholder.cshtml");
    }
}
