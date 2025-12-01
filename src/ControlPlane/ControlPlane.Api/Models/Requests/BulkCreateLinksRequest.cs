using FluentValidation;

namespace ControlPlane.Api.Models.Requests;


public sealed record BulkCreateLinksRequest
{
    /// <summary>The list of links to create. Each link is validated independently.</summary>
    public required IReadOnlyList<CreateLinkRequest> Links { get; init; }

    /// <summary>When true or when the list exceeds 1000 links, processing runs asynchronously. The response includes a job ID for status polling.</summary>
    public bool ProcessAsync { get; init; }
}

public class BulkCreateLinksRequestValidator : AbstractValidator<BulkCreateLinksRequest>
{
    public BulkCreateLinksRequestValidator()
    {
        RuleFor(x => x.Links)
            .NotEmpty().WithMessage("At least one link is required");

        // Per-item field validation is intentionally deferred to BulkLinkService so individual
        // link failures are reported inline (partial-success semantics) rather than rejecting
        // the entire request.
    }
}
