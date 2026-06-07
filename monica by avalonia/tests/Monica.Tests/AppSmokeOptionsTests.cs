namespace Monica.Tests;

public sealed class AppSmokeOptionsTests
{
    private const string DefaultEnvName = "MONICA_SMOKE_UI_UNLOCK_PASSWORD";
    private const string CustomEnvName = "MONICA_SMOKE_UI_UNLOCK_PASSWORD_TEST";
    private const string MissingEnvName = "MONICA_SMOKE_UI_UNLOCK_PASSWORD_MISSING_TEST";

    [Fact]
    public void Smoke_unlock_env_reads_custom_environment_variable()
    {
        WithEnvironment(CustomEnvName, "custom password", () =>
        {
            var password = Monica.App.App.GetSmokeUiUnlockPassword(["--smoke-ui-unlock-env", CustomEnvName]);

            Assert.Equal("custom password", password);
        });
    }

    [Fact]
    public void Smoke_unlock_env_without_name_reads_default_environment_variable()
    {
        WithEnvironment(DefaultEnvName, "default password", () =>
        {
            var password = Monica.App.App.GetSmokeUiUnlockPassword(["--smoke-ui-unlock-env"]);

            Assert.Equal("default password", password);
        });
    }

    [Fact]
    public void Smoke_unlock_env_takes_precedence_over_legacy_argument()
    {
        WithEnvironment(CustomEnvName, "env password", () =>
        {
            var password = Monica.App.App.GetSmokeUiUnlockPassword([
                "--smoke-ui-unlock",
                "legacy password",
                "--smoke-ui-unlock-env",
                CustomEnvName
            ]);

            Assert.Equal("env password", password);
        });
    }

    [Fact]
    public void Smoke_unlock_legacy_argument_remains_supported()
    {
        var password = Monica.App.App.GetSmokeUiUnlockPassword(["--smoke-ui-unlock", "legacy password"]);

        Assert.Equal("legacy password", password);
    }

    [Fact]
    public void Smoke_unlock_env_missing_value_does_not_fall_back_to_legacy_argument()
    {
        WithEnvironment(MissingEnvName, null, () =>
        {
            var password = Monica.App.App.GetSmokeUiUnlockPassword([
                "--smoke-ui-unlock-env",
                MissingEnvName,
                "--smoke-ui-unlock",
                "legacy password"
            ]);

            Assert.Null(password);
        });
    }

    private static void WithEnvironment(string name, string? value, Action test)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
