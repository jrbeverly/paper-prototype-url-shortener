namespace ControlPlane.Api.Models.Responses;

public sealed record ProcessTrialsResponse
{
    /// <summary>Number of tenants processed (expired or notified) by this batch operation.</summary>
    public required int Processed { get; init; }
}
