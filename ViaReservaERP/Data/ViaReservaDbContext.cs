using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Security.Claims;
using System.Text.Json;
using ViaReservaERP.Models;
using ViaReservaERP.Security;

namespace ViaReservaERP.Data;

public class ViaReservaDbContext : DbContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ViaReservaDbContext(DbContextOptions<ViaReservaDbContext> options, IHttpContextAccessor httpContextAccessor) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<ErpUser> Users => Set<ErpUser>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationRoom> ReservationRooms => Set<ReservationRoom>();
    public DbSet<ServiceCatalogItem> Services => Set<ServiceCatalogItem>();
    public DbSet<ReservationService> ReservationServices => Set<ReservationService>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AccountingTransaction> Transactions => Set<AccountingTransaction>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<EnterpriseInquiry> EnterpriseInquiries => Set<EnterpriseInquiry>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowInstanceStep> WorkflowInstanceSteps => Set<WorkflowInstanceStep>();
    public DbSet<EscalationRule> EscalationRules => Set<EscalationRule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnforcePlanRestrictionsAsync(cancellationToken);
        var auditEntries = OnBeforeSaveChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        await OnAfterSaveChanges(auditEntries);
        return result;
    }

    private async Task EnforcePlanRestrictionsAsync(CancellationToken ct)
    {
        var addedRooms = ChangeTracker.Entries<Room>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (addedRooms.Count == 0)
            return;

        var companyIds = addedRooms.Select(r => r.CompanyId).Distinct().ToList();
        foreach (var companyId in companyIds)
        {
            var planName = await Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.CompanyId == companyId)
                .OrderByDescending(s => s.SubscriptionId)
                .Select(s => s.Plan != null ? (s.Plan.PlanName ?? "") : "")
                .FirstOrDefaultAsync(ct);

            if (!string.Equals(planName, "Basic", StringComparison.OrdinalIgnoreCase))
                continue;

            var existingRooms = await Rooms.AsNoTracking().CountAsync(r => r.CompanyId == companyId, ct);
            var roomsBeingAdded = addedRooms.Count(r => r.CompanyId == companyId);

            if (existingRooms + roomsBeingAdded > 25)
                throw new InvalidOperationException("Basic plan allows a maximum of 25 rooms. Please upgrade your subscription to add more rooms.");
        }
    }

    private List<AuditEntry> OnBeforeSaveChanges()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            var auditEntry = new AuditEntry(entry);
            auditEntry.TableName = entry.Metadata.GetTableName();
            auditEntry.UserId = GetCurrentUserId();
            auditEntry.IPAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            auditEntry.UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString();

            // Capture CompanyId if the entity has one
            var companyIdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CompanyId");
            if (companyIdProp != null && companyIdProp.CurrentValue != null)
            {
                auditEntry.CompanyId = (int?)companyIdProp.CurrentValue;
            }
            else
            {
                // Fallback to current user's company claim
                var cidStr = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ViaReservaClaims.CompanyId);
                if (int.TryParse(cidStr, out var cid))
                    auditEntry.CompanyId = cid;
            }

            auditEntries.Add(auditEntry);

            foreach (var property in entry.Properties)
            {
                string propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[propertyName] = property.CurrentValue;
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.Action = "Create";
                        auditEntry.NewValues[propertyName] = property.CurrentValue;
                        break;

                    case EntityState.Deleted:
                        auditEntry.Action = "Delete";
                        auditEntry.OldValues[propertyName] = property.OriginalValue;
                        break;

                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.Action = "Update";
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                        }
                        break;
                }
            }
        }

        foreach (var auditEntry in auditEntries.Where(_ => !_.HasTemporaryProperties))
        {
            AuditLogs.Add(auditEntry.ToAuditLog());
        }

        return auditEntries.Where(_ => _.HasTemporaryProperties).ToList();
    }

    private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries)
    {
        if (auditEntries == null || auditEntries.Count == 0) return;

        foreach (var auditEntry in auditEntries)
        {
            foreach (var prop in auditEntry.TemporaryProperties)
            {
                if (prop.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                }
                else
                {
                    auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                }
            }
            AuditLogs.Add(auditEntry.ToAuditLog());
        }

        await base.SaveChangesAsync();
    }

    private int? GetCurrentUserId()
    {
        var raw = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ViaReservaClaims.UserId);
        return int.TryParse(raw, out var id) ? id : null;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RolePermission>()
            .HasKey(rp => new { rp.RoleId, rp.PermissionId });

        modelBuilder.Entity<RolePermission>()
            .HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId);

        modelBuilder.Entity<RolePermission>()
            .HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId);

        modelBuilder.Entity<ServiceRequest>()
            .HasOne(sr => sr.AssignedToUser)
            .WithMany(u => u.AssignedServiceRequests)
            .HasForeignKey(sr => sr.AssignedTo)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<WorkflowInstanceStep>()
            .HasOne(wis => wis.PerformedByUser)
            .WithMany(u => u.WorkflowInstanceStepsPerformed)
            .HasForeignKey(wis => wis.PerformedBy)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<EscalationRule>()
            .HasOne(er => er.EscalateToRole)
            .WithMany()
            .HasForeignKey(er => er.EscalateToRoleId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var nullableDateOnlyConverter = new ValueConverter<DateOnly?, DateTime?>(
            d => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : null,
            d => d.HasValue ? DateOnly.FromDateTime(d.Value) : null);

        modelBuilder.Entity<Reservation>()
            .Property(r => r.CheckInDate)
            .HasConversion(nullableDateOnlyConverter);

        modelBuilder.Entity<Reservation>()
            .Property(r => r.CheckOutDate)
            .HasConversion(nullableDateOnlyConverter);

        modelBuilder.Entity<Subscription>()
            .Property(s => s.StartDate)
            .HasConversion(nullableDateOnlyConverter);

        modelBuilder.Entity<Subscription>()
            .Property(s => s.EndDate)
            .HasConversion(nullableDateOnlyConverter);

        modelBuilder.Entity<Company>()
            .Property(c => c.CreatedAt)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.ActionDate)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<Payment>()
            .Property(p => p.CreatedAt)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<ServiceRequest>()
            .Property(sr => sr.RequestDate)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<WorkflowInstance>()
            .Property(wi => wi.CreatedAt)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<WorkflowInstanceStep>()
            .Property(wis => wis.PerformedAt)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<AccountingTransaction>()
            .Property(t => t.TransactionDate)
            .HasDefaultValueSql("GETDATE()");

        modelBuilder.Entity<Notification>()
            .Property(n => n.CreatedAt)
            .HasDefaultValueSql("GETDATE()");
    }
}

public class AuditEntry
{
    public AuditEntry(EntityEntry entry)
    {
        Entry = entry;
    }

    public EntityEntry Entry { get; }
    public int? UserId { get; set; }
    public int? CompanyId { get; set; }
    public string? TableName { get; set; }
    public string? Action { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public Dictionary<string, object?> KeyValues { get; } = new();
    public Dictionary<string, object?> OldValues { get; } = new();
    public Dictionary<string, object?> NewValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();

    public bool HasTemporaryProperties => TemporaryProperties.Any();

    public AuditLog ToAuditLog()
    {
        var audit = new AuditLog
        {
            UserId = UserId,
            CompanyId = CompanyId,
            Action = Action,
            TableName = TableName,
            ActionDate = ViaReservaERP.AppTime.Now,
            IPAddress = IPAddress,
            UserAgent = UserAgent,
            RecordId = KeyValues.Count > 0 ? (int?)Convert.ToInt32(KeyValues.Values.First()) : null,
            OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues),
            NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues)
        };
        return audit;
    }
}
