namespace Monica.Core.Bitwarden;

/// <summary>
/// Not sealed so <see cref="BitwardenPayloadRefusalException"/> can ride along with the code the
/// encoder already decided on. Every handler keeps catching this type.
/// </summary>
public class BitwardenProtocolException : Exception
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
