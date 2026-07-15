namespace Monica.Core.Services;

public static class VaultMasterPasswordPolicy
{
    public const int MinimumLength = 8;

    public static bool MeetsMinimumLength(string? password) =>
        password?.Length >= MinimumLength;
}
