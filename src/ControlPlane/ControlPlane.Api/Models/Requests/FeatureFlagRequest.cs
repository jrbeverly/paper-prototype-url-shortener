using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

/// <summary>
/// Creates a new feature flag. Key must follow the <c>feature.area.name</c> dot-notation convention,
/// e.g. <c>feature.links.csv_export</c>.
/// </summary>
public record CreateFeatureFlagRequest
{
    /// <summary>Unique key in dot-notation: lowercase letters, digits, dots, and underscores.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Flag type: "boolean", "percentage", "per_tenant", or "per_plan".</summary>
    public required string FlagType { get; init; }

    /// <summary>Global kill switch. Defaults to <c>true</c> (enabled).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Required when FlagType is "percentage". Value 0–100.</summary>
    public int? RolloutPercentage { get; init; }

    /// <summary>Required when FlagType is "per_tenant". Tenant IDs in the allow-list.</summary>
    public List<Guid>? EnabledTenantIds { get; init; }

    /// <summary>Required when FlagType is "per_plan". Plan IDs in the allow-list, e.g. ["pro", "team"].</summary>
    public List<string>? EnabledPlanIds { get; init; }
}

public sealed class CreateFeatureFlagRequestValidator : AbstractValidator<CreateFeatureFlagRequest>
{
    public CreateFeatureFlagRequestValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required.")
            .MaximumLength(100).WithMessage("Key cannot exceed 100 characters.")
            .Matches(@"^[a-z][a-z0-9_.]*[a-z0-9]$")
            .WithMessage("Key must start and end with a lowercase letter or digit, and contain only lowercase letters, digits, dots, and underscores.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.")
            .When(x => x.Description is not null);

        RuleFor(x => x.FlagType)
            .NotEmpty().WithMessage("FlagType is required.")
            .Must(t => Services.FlagTypes.All.Contains(t))
            .WithMessage($"FlagType must be one of: {string.Join(", ", Services.FlagTypes.All)}.");

        RuleFor(x => x.RolloutPercentage)
            .InclusiveBetween(0, 100).WithMessage("RolloutPercentage must be between 0 and 100.")
            .When(x => x.RolloutPercentage.HasValue);

        RuleFor(x => x.RolloutPercentage)
            .NotNull().WithMessage("RolloutPercentage is required for percentage flags.")
            .When(x => string.Equals(x.FlagType, Services.FlagTypes.Percentage, StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.EnabledTenantIds)
            .NotEmpty().WithMessage("EnabledTenantIds must contain at least one tenant ID for per_tenant flags.")
            .When(x => string.Equals(x.FlagType, Services.FlagTypes.PerTenant, StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.EnabledPlanIds)
            .NotEmpty().WithMessage("EnabledPlanIds must contain at least one plan ID for per_plan flags.")
            .When(x => string.Equals(x.FlagType, Services.FlagTypes.PerPlan, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Partially updates an existing feature flag. All fields are optional; omitted fields are unchanged.</summary>
public record UpdateFeatureFlagRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Set to <c>false</c> to trigger the kill switch (immediate disable).</summary>
    public bool? Enabled { get; init; }

    public string? FlagType { get; init; }
    public int? RolloutPercentage { get; init; }
    public List<Guid>? EnabledTenantIds { get; init; }
    public List<string>? EnabledPlanIds { get; init; }
}

public sealed class UpdateFeatureFlagRequestValidator : AbstractValidator<UpdateFeatureFlagRequest>
{
    public UpdateFeatureFlagRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name cannot be empty.")
            .MaximumLength(200).WithMessage("Name cannot exceed 200 characters.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.")
            .When(x => x.Description is not null);

        RuleFor(x => x.FlagType)
            .Must(t => t is null || Services.FlagTypes.All.Contains(t))
            .WithMessage($"FlagType must be one of: {string.Join(", ", Services.FlagTypes.All)}.");

        RuleFor(x => x.RolloutPercentage)
            .InclusiveBetween(0, 100).WithMessage("RolloutPercentage must be between 0 and 100.")
            .When(x => x.RolloutPercentage.HasValue);
    }
}
