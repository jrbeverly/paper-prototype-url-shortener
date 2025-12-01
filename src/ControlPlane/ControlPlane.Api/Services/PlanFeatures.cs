namespace ControlPlane.Api.Services;

/// <summary>Feature flag string constants used for plan-based access control.</summary>
public static class PlanFeatures
{
    public const string ApiAccess = "api_access";
    public const string AnalyticsExport = "analytics_export";
    public const string AdvancedAnalytics = "advanced_analytics";
    public const string BulkImport = "bulk_import";
    public const string CustomQrCodes = "custom_qr_codes";
    public const string PasswordLinks = "password_links";
    public const string TeamSeats = "team_seats";
    public const string Sso = "sso";
    public const string WhiteLabel = "white_label";
    public const string PrioritySupport = "priority_support";
}
