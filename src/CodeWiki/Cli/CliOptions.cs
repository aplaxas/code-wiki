namespace CodeWiki.Cli;

public sealed record CliOptions(string Verb, string? Solution, string? Output,
    string? Credentials, string? Ndjson, bool Wipe)
{
    public static CliOptions Parse(string[] args)
    {
        string verb = args.Length > 0 ? args[0] : "";
        string? sln = null, o = null, c = null, ndjson = null;
        bool wipe = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-s":
                case "--solution":
                    sln = args[++i];
                    break;
                case "-o":
                case "--output":
                    o = args[++i];
                    break;
                case "-c":
                case "--credentials":
                    c = args[++i];
                    break;
                case "--ndjson":
                    ndjson = args[++i];
                    break;
                case "--wipe":
                    wipe = true;
                    break;
            }
        }

        return new CliOptions(verb, sln, o, c, ndjson, wipe);
    }
}
