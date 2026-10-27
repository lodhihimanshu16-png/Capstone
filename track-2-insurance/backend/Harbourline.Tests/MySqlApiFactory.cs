using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using DbUp;
using Harbourline.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

namespace Harbourline.Tests;

/// <summary>
/// A [Fact] that is skipped (reason "HARBOURLINE_TEST_MYSQL_ADMIN is not set") when no MySQL admin connection is
/// configured. With REQUIRE_MYSQL_TESTS=1 it is not skipped, and MySqlApiFactory then throws, so the test FAILS.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        var admin = Environment.GetEnvironmentVariable(MySqlApiFactory.AdminVariable);
        var required = Environment.GetEnvironmentVariable("REQUIRE_MYSQL_TESTS") == "1";
        if (string.IsNullOrWhiteSpace(admin) && !required)
            Skip = MySqlApiFactory.AdminVariable + " is not set";
    }
}

/// <summary>
/// Sees every SQL command the application sends. Can record them (AC-41), fail a chosen command (AC-32) and hold
/// writes at a gate (AC-33). Registered on the DbContext through AddInterceptors.
/// </summary>
public sealed class SqlProbe : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();
    private int _failures;

    public IReadOnlyList<string> Commands => _commands.ToArray();
    public int Failures => Volatile.Read(ref _failures);

    /// <summary>When it returns true for a command text, that command throws instead of running.</summary>
    public Func<string, bool>? FailWhen { get; set; }

    /// <summary>Awaited before every INSERT, UPDATE or DELETE. Used to make two requests overlap.</summary>
    public Func<Task>? BeforeWrite { get; set; }

    public void Reset() { while (_commands.TryDequeue(out _)) { } }

    private static bool IsWrite(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || t.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private async Task BeforeAsync(DbCommand command)
    {
        _commands.Enqueue(command.CommandText);
        var gate = BeforeWrite;
        if (gate != null && IsWrite(command.CommandText)) await gate();
        var fail = FailWhen;
        if (fail != null && fail(command.CommandText))
        {
            Interlocked.Increment(ref _failures);
            throw new InvalidOperationException("Forced test failure for command: " + command.CommandText);
        }
    }

    private void Before(DbCommand command) => BeforeAsync(command).GetAwaiter().GetResult();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Before(command); return result; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { await BeforeAsync(command); return result; }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Before(command); return result; }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { await BeforeAsync(command); return result; }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    { Before(command); return result; }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { await BeforeAsync(command); return result; }
}

/// <summary>Lets N callers meet before any of them goes on. Gives up after a timeout so a test cannot hang.</summary>
public sealed class Rendezvous
{
    private readonly int _parties;
    private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public Rendezvous(int parties) => _parties = parties;

    public int Arrived => Volatile.Read(ref _arrived);

    public async Task ArriveAsync(TimeSpan timeout)
    {
        if (Interlocked.Increment(ref _arrived) >= _parties) _all.TrySetResult();
        await Task.WhenAny(_all.Task, Task.Delay(timeout));
    }
}

/// <summary>
/// The API on a real, throw-away MySQL database (harbourline_test_&lt;guid&gt;), migrated by DbUp exactly as in
/// production (V001 to V006). Environment: see the header of ApiFactory.cs. The clock is a FakeClock.
/// </summary>
public sealed class MySqlApiFactory : WebApplicationFactory<Program>
{
    public const string AdminVariable = "HARBOURLINE_TEST_MYSQL_ADMIN";

    // The host reads the connection string while it is built. A process-wide environment variable is the only
    // input that is certain to be seen that early, so host creation is serialised and the variable restored.
    private static readonly object StartLock = new();
    private const string ConnectionVariable = "ConnectionStrings__Harbourline";

    private readonly string _admin;
    private bool _dropped;

    public string DatabaseName { get; }
    public string AppConnectionString { get; }
    public FakeClock Clock { get; } = new();
    public SqlProbe Probe { get; } = new();

    public MySqlApiFactory()
    {
        _admin = Environment.GetEnvironmentVariable(AdminVariable) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException(AdminVariable + " is not set");
        DatabaseName = "harbourline_test_" + Guid.NewGuid().ToString("N");
        ExecuteOn(_admin, $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4");
        AppConnectionString = new MySqlConnectionStringBuilder(_admin) { Database = DatabaseName }.ConnectionString;
    }

    /// <summary>Applies only the migrations V001 to V<paramref name="lastVersion"/> (same DbUp journal as the app).</summary>
    public void ApplyMigrationsUpTo(int lastVersion)
    {
        var result = DeployChanges.To
            .MySqlDatabase(AppConnectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly,
                name => Regex.Match(name, @"\.V(\d{3})__") is { Success: true } m && int.Parse(m.Groups[1].Value) <= lastVersion)
            .LogToNowhere()
            .Build()
            .PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException("Pre-migration failed", result.Error);
    }

    /// <summary>Starts the app (this runs the remaining migrations) and returns a client.</summary>
    public HttpClient StartClient()
    {
        lock (StartLock)
        {
            var previous = Environment.GetEnvironmentVariable(ConnectionVariable);
            Environment.SetEnvironmentVariable(ConnectionVariable, AppConnectionString);
            try { return CreateClient(); }
            finally { Environment.SetEnvironmentVariable(ConnectionVariable, previous); }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Any name except "Testing", so Program.cs uses MySQL and runs DbUp.
        builder.UseEnvironment("MySqlTest");
        builder.UseSetting("ConnectionStrings:Harbourline", AppConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Same registration as Program.cs, plus the probe.
            services.RemoveAll<DbContextOptions<HarbourlineDbContext>>();
            services.RemoveAll<HarbourlineDbContext>();
            services.AddDbContext<HarbourlineDbContext>(o => o
                .UseMySql(AppConnectionString, new MySqlServerVersion(new Version(8, 4, 0)))
                .AddInterceptors(Probe));
        });
    }

    public void Execute(string sql) => ExecuteOn(AppConnectionString, sql);

    public long Scalar(string sql)
    {
        using var connection = new MySqlConnection(AppConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? throw new InvalidOperationException("Query returned NULL: " + sql) : Convert.ToInt64(value);
    }

    /// <summary>True when the query's single value is SQL NULL.</summary>
    public bool ScalarIsNull(string sql)
    {
        using var connection = new MySqlConnection(AppConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() is null or DBNull;
    }

    public string ScalarText(string sql)
    {
        using var connection = new MySqlConnection(AppConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private static void ExecuteOn(string connectionString, string sql)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        command.ExecuteNonQuery();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _dropped) return;
        _dropped = true;
        MySqlConnection.ClearAllPools();
        ExecuteOn(_admin, $"DROP DATABASE IF EXISTS `{DatabaseName}`");
    }
}
