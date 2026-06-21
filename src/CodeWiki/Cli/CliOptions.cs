namespace CodeWiki.Cli;

public sealed record CliOptions(string Verb, string? Solution, string? Output,
    string? Credentials, string? Ndjson, bool Wipe,
    string? Vm, string? Iface, string? Semantic, string? Model)
{
    public static CliOptions Parse(string[] args)
    {
        string verb = args.Length > 0 ? args[0] : "";
        string? sln = null, o = null, c = null, ndjson = null;
        string? vm = null, iface = null, semantic = null, model = null;
        bool wipe = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-s": case "--solution": if (++i < args.Length) sln = args[i]; break;
                case "-o": case "--output": if (++i < args.Length) o = args[i]; break;
                case "-c": case "--credentials": if (++i < args.Length) c = args[i]; break;
                case "--ndjson": if (++i < args.Length) ndjson = args[i]; break;
                case "--wipe": wipe = true; break;
                case "--vm": if (++i < args.Length) vm = args[i]; break;
                case "--iface": if (++i < args.Length) iface = args[i]; break;
                case "--semantic": if (++i < args.Length) semantic = args[i]; break;
                case "--model": if (++i < args.Length) model = args[i]; break;
            }
        }
        return new CliOptions(verb, sln, o, c, ndjson, wipe, vm, iface, semantic, model);
    }
}
