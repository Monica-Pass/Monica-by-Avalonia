namespace Monica.App.ViewModels;

public sealed record SyncHealthDisplayItem(string Label, string Value, string Detail);
public sealed record WebDavBackupHistoryItem(string FileName, string Path, string DateString, string SizeText, DateTimeOffset? LastModified);
