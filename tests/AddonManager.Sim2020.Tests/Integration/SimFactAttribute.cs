namespace AddonManager.Sim2020.Tests.Integration;

/// <summary>A fact that runs only on a machine with an MSFS 2020 install and <c>MMAM_SIM_TESTS=1</c>. CI never sets the variable.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SimFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "MMAM_SIM_TESTS";

    public SimFactAttribute()
    {
        if (!Enabled)
            Skip = $"Set {EnvironmentVariable}=1 to run tests against the local MSFS install.";
    }

    public static bool Enabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";
}
