namespace ControlPlane.Api.Models.Responses;

public sealed record ApiKeyCreatedResponse
{
    /// <summary>The unique identifier of the API key.</summary>
    public required Guid Id { get; init; }

    /// <summary>The human-readable name of the API key.</summary>
    public required string Name { get; init; }

    /// <summary>The full API key value. This is the only time the full key is returned — store it securely.</summary>
    public required string Key { get; init; }

    /// <summary>The key prefix for display purposes (e.g., "sk_abc12345").</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>When the API key was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>The role-based permissions assigned to this key.</summary>
    public required List<string> Permissions { get; init; }
}

public sealed record ApiKeyListItemResponse
{
    /// <summary>The unique identifier of the API key.</summary>
    public required Guid Id { get; init; }

    /// <summary>The human-readable name of the API key.</summary>
    public required string Name { get; init; }

    /// <summary>A masked preview of the key value (prefix****...****suffix).</summary>
    public required string KeyPreview { get; init; }

    /// <summary>The key prefix for display purposes.</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>When the API key was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the API key was last used, or null if never used.</summary>
    public DateTime? LastUsedAt { get; init; }

    /// <summary>The role-based permissions assigned to this key.</summary>
    public required List<string> Permissions { get; init; }

    /// <summary>Whether the API key has been revoked.</summary>
    public required bool IsRevoked { get; init; }
}
