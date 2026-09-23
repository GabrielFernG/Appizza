using System.Reflection;

namespace Appizza.ArchitectureTests;

public sealed class PaymentDependencyRulesTests
{
    [Fact]
    public void HostsUseApplicationAndForbiddenReverseDependenciesAreAbsent()
    {
        var api = typeof(Appizza.Api.Phase7PaymentAttemptEndpoints).Assembly;
        var worker = typeof(Appizza.Worker.OutboxMonitorWorker).Assembly;
        var application = typeof(Appizza.Payments.Application.PaymentProcessingService).Assembly;
        var payments = typeof(Appizza.Modules.Payments.PaymentsModule).Assembly;
        var persistence = typeof(Appizza.Persistence.AppizzaDbContext).Assembly;
        Assert.Contains(application.GetName().Name, api.GetReferencedAssemblies().Select(x => x.Name));
        // The Worker is intentionally only graph-ready in this foundation; until
        // a payment BackgroundService is introduced, the compiler may omit the
        // unused Application assembly reference from its emitted metadata.
        var repoRoot = FindRepositoryRoot();
        var workerProject = File.ReadAllText(Path.Combine(repoRoot, "src", "Backend", "Appizza.Worker", "Appizza.Worker.csproj"));
        Assert.Contains("Appizza.Payments.Application.csproj", workerProject, StringComparison.Ordinal);
        Assert.DoesNotContain(api.GetName().Name, application.GetReferencedAssemblies().Select(x => x.Name));
        Assert.DoesNotContain(worker.GetName().Name, application.GetReferencedAssemblies().Select(x => x.Name));
        Assert.DoesNotContain(application.GetName().Name, payments.GetReferencedAssemblies().Select(x => x.Name));
        Assert.DoesNotContain(application.GetName().Name, persistence.GetReferencedAssemblies().Select(x => x.Name));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Appizza.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
