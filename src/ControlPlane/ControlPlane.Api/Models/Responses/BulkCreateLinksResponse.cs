namespace ControlPlane.Api.Models.Responses;

public sealed record BulkCreateLinksResponse
{
    /// <summary>Total number of links requested for creation.</summary>
    public int TotalRequested { get; init; }

    /// <summary>Number of links successfully created.</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of links that failed to create.</summary>
    public int Failed { get; init; }

    /// <summary>Per-link results with success status and error details for failures.</summary>
    public required IReadOnlyList<BulkLinkResult> Results { get; init; }
}

public sealed record BulkLinkResult
{
    /// <summary>The 0-based index of this link in the request list.</summary>
    public int Index { get; init; }

    /// <summary>Whether this link was created successfully.</summary>
    public bool Success { get; init; }

    /// <summary>The created link details, or null if creation failed.</summary>
    public CreateLinkResponse? Link { get; init; }

    /// <summary>Error message describing why creation failed, or null on success.</summary>
    public string? Error { get; init; }
}

public sealed record CsvExportResponse
{
    /// <summary>The raw CSV content (text/csv).</summary>
    public required string CsvContent { get; init; }

    /// <summary>Suggested filename for the CSV download.</summary>
    public required string FileName { get; init; }

    /// <summary>Total number of links exported.</summary>
    public int TotalLinks { get; init; }
}
