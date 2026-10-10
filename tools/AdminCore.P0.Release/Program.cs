using MySqlConnector;
using ZhonTai.Admin.Core.Db;

return await P0ReleaseCommand.RunAsync(args);

internal static class P0ReleaseCommand
{
    private const string ConnectionVariable = "ADMINCORE_RELEASE_MYSQL";

    public static Task<int> RunAsync(string[] args)
    {
        var action = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action) || action is "help" or "--help" or "-h")
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        if (action is not ("plan" or "verify" or "apply" or "rollback" or "recover"))
        {
            Console.Error.WriteLine("ERROR: Unknown action. Use help to list supported commands.");
            return Task.FromResult(2);
        }

        var confirm = args.Any(argument => string.Equals(argument, "--confirm", StringComparison.OrdinalIgnoreCase));
        var version = ReadOption(args, "--version");
        var sensitiveAction = action is "apply" or "rollback" or "recover";
        if (sensitiveAction && !confirm)
        {
            Console.Error.WriteLine($"ERROR: {action} requires the explicit --confirm flag; no database changes were made.");
            return Task.FromResult(2);
        }
        if (action is "rollback" or "recover" && string.IsNullOrWhiteSpace(version))
        {
            Console.Error.WriteLine($"ERROR: {action} requires --version <version>; no database changes were made.");
            return Task.FromResult(2);
        }

        var connection = ReadOption(args, "--connection") ?? Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine($"ERROR: {action} requires --connection or {ConnectionVariable}.");
            return Task.FromResult(2);
        }

        var manifestPath = ReadOption(args, "--manifest") ?? FindDefaultManifest();

        try
        {
            manifestPath = Path.GetFullPath(manifestPath);
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("P0 migration manifest was not found.");
            var manifestRoot = Path.GetDirectoryName(manifestPath) ?? throw new InvalidOperationException("P0 migration manifest has no parent directory.");
            _ = P0MigrationManifest.Load(manifestPath).Forward(manifestRoot);
            var database = new MySqlConnectionStringBuilder(connection).Database;
            Console.WriteLine($"action={action}; target={(string.IsNullOrWhiteSpace(database) ? "configured-mysql" : database)}");

            var executor = new P0MigrationExecutor(connection, manifestRoot);
            switch (action)
            {
                case "plan":
                    var plan = executor.Plan(manifestPath);
                    Console.WriteLine($"historyExists={plan.HistoryExists}; current={(plan.PendingVersions.Count == 0 ? plan.TargetVersion : "not-at-target")}; target={plan.TargetVersion}; pending={string.Join(',', plan.PendingVersions)}; status=read-only");
                    break;
                case "verify":
                    executor.Verify(manifestPath);
                    Console.WriteLine("status=verified");
                    break;
                case "apply":
                    var result = executor.Apply(manifestPath, Environment.UserName);
                    Console.WriteLine($"target={result.TargetVersion}; applied={string.Join(',', result.AppliedVersions)}; lock=released; status={(result.AlreadyApplied ? "already-applied" : "applied")}");
                    break;
                case "rollback":
                    executor.Rollback(manifestPath, version!, Environment.UserName);
                    Console.WriteLine($"target={version}; lock=released; status=rolled-back");
                    break;
                case "recover":
                    executor.Recover(manifestPath, version!, Environment.UserName);
                    Console.WriteLine($"target={version}; lock=released; status=recovered");
                    break;
            }
            return Task.FromResult(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {Redact(exception.Message, connection)}");
            return Task.FromResult(1);
        }
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }

    private static string FindDefaultManifest()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "database", "mysql", "manifest.json");
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "database", "mysql", "manifest.json");
    }

    private static string Redact(string message, string connection) => message.Replace(connection, "[redacted]", StringComparison.Ordinal);

    private static void PrintHelp()
    {
        Console.WriteLine("Admin.Core P0 release tool (read-only by default)");
        Console.WriteLine("Commands: plan | verify | apply --confirm | rollback --version <version> --confirm | recover --version <version> --confirm");
        Console.WriteLine($"Connection: --connection <value> or {ConnectionVariable}; no connection value is printed.");
        Console.WriteLine("Optional: --manifest <path>");
    }
}
