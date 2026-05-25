using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stripe;
using ViaReservaERP.Data;
using ViaReservaERP.Models.Reservations;

namespace ViaReservaERP.Services;

public class StripePaymentService : IStripePaymentService
{
    private readonly ViaReservaDbContext _db;
    private readonly IConfiguration _config;

    public StripePaymentService(ViaReservaDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<CreatePaymentIntentResponse> CreateReservationPaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct = default)
    {
        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe secret key is not configured. Set Stripe:SecretKey in appsettings.json or as an environment variable.");

        if (request.CheckOutDate <= request.CheckInDate)
            throw new InvalidOperationException("Check-out must be after check-in.");

        var companyExists = await _db.Companies
            .AsNoTracking()
            .AnyAsync(c => c.CompanyId == request.CompanyId && !c.IsDeleted && c.IsActive, ct);

        if (!companyExists)
            throw new InvalidOperationException("Selected hotel is not available.");

        var availableRooms = await _db.Rooms
            .AsNoTracking()
            .CountAsync(r => r.CompanyId == request.CompanyId && r.RoomTypeId == request.SelectedRoomTypeId && r.Status == "Available", ct);

        if (availableRooms <= 0)
            throw new InvalidOperationException("Selected room type is not available.");

        var nights = Math.Max(0, request.CheckOutDate.DayNumber - request.CheckInDate.DayNumber);
        if (nights <= 0)
            throw new InvalidOperationException("Invalid date range.");

        List<SelectedServiceDto> selectedServices;
        try
        {
            selectedServices = JsonSerializer.Deserialize<List<SelectedServiceDto>>(request.SelectedServicesJson ?? "[]") ?? new();
        }
        catch
        {
            selectedServices = new();
        }

        var allowedServiceIds = selectedServices.Select(s => s.ServiceId).Distinct().ToList();
        var servicePriceMap = await _db.Services
            .AsNoTracking()
            .Where(s => s.CompanyId.HasValue && s.CompanyId.Value == request.CompanyId)
            .Where(s => allowedServiceIds.Contains(s.ServiceId))
            .Select(s => new { s.ServiceId, Price = s.Price ?? 0m, Name = s.ServiceName ?? "Service" })
            .ToDictionaryAsync(x => x.ServiceId, x => new { x.Price, x.Name }, ct);

        var servicesTotal = 0m;
        foreach (var svc in selectedServices)
        {
            if (!servicePriceMap.TryGetValue(svc.ServiceId, out var actual))
                continue;
            var qty = Math.Max(1, svc.Quantity);
            servicesTotal += actual.Price * qty;
        }

        var roomTotal = request.SelectedRoomTypePrice * nights;
        var grandTotal = roomTotal + servicesTotal;
        if (grandTotal <= 0)
            throw new InvalidOperationException("Invalid total amount.");

        // Stripe uses the smallest currency unit.
        var amount = (long)Math.Round(grandTotal * 100m, MidpointRounding.AwayFromZero);

        StripeConfiguration.ApiKey = secretKey;

        var service = new PaymentIntentService();
        var options = new PaymentIntentCreateOptions
        {
            Amount = amount,
            Currency = "php",
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string>
            {
                ["companyId"] = request.CompanyId.ToString(CultureInfo.InvariantCulture),
                ["email"] = request.Email ?? "",
                ["purpose"] = "reservation"
            }
        };

        var intent = await service.CreateAsync(options, cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(intent.ClientSecret))
            throw new InvalidOperationException("Stripe did not return a client secret.");

        return new CreatePaymentIntentResponse
        {
            ClientSecret = intent.ClientSecret,
            PaymentIntentId = intent.Id,
            Amount = amount,
            Currency = intent.Currency ?? "php"
        };
    }

    public async Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct = default)
    {
        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe secret key is not configured. Set Stripe:SecretKey in appsettings.json or as an environment variable.");

        if (string.IsNullOrWhiteSpace(paymentIntentId))
            return null;

        StripeConfiguration.ApiKey = secretKey;
        var service = new PaymentIntentService();
        var intent = await service.GetAsync(paymentIntentId, cancellationToken: ct);
        return intent.Status;
    }
    public async Task<bool> RefundPaymentAsync(string paymentIntentId, CancellationToken ct = default)
    {
        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe secret key is not configured.");

        if (string.IsNullOrWhiteSpace(paymentIntentId))
            return false;

        StripeConfiguration.ApiKey = secretKey;
        var options = new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
        };

        var service = new RefundService();
        var refund = await service.CreateAsync(options, cancellationToken: ct);

        return refund.Status == "succeeded" || refund.Status == "pending";
    }

    public async Task<string?> CreateCustomerPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct = default)
    {
        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe secret key is not configured.");

        StripeConfiguration.ApiKey = secretKey;

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = returnUrl,
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }
}
