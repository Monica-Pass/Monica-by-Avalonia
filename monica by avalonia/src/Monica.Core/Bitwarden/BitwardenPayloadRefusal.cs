namespace Monica.Core.Bitwarden;

/// <summary>
/// Why a local row cannot travel to Bitwarden. The code rather than the sentence is what leaves this
/// assembly: the write-back queue reports a refusal to the screen, which has to say it in the language the
/// user chose, and a reason read out of an exception message would break the moment somebody reworded it.
/// Every member here is a state the user can either accept or change, because a refusal the user cannot act
/// on is only noise.
/// </summary>
public enum BitwardenPayloadRefusal
{
    /// <summary>
    /// The row is bound to a cipher whose current remote revision this device does not hold, so an update
    /// would have to be sent unguarded and could overwrite a change made elsewhere.
    /// </summary>
    MissingRemoteRevision = 1,

    /// <summary>Monica writes back logins, notes, cards and identities, and this row is none of those.</summary>
    UnsupportedShape,

    /// <summary>Bitwarden carries attachments in a separate upload API Monica does not speak.</summary>
    HasAttachments,

    /// <summary>The title is the one field a cipher cannot exist without.</summary>
    MissingTitle,

    /// <summary>
    /// The content has fields Bitwarden's shape has no place for - or child records Monica would have to
    /// invent - so writing it back would promise a round trip it cannot keep.
    /// </summary>
    UnsupportedContent,

    /// <summary>Larger than the payload ceiling the server itself accepts.</summary>
    PayloadTooLarge
}

/// <summary>
/// A protocol refusal that carries its reason. It subclasses the protocol exception rather than replacing
/// it so every existing handler - the queue that parks the entry, the tests that assert a shape is refused,
/// the coordinator that sanitizes messages - keeps catching exactly what it catches today.
/// </summary>
public sealed class BitwardenPayloadRefusalException : BitwardenProtocolException
{
    public BitwardenPayloadRefusalException(BitwardenPayloadRefusal reason, string message)
        : base(message) => Reason = reason;

    public BitwardenPayloadRefusalException(BitwardenPayloadRefusal reason, string message, Exception innerException)
        : base(message, innerException) => Reason = reason;

    public BitwardenPayloadRefusal Reason { get; }
}

/// <summary>
/// A refusal as the encoder found it: the code for the screen to translate, the sentence for the log to
/// keep. Written as a pair so the branch that decides and the words that explain it cannot drift apart.
/// </summary>
public sealed record BitwardenPayloadRefusalInfo(BitwardenPayloadRefusal Reason, string Message);

/// <summary>
/// One local row this device would like Bitwarden to take and cannot. Carries the title because the user's
/// first question is "which entry", and a reason code rather than a sentence because the screen answers in
/// the language the user chose. Deliberately carries no secret field: this list reaches the sync page and the
/// diagnostic log, and a refusal is about the shape of an entry, never its content.
/// </summary>
public sealed record BitwardenUnsyncableLocalChange(
    string Title,
    bool IsPassword,
    BitwardenPayloadRefusal Reason);
