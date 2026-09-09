using System.Collections.Concurrent;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Agora.Tests.Integration;

internal sealed class ReportTestScenario : IDisposable
{
    private readonly AgoraApiFactory _base = new();
    public WebApplicationFactory<Program> App { get; }
    public HttpClient Admin { get; }
    public FrozenReportClock Clock { get; } = new();
    public ReportCommands Commands { get; } = new();

    private ReportTestScenario(Action<IServiceCollection>? configure = null)
    {
        App = _base.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILoggerProvider>(Commands);
            configure?.Invoke(services);
        }));
        Admin = App.CreateClient();
    }

    public static async Task<ReportTestScenario> Create(Action<IServiceCollection>? configure = null)
    {
        var scenario = new ReportTestScenario(configure);
        try
        {
            await scenario.Admin.AuthenticateAsAdminAsync();
            scenario.Clock.Instant = DateTimeOffset.UtcNow;
            return scenario;
        }
        catch { scenario.Dispose(); throw; }
    }

    public async Task Db(Func<AgoraDbContext, Task> action)
    {
        using var scope = App.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AgoraDbContext>());
    }

    public void Dispose() { Admin.Dispose(); App.Dispose(); _base.Dispose(); }
}

internal sealed class FrozenReportClock : TimeProvider
{
    public DateTimeOffset Instant { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Instant;
}

internal sealed class ReportCommands : ILoggerProvider
{
    public ConcurrentQueue<string> Statements { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Statements);
    public void Dispose() { }
    private sealed class Capture(string category, ConcurrentQueue<string> statements) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (category == "Microsoft.EntityFrameworkCore.Database.Command" && eventId.Id == 20101)
                statements.Enqueue(formatter(state, exception));
        }
    }
}
