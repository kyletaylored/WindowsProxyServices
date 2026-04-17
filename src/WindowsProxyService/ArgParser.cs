namespace WindowsProxyService;

/// <summary>
/// Parses the --name / --all command-line arguments passed to WindowsProxyService.
/// Extracted as a static class so it can be covered by unit tests without spinning
/// up a full WebApplication.
/// </summary>
internal static class ArgParser
{
    internal readonly record struct Result(bool StartAll, IReadOnlyList<string> Names);

    /// <summary>
    /// Parses <paramref name="args"/> and returns the resolved StartAll flag and
    /// the list of explicitly-requested instance names.
    /// </summary>
    internal static Result Parse(string[] args)
    {
        var names    = new List<string>();
        var startAll = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--all", StringComparison.OrdinalIgnoreCase))
            {
                startAll = true;
            }
            else if (args[i].Equals("--name", StringComparison.OrdinalIgnoreCase))
            {
                // Consume all following values that don't begin with '--'
                for (var j = i + 1; j < args.Length && !args[j].StartsWith("--"); j++, i++)
                {
                    if (args[j] is "*" or "all") startAll = true;
                    else names.Add(args[j]);
                }
            }
        }

        return new Result(startAll, names);
    }
}
