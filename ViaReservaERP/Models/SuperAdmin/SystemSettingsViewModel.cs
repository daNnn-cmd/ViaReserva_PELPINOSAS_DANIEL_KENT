namespace ViaReservaERP.Models.SuperAdmin;

public class SystemSettingsViewModel
{
    public string? SearchQuery { get; set; }

    public int ActiveCompanies { get; set; }
    public int ActiveUsers { get; set; }
    public int ActiveIntegrations { get; set; }
    public int SecurityAlertsLast7Days { get; set; }
    public int ActiveSubscriptionPlans { get; set; }
    public int WorkflowAutomations { get; set; }

    public MultiTenantCompanySettingsSummary CompanyOnboarding { get; set; } = new();
    public List<RoleAccessMonitoringRow> RoleAccess { get; set; } = new();
    public SecuritySettingsSummary Security { get; set; } = new();
    public WorkflowAutomationSettingsSummary Workflow { get; set; } = new();
    public SubscriptionSettingsSummary Subscription { get; set; } = new();
    public NotificationSettingsSummary Notifications { get; set; } = new();
    public List<SystemIntegrationStatusRow> Integrations { get; set; } = new();
    public AuditComplianceSettingsSummary Compliance { get; set; } = new();
    public BackupRecoverySettingsSummary Backup { get; set; } = new();
    public SystemHealthSummary Health { get; set; } = new();

    public List<SettingsAuditTrailRow> AuditTrail { get; set; } = new();
}

public class MultiTenantCompanySettingsSummary
{
    public int PendingActivationCompanies { get; set; }
    public int SuspendedCompanies { get; set; }
    public int TenantMaxUsers { get; set; }
    public double TenantAverageUsers { get; set; }
    public int PermissionsDefined { get; set; }
}

public class RoleAccessMonitoringRow
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int Users { get; set; }
    public int Permissions { get; set; }
}

public class SecuritySettingsSummary
{
    public int CookieSessionHours { get; set; }
    public bool MfaConfigured { get; set; }
    public string IpRestrictionMode { get; set; } = string.Empty;
    public string EncryptionMode { get; set; } = string.Empty;
    public string LoginAttemptControls { get; set; } = string.Empty;
}

public class WorkflowAutomationSettingsSummary
{
    public int Workflows { get; set; }
    public int WorkflowSteps { get; set; }
    public int EscalationRules { get; set; }
    public double AverageEscalationMinutes { get; set; }
}

public class BillingCycleDistributionRow
{
    public int? DurationMonths { get; set; }
    public int Plans { get; set; }
}

public class SubscriptionSettingsSummary
{
    public int Plans { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int TrialSubscriptions { get; set; }
    public List<BillingCycleDistributionRow> BillingCycles { get; set; } = new();
}

public class NotificationSettingsSummary
{
    public string EmailProvider { get; set; } = string.Empty;
    public int AlertsLast7Days { get; set; }
}

public class SystemIntegrationStatusRow
{
    public string IntegrationName { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
}

public class AuditComplianceSettingsSummary
{
    public int RetentionPolicyDays { get; set; }
    public string ComplianceMonitoring { get; set; } = string.Empty;
}

public class BackupRecoverySettingsSummary
{
    public string BackupMode { get; set; } = string.Empty;
    public string Scheduling { get; set; } = string.Empty;
    public string DisasterRecovery { get; set; } = string.Empty;
}

public class SystemHealthSummary
{
    public string DatabaseConnectivity { get; set; } = string.Empty;
    public string ApiUptime { get; set; } = string.Empty;
    public int ErrorsLast24Hours { get; set; }
    public int FailedPaymentsLast24Hours { get; set; }
}

public class SettingsAuditTrailRow
{
    public int AuditId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime WhenUtc { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}
