using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.XRay.Recorder.Handlers.AwsSdk;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace BackupRestoreTest.Function;

/// <summary>
/// Automated restore test Lambda.
/// Scheduled quarterly by EventBridge Scheduler; validates that DynamoDB PITR
/// restore produces a queryable table with the correct schema and non-zero data.
/// Results are written to CloudWatch Logs and (on failure) published to SNS.
/// </summary>
public sealed class Function
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IAmazonSimpleNotificationService? _sns;
    private readonly string _sourceTableName;

    private static readonly string _version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private static readonly HashSet<string> _expectedAttributeNames =
        ["PK", "SK", "GSI1PK", "GSI1SK"];

    static Function()
    {
        AWSSDKHandler.RegisterXRayForAllServices();
    }

    /// <summary>Default constructor used by the Lambda managed runtime.</summary>
    public Function()
    {
        _dynamoDb = new AmazonDynamoDBClient();
        _sourceTableName = Environment.GetEnvironmentVariable("REDIRECTS_TABLE_NAME")
            ?? "redirects-prod";
        var snsTopicArn = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN");
        if (!string.IsNullOrEmpty(snsTopicArn))
            _sns = new AmazonSimpleNotificationServiceClient();
    }

    /// <summary>Constructor for tests; accepts injected dependencies.</summary>
    public Function(IAmazonDynamoDB dynamoDb, string sourceTableName, IAmazonSimpleNotificationService? sns = null)
    {
        _dynamoDb = dynamoDb;
        _sourceTableName = sourceTableName;
        _sns = sns;
    }

    /// <summary>
    /// Lambda handler — invoked by EventBridge Scheduler on quarterly cadence.
    /// </summary>
    public async Task<RestoreTestResult> HandleAsync(Stream input, ILambdaContext context)
    {
        var now = DateTime.UtcNow;
        var testTableName = $"{_sourceTableName}-dr-test-{now:yyyyMMdd-HHmmss}";
        var startedAt = now;

        try
        {
            context.Logger.LogInformation(
                "Restore test started: sourceTable={SourceTable} testTable={TestTable} version={Version}",
                _sourceTableName, testTableName, _version);

            // 1. Restore to point-in-time (30 seconds ago to ensure the restore timestamp
            //    is within the PITR window and all recent writes are included).
            var restoreTimestamp = now.AddSeconds(-30);
            var restoreStarted = DateTime.UtcNow;
            await RestoreTableAsync(testTableName, restoreTimestamp, context);

            // 2. Wait for the restored table to become ACTIVE.
            await WaitForTableActiveAsync(testTableName, context);
            var restoreCompleted = DateTime.UtcNow;
            var restoreDuration = (restoreCompleted - restoreStarted).TotalSeconds;

            // 3. Validate schema.
            var schemaCheck = await ValidateSchemaAsync(testTableName, context);

            // 4. Validate data (item count).
            var itemCount = await GetItemCountAsync(testTableName, context);

            // 5. Delete test table.
            await DeleteTableAsync(testTableName, context);

            var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
            var passed = schemaCheck && itemCount > 0;

            var result = new RestoreTestResult
            {
                TestId = Guid.NewGuid().ToString("N")[..8],
                Timestamp = now.ToString("O"),
                SourceTable = _sourceTableName,
                TestTable = testTableName,
                Passed = passed,
                RestoreDurationSeconds = restoreDuration,
                TotalDurationSeconds = elapsed,
                ItemCount = itemCount,
                SchemaValid = schemaCheck
            };

            EmitMetrics(context, result);

            context.Logger.LogInformation(
                "Restore test completed: passed={Passed} itemCount={ItemCount} schemaValid={SchemaValid} restoreDurationSec={RestoreDuration} totalDurationSec={TotalDuration}",
                passed, itemCount, schemaCheck, restoreDuration, elapsed);

            if (!passed)
            {
                var failureReason = !schemaCheck
                    ? $"Schema validation failed for table {testTableName}"
                    : $"Item count validation failed: expected > 0, got {itemCount}";

                await PublishFailureAsync(result, failureReason, context);
            }

            return result;
        }
        catch (Exception ex)
        {
            var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;

            context.Logger.LogError(ex,
                "Restore test failed: sourceTable={SourceTable} testTable={TestTable} elapsedSec={ElapsedSeconds}",
                _sourceTableName, testTableName, elapsed);

            // Attempt to clean up the test table even on failure.
            try
            {
                await DeleteTableAsync(testTableName, context);
            }
            catch (Exception cleanupEx)
            {
                context.Logger.LogWarning("Cleanup of test table {TestTable} failed: {Message}",
                    testTableName, cleanupEx.Message);
            }

            var failureResult = new RestoreTestResult
            {
                TestId = Guid.NewGuid().ToString("N")[..8],
                Timestamp = startedAt.ToString("O"),
                SourceTable = _sourceTableName,
                TestTable = testTableName,
                Passed = false,
                TotalDurationSeconds = elapsed,
                Error = ex.Message
            };

            await PublishFailureAsync(failureResult, $"Restore test exception: {ex.Message}", context);
            throw;
        }
    }

    // ── DynamoDB operations ───────────────────────────────────────────────

    private async Task RestoreTableAsync(string testTableName, DateTime restoreTimestamp, ILambdaContext context)
    {
        context.Logger.LogInformation(
            "Restoring table to point-in-time: source={SourceTable} target={TestTable} restoreTime={RestoreTime:O}",
            _sourceTableName, testTableName, restoreTimestamp);

        // Round to nearest second — DynamoDB PITR granularity is 1 second.
        var rounded = new DateTime(
            restoreTimestamp.Ticks - (restoreTimestamp.Ticks % TimeSpan.TicksPerSecond),
            restoreTimestamp.Kind);

        await _dynamoDb.RestoreTableToPointInTimeAsync(new RestoreTableToPointInTimeRequest
        {
            SourceTableName = _sourceTableName,
            TargetTableName = testTableName,
            RestoreDateTime = rounded
        });
    }

    private async Task WaitForTableActiveAsync(string tableName, ILambdaContext context)
    {
        context.Logger.LogInformation("Waiting for table {TableName} to become ACTIVE...", tableName);

        var sw = Stopwatch.StartNew();
        var pollInterval = TimeSpan.FromSeconds(10);
        var maxWait = TimeSpan.FromMinutes(30);

        while (sw.Elapsed < maxWait)
        {
            await Task.Delay(pollInterval);

            var response = await _dynamoDb.DescribeTableAsync(tableName);

            if (response.Table.TableStatus == TableStatus.ACTIVE)
            {
                context.Logger.LogInformation(
                    "Table {TableName} is ACTIVE after {Seconds:N0}s",
                    tableName, sw.Elapsed.TotalSeconds);
                return;
            }

            context.Logger.LogInformation(
                "Table {TableName} status: {Status} (elapsed: {Seconds:N0}s)",
                tableName, response.Table.TableStatus, sw.Elapsed.TotalSeconds);
        }

        throw new TimeoutException(
            $"Table {tableName} did not become ACTIVE within {maxWait.TotalMinutes:N0} minutes");
    }

    private async Task<bool> ValidateSchemaAsync(string tableName, ILambdaContext context)
    {
        context.Logger.LogInformation("Validating schema for table {TableName}...", tableName);

        var response = await _dynamoDb.DescribeTableAsync(tableName);
        var table = response.Table;

        var actualAttributes = new HashSet<string>(
            table.AttributeDefinitions.Select(a => a.AttributeName));

        var hasAllExpected = _expectedAttributeNames.IsSubsetOf(actualAttributes);

        if (!hasAllExpected)
        {
            var missing = string.Join(", ", _expectedAttributeNames.Except(actualAttributes));
            var extra = string.Join(", ", actualAttributes.Except(_expectedAttributeNames));
            context.Logger.LogWarning(
                "Schema mismatch for table {TableName}: missing=[{Missing}] extra=[{Extra}]",
                tableName, missing, extra);
        }

        // Verify GSI1 exists
        var gsi1Exists = table.GlobalSecondaryIndexes?.Any(
            gsi => gsi.IndexName == "GSI1") == true;

        if (!gsi1Exists)
            context.Logger.LogWarning("GSI1 index not found on table {TableName}", tableName);

        return hasAllExpected && gsi1Exists;
    }

    private async Task<long> GetItemCountAsync(string tableName, ILambdaContext context)
    {
        context.Logger.LogInformation("Counting items in table {TableName}...", tableName);

        var response = await _dynamoDb.DescribeTableAsync(tableName);
        var itemCount = response.Table.ItemCount;

        context.Logger.LogInformation(
            "Table {TableName} item count: {ItemCount}",
            tableName, itemCount);

        return itemCount;
    }

    private async Task DeleteTableAsync(string tableName, ILambdaContext context)
    {
        context.Logger.LogInformation("Deleting test table {TableName}...", tableName);

        try
        {
            await _dynamoDb.DeleteTableAsync(tableName);
            context.Logger.LogInformation("Test table {TableName} deleted", tableName);
        }
        catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException)
        {
            context.Logger.LogWarning("Test table {TableName} does not exist (already deleted)", tableName);
        }
    }

    // ── Notifications ─────────────────────────────────────────────────────

    private async Task PublishFailureAsync(RestoreTestResult result, string reason, ILambdaContext context)
    {
        if (_sns is null)
        {
            context.Logger.LogWarning("SNS client not configured; cannot publish failure notification");
            return;
        }

        var snsTopicArn = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN");
        if (string.IsNullOrEmpty(snsTopicArn))
        {
            context.Logger.LogWarning("SNS_TOPIC_ARN environment variable not set; cannot publish failure notification");
            return;
        }

        var subject = $"Restore test FAILED — {result.SourceTable}";
        var message = JsonSerializer.Serialize(new
        {
            result.TestId,
            result.Timestamp,
            result.SourceTable,
            result.TestTable,
            result.Passed,
            result.RestoreDurationSeconds,
            result.TotalDurationSeconds,
            result.ItemCount,
            result.SchemaValid,
            result.Error,
            Reason = reason,
            Version = _version
        }, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            await _sns.PublishAsync(new PublishRequest
            {
                TopicArn = snsTopicArn,
                Subject = subject,
                Message = message
            });

            context.Logger.LogInformation("Published failure notification to SNS topic {TopicArn}", snsTopicArn);
        }
        catch (Exception ex)
        {
            context.Logger.LogWarning("Failed to publish SNS notification: {Message}", ex.Message);
        }
    }

    // ── Metrics ───────────────────────────────────────────────────────────

    private static void EmitMetrics(ILambdaContext context, RestoreTestResult result)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var passedValue = result.Passed ? 1.0 : 0.0;
        var emf = $"{{\"_aws\":{{\"Timestamp\":{timestamp},\"CloudWatchMetrics\":[{{\"Namespace\":\"ShortIo/BackupRestoreTest\",\"Dimensions\":[[\"SourceTable\"]],\"Metrics\":[{{\"Name\":\"RestoreTestPassed\",\"Unit\":\"None\"}},{{\"Name\":\"RestoreDuration\",\"Unit\":\"Seconds\"}},{{\"Name\":\"ItemCount\",\"Unit\":\"Count\"}}]}}]}},\"SourceTable\":\"{result.SourceTable}\",\"RestoreTestPassed\":{passedValue},\"RestoreDuration\":{result.RestoreDurationSeconds},\"ItemCount\":{result.ItemCount},\"TestId\":\"{result.TestId}\"}}";
        Console.WriteLine(emf);
    }
}

/// <summary>
/// Result of an automated restore test run.
/// </summary>
public sealed record RestoreTestResult
{
    public required string TestId { get; init; }
    public required string Timestamp { get; init; }
    public required string SourceTable { get; init; }
    public required string TestTable { get; init; }
    public bool Passed { get; init; }
    public double RestoreDurationSeconds { get; init; }
    public double TotalDurationSeconds { get; init; }
    public long ItemCount { get; init; }
    public bool SchemaValid { get; init; }
    public string? Error { get; init; }
}
