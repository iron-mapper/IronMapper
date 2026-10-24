namespace Comparison.Cli.Ui;

/// <summary>
/// How the CLI may talk to the terminal. Plain: line by line, no live widgets (--plain, or stdout is not a terminal).
/// Interactive: prompts and menus are possible (not plain, and stdin is a terminal).
/// </summary>
internal static class Terminal
{
    public const string PlainFlag = "--plain";

    public static bool Plain { get; private set; }

    public static bool Interactive => !Plain && !Console.IsInputRedirected;

    /// <summary>Removes the global --plain flag from the arguments and sets <see cref="Plain"/>.</summary>
    public static string[] Configure(string[] args)
    {
        Plain = Console.IsOutputRedirected || args.Contains(PlainFlag, StringComparer.Ordinal);
        return args.Where(arg => arg != PlainFlag).ToArray();
    }
}
