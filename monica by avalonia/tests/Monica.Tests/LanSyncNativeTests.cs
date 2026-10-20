using System.Text.Json;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class LanSyncNativeTests
{
    [Fact]
    public void Native_failures_retain_operation_and_size_without_request_content()
    {
        const string secret = "synthetic-private-password";
        var request = new
        {
            extra = new { operation = secret, password = secret },
            operation = "replica_scan_batch",
            handle = -1,
            entries = Array.Empty<object>()
        };

        var failure = Assert.Throws<LanSyncException>(() => LanSyncNative.Request(request));

        Assert.Equal("invalid_request", failure.Code);
        Assert.Equal("replica_scan_batch", failure.Operation);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(request).Length, failure.RequestBytes);
        Assert.DoesNotContain(secret, failure.ToString());
    }

    [Fact]
    public void Unknown_operation_values_are_not_copied_to_diagnostics()
    {
        const string privateValue = "synthetic-private-value";
        var failure = Assert.Throws<LanSyncException>(() => LanSyncNative.Request(new { operation = privateValue }));
        Assert.Equal("invalid_request", failure.Code);
        Assert.Equal("unknown", failure.Operation);
        Assert.DoesNotContain(privateValue, failure.ToString());
    }

    [Fact]
    public void Oversized_requests_report_the_size_limit_instead_of_invalid_request()
    {
        var failure = Assert.Throws<LanSyncException>(() => LanSyncNative.Request(new
        {
            operation = "replica_scan_batch",
            handle = 1,
            padding = new string('x', LanSyncNative.MaxRequestBytes)
        }));
        Assert.Equal("request_too_large", failure.Code);
        Assert.Equal("replica_scan_batch", failure.Operation);
        Assert.True(failure.RequestBytes > LanSyncNative.MaxRequestBytes);
    }
}
