using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;

namespace RedirectService.Tests.Infrastructure;

/// <summary>
/// Controllable DynamoDB stub for unit tests.
/// Overrides <see cref="GetItemAsync"/> and <see cref="UpdateItemAsync"/>
/// (the methods used by <c>DynamoDbRedirectRepository</c>) and records the last request for assertion.
/// </summary>
internal sealed class StubDynamoDB : AmazonDynamoDBClient
{
    private GetItemResponse _response = new();
    private Exception? _exception;
    private UpdateItemResponse _updateResponse = new();
    private Exception? _updateException;
    private bool _updateShouldThrowConditional;

    /// <summary>The last <see cref="GetItemRequest"/> received by this stub.</summary>
    public GetItemRequest? LastRequest { get; private set; }

    /// <summary>The last <see cref="UpdateItemRequest"/> received by this stub.</summary>
    public UpdateItemRequest? LastUpdateRequest { get; private set; }

    public StubDynamoDB()
        : base(
            new BasicAWSCredentials("test-key", "test-secret"),
            new AmazonDynamoDBConfig { ServiceURL = "http://localhost:1", MaxErrorRetry = 0 })
    {
    }

    /// <summary>Configures the stub to return the given response on the next call.</summary>
    public void Returns(GetItemResponse response) => _response = response;

    /// <summary>Configures the stub to throw the given exception on the next call.</summary>
    public void Throws(Exception exception) => _exception = exception;

    /// <summary>Configures the stub to throw a <see cref="ConditionalCheckFailedException"/> on the next <see cref="UpdateItemAsync"/> call.</summary>
    public void SetUpdateConditionalCheckFails(bool fails = true) => _updateShouldThrowConditional = fails;

    /// <summary>Configures the stub to throw the given exception on the next <see cref="UpdateItemAsync"/> call.</summary>
    public void SetUpdateThrows(Exception exception) => _updateException = exception;

    public override Task<GetItemResponse> GetItemAsync(GetItemRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (_exception is not null)
            throw _exception;
        return Task.FromResult(_response);
    }

    public override Task<UpdateItemResponse> UpdateItemAsync(UpdateItemRequest request, CancellationToken cancellationToken = default)
    {
        LastUpdateRequest = request;
        if (_updateException is not null)
            throw _updateException;
        if (_updateShouldThrowConditional)
            throw new ConditionalCheckFailedException("Condition check failed");
        return Task.FromResult(_updateResponse);
    }
}
