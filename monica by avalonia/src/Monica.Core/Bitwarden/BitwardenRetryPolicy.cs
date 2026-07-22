using System.Net;

namespace Monica.Core.Bitwarden;

public static class BitwardenRetryPolicy
{
    public const int MaximumAttempts = 8;
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(6);

    public static BitwardenFailureClass ClassifyHttpStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => BitwardenFailureClass.Unauthorized,
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => BitwardenFailureClass.Conflict,
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity => BitwardenFailureClass.Validation,
            (HttpStatusCode)429 => BitwardenFailureClass.RateLimited,
            HttpStatusCode.RequestTimeout or
            >= HttpStatusCode.InternalServerError => BitwardenFailureClass.TransientNetwork,
            _ => BitwardenFailureClass.Permanent
        };

    public static BitwardenFailureClass ClassifyException(Exception exception) => exception switch
    {
        OperationCanceledException => BitwardenFailureClass.TransientNetwork,
        HttpRequestException => BitwardenFailureClass.TransientNetwork,
        TimeoutException => BitwardenFailureClass.TransientNetwork,
        _ => BitwardenFailureClass.Permanent
    };

    public static bool CanRetry(BitwardenFailureClass failureClass, int attemptCount) =>
        attemptCount < MaximumAttempts && failureClass is
            BitwardenFailureClass.TransientNetwork or BitwardenFailureClass.RateLimited;

    public static DateTimeOffset GetNextAttemptAt(
        DateTimeOffset now,
        int attemptCount,
        BitwardenFailureClass failureClass,
        TimeSpan? retryAfter = null)
    {
        if (!CanRetry(failureClass, attemptCount))
        {
            return now;
        }

        if (failureClass == BitwardenFailureClass.RateLimited && retryAfter is { } serverDelay)
        {
            return now + Clamp(serverDelay, TimeSpan.FromSeconds(1), MaximumDelay);
        }

        var exponent = Math.Clamp(attemptCount - 1, 0, 10);
        var seconds = Math.Min(MaximumDelay.TotalSeconds, Math.Pow(2, exponent) * 2);
        return now + TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
