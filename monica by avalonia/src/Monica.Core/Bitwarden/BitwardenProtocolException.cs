namespace Monica.Core.Bitwarden;

public sealed class BitwardenProtocolException : Exception
{
    public BitwardenProtocolException(string message)
        : base(message)
    {
    }

    public BitwardenProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
