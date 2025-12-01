using Amazon.Lambda.Core;

namespace AnalyticsService.Tests.Infrastructure;

/// <summary>Minimal ILambdaContext stub for unit tests.</summary>
internal sealed class TestLambdaContext : ILambdaContext
{
    public string AwsRequestId => "test-request-id";
    public IClientContext ClientContext => null!;
    public string FunctionName => "test-function";
    public string FunctionVersion => "1";
    public ICognitoIdentity Identity => null!;
    public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123:function:test";
    public ILambdaLogger Logger => new TestLambdaLogger();
    public string LogGroupName => "/aws/lambda/test";
    public string LogStreamName => "test-stream";
    public int MemoryLimitInMB => 256;
    public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
}

internal sealed class TestLambdaLogger : ILambdaLogger
{
    public void Log(string message) { }
    public void LogLine(string message) { }

    public void Log(string level, string message) { }

    // Forward all structured log calls to the no-op base.
    public void LogDebug(string message) { }
    public void LogDebug(string format, params object[] args) { }
    public void LogInformation(string message) { }
    public void LogInformation(string format, params object[] args) { }
    public void LogWarning(string message) { }
    public void LogWarning(string format, params object[] args) { }
    public void LogError(string message) { }
    public void LogError(string format, params object[] args) { }
    public void LogCritical(string message) { }
    public void LogCritical(string format, params object[] args) { }
    public void LogTrace(string message) { }
    public void LogTrace(string format, params object[] args) { }
}
