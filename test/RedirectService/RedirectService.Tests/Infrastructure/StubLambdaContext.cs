using Amazon.Lambda.Core;

namespace RedirectService.Tests.Infrastructure;

internal sealed class StubLambdaContext : ILambdaContext
{
    public string AwsRequestId => "test-request-id";
    public IClientContext ClientContext => null!;
    public string FunctionName => "test-function";
    public string FunctionVersion => "1";
    public ICognitoIdentity Identity => null!;
    public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789:function:test";
    public ILambdaLogger Logger { get; } = new StubLambdaLogger();
    public string LogGroupName => "/aws/lambda/test";
    public string LogStreamName => "test-stream";
    public int MemoryLimitInMB => 512;
    public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);

    public List<string> Warnings => ((StubLambdaLogger)Logger).Warnings;
}

internal sealed class StubLambdaLogger : ILambdaLogger
{
    public List<string> Warnings { get; } = [];

    public void Log(string message) { }
    public void LogLine(string message) { }
    public void Log(string level, string message) { }
    public void LogDebug(string message) { }
    public void LogDebug(string format, params object[] args) { }
    public void LogInformation(string message) { }
    public void LogInformation(string format, params object[] args) { }
    public void LogWarning(string message) => Warnings.Add(message);
    // Lambda SDK supports named placeholders ({Host}, not {0}); capture the raw template so tests can assert non-empty.
    public void LogWarning(string format, params object[] args) => Warnings.Add(format);
    public void LogError(string message) { }
    public void LogError(string format, params object[] args) { }
    public void LogCritical(string message) { }
    public void LogCritical(string format, params object[] args) { }
    public void LogTrace(string message) { }
    public void LogTrace(string format, params object[] args) { }
}
