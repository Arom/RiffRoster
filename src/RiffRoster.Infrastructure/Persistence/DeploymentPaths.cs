namespace RiffRoster;

public static class DeploymentPaths
{
    public static string Resolve(string path, string contentRoot) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
}
