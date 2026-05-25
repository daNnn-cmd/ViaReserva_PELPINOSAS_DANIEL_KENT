namespace ViaReservaERP.Models.SuperAdmin;

public record ReservationReportRow(int ReservationId, string CompanyName, string GuestName, DateTime? CheckInDate, DateTime? CheckOutDate, string Status, decimal TotalAmount);

public record ServiceRequestReportRow(int RequestId, string CompanyName, string GuestName, string ServiceType, string AssignedStaff, string Status, DateTime RequestDateUtc);

public record RevenueReportRow(DateTime Date, string Source, string CompanyName, decimal Amount, string Status);

public record WorkflowInstanceReportRow(int InstanceId, string CompanyName, string WorkflowName, string ReferenceType, int? ReferenceId, string Status, DateTime CreatedAtUtc);

public record SubscriptionReportRow(int SubscriptionId, string CompanyName, string PlanName, DateTime? StartDate, DateTime? EndDate, string Status, decimal Price);

public record FinancialReportRow(DateTime Date, string CompanyName, string Type, string Description, decimal Amount);

public record AccountingTransactionReportRow(DateTime DateUtc, string CompanyName, string TransactionType, string RelatedModule, string CustomerOrGuest, string PaymentMethod, decimal Amount, string Status, string Source, int SourceId);

public record AuditLogReportRow(DateTime Date, string CompanyName, string UserName, string Action, string TableName, int? RecordId, string IpAddress);
