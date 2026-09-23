namespace Monica.App.Services;

/// <summary>
/// One row of the auto-type picker. A projection on purpose: the row has to name the account it is
/// about to send (that is what Android's list shows too), and carrying the entry itself would put a
/// password within reach of a stray binding. Nothing here is secret.
/// </summary>
public sealed record AutoTypeCandidate(long EntryId, string Title, string UserName)
{
    // Sortable text for the picker's filter box: what the user can see is what they can type.
    public string FilterText => $"{Title} {UserName}";
}
