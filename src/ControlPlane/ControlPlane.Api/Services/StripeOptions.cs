namespace ControlPlane.Api.Services;

/// <summary>Configuration for Stripe billing integration.</summary>
/// <remarks>
/// In production, set values via environment variables populated from AWS Secrets Manager:
///   STRIPE__SECRETKEY     — Stripe secret API key (sk_live_… or sk_test_…)
///   STRIPE__WEBHOOKSECRET — Stripe webhook signing secret (whsec_…)
/// Never commit real keys to source control.
/// </remarks>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Stripe secret API key. Populated at runtime from AWS Secrets Manager via STRIPE__SECRETKEY.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Stripe webhook signing secret used to verify incoming events. Populated via STRIPE__WEBHOOKSECRET.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
