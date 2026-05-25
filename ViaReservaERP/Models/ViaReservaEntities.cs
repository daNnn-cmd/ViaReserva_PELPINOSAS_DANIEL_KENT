using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ViaReservaERP.Models;

[Table("Roles")]
public class Role
{
    [Key]
    public int RoleId { get; set; }

    [Required]
    [MaxLength(50)]
    public string RoleName { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<ErpUser> Users { get; set; } = new List<ErpUser>();
}

[Table("Permissions")]
public class Permission
{
    [Key]
    public int PermissionId { get; set; }

    [Required]
    [MaxLength(100)]
    public string PermissionName { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

[Table("RolePermissions")]
public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }

    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

[Table("Companies")]
public class Company
{
    [Key]
    public int CompanyId { get; set; }

    [Required]
    [MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? SubscriptionStatus { get; set; } = "Active";

    [MaxLength(255)]
    public string? StripeCustomerId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public int? CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }
    public int? DeletedBy { get; set; }

    public bool IsDeleted { get; set; } = false;

    public ICollection<ErpUser> Users { get; set; } = new List<ErpUser>();
    public ICollection<RoomType> RoomTypes { get; set; } = new List<RoomType>();
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
    public ICollection<Guest> Guests { get; set; } = new List<Guest>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<ServiceCatalogItem> Services { get; set; } = new List<ServiceCatalogItem>();
    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<AccountingTransaction> Transactions { get; set; } = new List<AccountingTransaction>();
    public ICollection<Workflow> Workflows { get; set; } = new List<Workflow>();
    public ICollection<WorkflowInstance> WorkflowInstances { get; set; } = new List<WorkflowInstance>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}

[Table("Users")]
public class ErpUser
{
    [Key]
    public int UserId { get; set; }

    public int CompanyId { get; set; }
    public int RoleId { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? AvatarUrl { get; set; }

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public int? CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }
    public int? DeletedBy { get; set; }

    public bool IsDeleted { get; set; } = false;

    public Company? Company { get; set; }
    public Role? Role { get; set; }

    public ICollection<ServiceRequest> AssignedServiceRequests { get; set; } = new List<ServiceRequest>();
    public ICollection<WorkflowInstanceStep> WorkflowInstanceStepsPerformed { get; set; } = new List<WorkflowInstanceStep>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}

[Table("Notifications")]
public class Notification
{
    [Key]
    public int NotificationId { get; set; }

    public int UserId { get; set; }

    public int? CompanyId { get; set; }

    [MaxLength(150)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Message { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public ErpUser? User { get; set; }
}

[Table("RoomTypes")]
public class RoomType
{
    [Key]
    public int RoomTypeId { get; set; }

    public int CompanyId { get; set; }

    [MaxLength(100)]
    public string? TypeName { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? BasePrice { get; set; }

    public Company? Company { get; set; }
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}

[Table("Rooms")]
public class Room
{
    [Key]
    public int RoomId { get; set; }

    public int CompanyId { get; set; }
    public int RoomTypeId { get; set; }

    [MaxLength(20)]
    public string? RoomNumber { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; } = "Available";

    public Company? Company { get; set; }
    public RoomType? RoomType { get; set; }

    public bool IsDeleted { get; set; } = false;

    public ICollection<ReservationRoom> ReservationRooms { get; set; } = new List<ReservationRoom>();
}

[Table("Guests")]
public class Guest
{
    [Key]
    public int GuestId { get; set; }

    public int CompanyId { get; set; }

    public int? UserId { get; set; }

    [MaxLength(150)]
    public string? FullName { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = ViaReservaERP.AppTime.Now;
    public bool IsDeleted { get; set; } = false;

    public Company? Company { get; set; }

    public ErpUser? User { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();
}

[Table("Reservations")]
public class Reservation
{
    [Key]
    public int ReservationId { get; set; }

    public int CompanyId { get; set; }
    public int GuestId { get; set; }

    public DateOnly? CheckInDate { get; set; }
    public DateOnly? CheckOutDate { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; } = "Pending";

    [Column(TypeName = "decimal(10,2)")]
    public decimal? TotalAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Subtotal { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? TaxAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? ServiceCharge { get; set; }

    public DateTime CreatedAt { get; set; } = ViaReservaERP.AppTime.Now;

    public Company? Company { get; set; }
    public Guest? Guest { get; set; }

    public ICollection<ReservationRoom> ReservationRooms { get; set; } = new List<ReservationRoom>();
    public ICollection<ReservationService> ReservationServices { get; set; } = new List<ReservationService>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

[Table("ReservationRooms")]
public class ReservationRoom
{
    [Key]
    public int ReservationRoomId { get; set; }

    public int? ReservationId { get; set; }
    public int? RoomId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    public Reservation? Reservation { get; set; }
    public Room? Room { get; set; }
}

[Table("Services")]
public class ServiceCatalogItem
{
    [Key]
    public int ServiceId { get; set; }

    public int? CompanyId { get; set; }

    [MaxLength(150)]
    public string? ServiceName { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    public Company? Company { get; set; }

    public bool IsDeleted { get; set; } = false;

    public ICollection<ReservationService> ReservationServices { get; set; } = new List<ReservationService>();
    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();
}

[Table("ReservationServices")]
public class ReservationService
{
    [Key]
    public int ReservationServiceId { get; set; }

    public int? ReservationId { get; set; }
    public int? ServiceId { get; set; }

    public int? Quantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    public Reservation? Reservation { get; set; }
    public ServiceCatalogItem? Service { get; set; }
}

[Table("ServiceRequests")]
public class ServiceRequest
{
    [Key]
    public int RequestId { get; set; }

    public int? CompanyId { get; set; }
    public int? GuestId { get; set; }
    public int? ReservationId { get; set; }
    public int? ServiceId { get; set; }
    public int? AssignedTo { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    public DateTime RequestDate { get; set; }

    public Company? Company { get; set; }
    public Guest? Guest { get; set; }
    public Reservation? Reservation { get; set; }
    public ServiceCatalogItem? Service { get; set; }
    public ErpUser? AssignedToUser { get; set; }
}

[Table("Payments")]
public class Payment
{
    [Key]
    public int PaymentId { get; set; }

    public int? CompanyId { get; set; }
    public int? ReservationId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Amount { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(255)]
    public string? StripePaymentIntentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Company? Company { get; set; }
    public Reservation? Reservation { get; set; }
}

[Table("Transactions")]
public class AccountingTransaction
{
    [Key]
    public int TransactionId { get; set; }

    public int? CompanyId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Amount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Subtotal { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? TaxAmount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? ServiceCharge { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; }

    [MaxLength(255)]
    public string? Description { get; set; }

    public int? ReferenceId { get; set; }

    [MaxLength(50)]
    public string? ReferenceType { get; set; }

    public DateTime TransactionDate { get; set; }

    public Company? Company { get; set; }
}

[Table("SubscriptionPlans")]
public class SubscriptionPlan
{
    [Key]
    public int PlanId { get; set; }

    [MaxLength(100)]
    public string? PlanName { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    public int? DurationMonths { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}

[Table("Subscriptions")]
public class Subscription
{
    [Key]
    public int SubscriptionId { get; set; }

    public int? CompanyId { get; set; }
    public int? PlanId { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(255)]
    public string? StripeSubscriptionId { get; set; }

    public Company? Company { get; set; }
    public SubscriptionPlan? Plan { get; set; }
}

[Table("EnterpriseInquiries")]
public class EnterpriseInquiry
{
    [Key]
    public int EnterpriseInquiryId { get; set; }

    [Required]
    [MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string ContactPerson { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    public int NumberOfBranches { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Requirements { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? CustomWorkflowNeeds { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "New";

    public DateTime CreatedAt { get; set; } = ViaReservaERP.AppTime.Now;

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
}

[Table("Workflows")]
public class Workflow
{
    [Key]
    public int WorkflowId { get; set; }

    public int? CompanyId { get; set; }

    [MaxLength(150)]
    public string? Name { get; set; }

    [MaxLength(255)]
    public string? Description { get; set; }

    public Company? Company { get; set; }
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public ICollection<WorkflowInstance> Instances { get; set; } = new List<WorkflowInstance>();
}

[Table("WorkflowSteps")]
public class WorkflowStep
{
    [Key]
    public int StepId { get; set; }

    public int? WorkflowId { get; set; }
    public int? StepOrder { get; set; }
    public int? RoleId { get; set; }

    [MaxLength(100)]
    public string? ActionName { get; set; }

    public Workflow? Workflow { get; set; }
    public Role? Role { get; set; }

    public ICollection<WorkflowInstanceStep> InstanceSteps { get; set; } = new List<WorkflowInstanceStep>();
    public ICollection<EscalationRule> EscalationRules { get; set; } = new List<EscalationRule>();
}

[Table("WorkflowInstances")]
public class WorkflowInstance
{
    [Key]
    public int InstanceId { get; set; }

    public int? WorkflowId { get; set; }
    public int? CompanyId { get; set; }

    public int? ReferenceId { get; set; }

    [MaxLength(50)]
    public string? ReferenceType { get; set; }

    public int? CurrentStep { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public Workflow? Workflow { get; set; }
    public Company? Company { get; set; }

    public ICollection<WorkflowInstanceStep> Steps { get; set; } = new List<WorkflowInstanceStep>();
}

[Table("WorkflowInstanceSteps")]
public class WorkflowInstanceStep
{
    [Key]
    public int InstanceStepId { get; set; }

    public int? InstanceId { get; set; }
    public int? StepId { get; set; }

    [MaxLength(100)]
    public string? ActionTaken { get; set; }

    public int? PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }

    public WorkflowInstance? Instance { get; set; }
    public WorkflowStep? Step { get; set; }
    public ErpUser? PerformedByUser { get; set; }
}

[Table("EscalationRules")]
public class EscalationRule
{
    [Key]
    public int EscalationId { get; set; }

    public int? WorkflowId { get; set; }
    public int? StepId { get; set; }

    public int? EscalateAfterMinutes { get; set; }
    public int? EscalateToRoleId { get; set; }

    public Workflow? Workflow { get; set; }
    public WorkflowStep? Step { get; set; }
    public Role? EscalateToRole { get; set; }
}

[Table("AuditLogs")]
public class AuditLog
{
    [Key]
    public int AuditId { get; set; }

    public int? CompanyId { get; set; }
    public int? UserId { get; set; }

    [MaxLength(100)]
    public string? Action { get; set; }

    [MaxLength(100)]
    public string? TableName { get; set; }

    public int? RecordId { get; set; }

    [Column(TypeName = "varchar(max)")]
    public string? OldValues { get; set; }

    [Column(TypeName = "varchar(max)")]
    public string? NewValues { get; set; }

    [MaxLength(50)]
    public string? IPAddress { get; set; }

    [MaxLength(255)]
    public string? UserAgent { get; set; }

    public DateTime ActionDate { get; set; }

    public ErpUser? User { get; set; }
    public Company? Company { get; set; }
}
