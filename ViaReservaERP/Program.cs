using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using ViaReservaERP.Data;
using ViaReservaERP.Services;
using ViaReservaERP.Security;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
var viaReservaConnectionString = builder.Configuration.GetConnectionString("ViaReservaDb") ?? throw new InvalidOperationException("Connection string 'ViaReservaDb' not found.");
builder.Services.AddDbContext<ViaReservaDbContext>(options =>
    options.UseSqlServer(viaReservaConnectionString));

builder.Services.AddHttpContextAccessor();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(RoleNames.SuperAdmin, policy => policy.RequireClaim(ViaReservaClaims.RoleId, "1"));
    options.AddPolicy(RoleNames.CompanyAdmin, policy => policy.RequireClaim(ViaReservaClaims.RoleId, "2"));
    options.AddPolicy(RoleNames.Accountant, policy => policy.RequireClaim(ViaReservaClaims.RoleId, "3"));
    options.AddPolicy(RoleNames.FrontDesk, policy => policy.RequireClaim(ViaReservaClaims.RoleId, "4"));
    options.AddPolicy(RoleNames.ServiceStaff, policy => policy.RequireClaim(ViaReservaClaims.RoleId, "5"));
    options.AddPolicy(RoleNames.Guest, policy => policy.RequireClaim(ViaReservaClaims.RoleId, "6"));
});

builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IGuestService, GuestService>();
builder.Services.AddScoped<IReservationService, ReservationAppService>();
builder.Services.AddScoped<IAuthSignInService, AuthSignInService>();
builder.Services.AddScoped<IStripePaymentService, StripePaymentService>();
builder.Services.AddScoped<IBookingCheckoutService, BookingCheckoutService>();
builder.Services.AddScoped<IEmailService, SendGridEmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IServiceRequestAppService, ServiceRequestAppService>();
builder.Services.AddScoped<ISystemValidationService, SystemValidationService>();
builder.Services.AddScoped<ITaxService, TaxService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseDeveloperExceptionPage(); // Enabled for debugging

if (!app.Environment.IsDevelopment())
{
    // app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/db-check", async (ViaReservaDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);

    var companies = await db.Companies.CountAsync(ct);
    var users = await db.Users.CountAsync(ct);
    var reservations = await db.Reservations.CountAsync(ct);

    return Results.Ok(new
    {
        canConnect,
        companies,
        users,
        reservations
    });
});

// Auto-fix: ensure IsDeleted columns exist before serving any requests
using (var scope = app.Services.CreateScope())
{
    var fixDb = scope.ServiceProvider.GetRequiredService<ViaReservaDbContext>();
    try
    {
        await fixDb.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'[dbo].[EnterpriseInquiries]', N'U') IS NULL BEGIN CREATE TABLE [dbo].[EnterpriseInquiries] ([EnterpriseInquiryId] INT IDENTITY(1,1) NOT NULL, [CompanyName] NVARCHAR(150) NOT NULL, [ContactPerson] NVARCHAR(150) NOT NULL, [Email] NVARCHAR(150) NOT NULL, [Phone] NVARCHAR(50) NOT NULL, [NumberOfBranches] INT NOT NULL, [Requirements] NVARCHAR(2000) NOT NULL, [CustomWorkflowNeeds] NVARCHAR(2000) NULL, [Status] NVARCHAR(50) NOT NULL DEFAULT 'New', [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(), [IsDeleted] BIT NOT NULL DEFAULT 0, [DeletedAt] DATETIME2 NULL, [DeletedBy] INT NULL, CONSTRAINT [PK_EnterpriseInquiries] PRIMARY KEY ([EnterpriseInquiryId])); END");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Rooms]') AND name = 'IsDeleted') ALTER TABLE [dbo].[Rooms] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Guests]') AND name = 'IsDeleted') ALTER TABLE [dbo].[Guests] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Services]') AND name = 'IsDeleted') ALTER TABLE [dbo].[Services] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Guests]') AND name = 'CreatedAt') ALTER TABLE [dbo].[Guests] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Guests]') AND name = 'IsActive') ALTER TABLE [dbo].[Guests] ADD [IsActive] BIT NOT NULL DEFAULT 1;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservations]') AND name = 'CreatedAt') ALTER TABLE [dbo].[Reservations] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = 'CreatedAt') ALTER TABLE [dbo].[Payments] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Companies]') AND name = 'StripeCustomerId') ALTER TABLE [dbo].[Companies] ADD [StripeCustomerId] NVARCHAR(255) NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceRequests]') AND name = 'ReservationId') ALTER TABLE [dbo].[ServiceRequests] ADD [ReservationId] INT NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[EnterpriseInquiries]') AND name = 'IsDeleted') ALTER TABLE [dbo].[EnterpriseInquiries] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = 'AvatarUrl') ALTER TABLE [dbo].[Users] ADD [AvatarUrl] NVARCHAR(255) NULL;");
        
        // Tax & Service Charge columns
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservations]') AND name = 'Subtotal') ALTER TABLE [dbo].[Reservations] ADD [Subtotal] DECIMAL(10,2) NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservations]') AND name = 'TaxAmount') ALTER TABLE [dbo].[Reservations] ADD [TaxAmount] DECIMAL(10,2) NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Reservations]') AND name = 'ServiceCharge') ALTER TABLE [dbo].[Reservations] ADD [ServiceCharge] DECIMAL(10,2) NULL;");
        
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND name = 'Subtotal') ALTER TABLE [dbo].[Transactions] ADD [Subtotal] DECIMAL(10,2) NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND name = 'TaxAmount') ALTER TABLE [dbo].[Transactions] ADD [TaxAmount] DECIMAL(10,2) NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Transactions]') AND name = 'ServiceCharge') ALTER TABLE [dbo].[Transactions] ADD [ServiceCharge] DECIMAL(10,2) NULL;");

        await fixDb.Database.ExecuteSqlRawAsync("ALTER TABLE [dbo].[AuditLogs] ALTER COLUMN [OldValues] VARCHAR(MAX) NULL;");
        await fixDb.Database.ExecuteSqlRawAsync("ALTER TABLE [dbo].[AuditLogs] ALTER COLUMN [NewValues] VARCHAR(MAX) NULL;");
        app.Logger.LogInformation("Database schema verified: EnterpriseInquiries present, IsDeleted, CreatedAt, IsActive columns present, AuditLogs size fixed.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not auto-fix database schema.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/hash", (string password) =>
    {
        var hash = PasswordHasher.Hash(password);
        return Results.Ok(new { password, hash });
    });
}

app.Run();
