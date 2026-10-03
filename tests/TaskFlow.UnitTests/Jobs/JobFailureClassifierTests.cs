using System.Net.Http;
using System.Text.Json;

using TaskFlow.Application.Jobs;

namespace TaskFlow.UnitTests.Jobs;

public sealed class JobFailureClassifierTests
{
    [Theory]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(ArgumentOutOfRangeException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(NotSupportedException))]
    public void Bad_input_is_permanent(Type type)
    {
        Exception exception = (Exception)Activator.CreateInstance(type, "bad input")!;

        Assert.True(JobFailureClassifier.IsPermanent(exception));
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    public void Anything_else_is_retryable(Type type)
    {
        Exception exception = (Exception)Activator.CreateInstance(type, "later")!;

        Assert.False(JobFailureClassifier.IsPermanent(exception));
    }
}
