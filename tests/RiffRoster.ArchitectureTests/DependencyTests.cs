namespace RiffRoster.ArchitectureTests;

public sealed class DependencyTests
{
    [Fact]
    public void Domain_has_no_persistence_http_or_application_dependencies()
    {
        var names = typeof(ScheduleRules).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.DoesNotContain(names, n => n.StartsWith("Microsoft.AspNetCore") || n.Contains("EntityFramework") || n.StartsWith("Npgsql") || n.StartsWith("WebPush") || n.StartsWith("RiffRoster."));
    }
    [Fact]
    public void Application_points_inward_only()
    {
        var names = typeof(DataStore).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.Contains("RiffRoster.Domain", names);
        Assert.DoesNotContain(names, n => n.Contains("Infrastructure") || n.Contains("Api") || n.Contains("EntityFramework") || n.StartsWith("Microsoft.AspNetCore") || n.StartsWith("Npgsql"));
    }
}
