using System;
using System.Linq;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

internal static class P0MySqlAcceptanceEnvironment
{
    public static string Missing(params string[] variables) => $"Set {string.Join(" / ", variables)} to dedicated MySQL 8 acceptance schemas.";

    public static bool IsMissing(params string[] variables) => variables.Any(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)));
}

public sealed class P0T4MySqlFactAttribute : FactAttribute
{
    public P0T4MySqlFactAttribute()
    {
        if (P0MySqlAcceptanceEnvironment.IsMissing("ADMINCORE_T4_MYSQL"))
            Skip = P0MySqlAcceptanceEnvironment.Missing("ADMINCORE_T4_MYSQL");
    }
}
