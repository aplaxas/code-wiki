using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;

namespace CodeWiki.Semantic;

public sealed class Neo4jGraphReader : IGraphReader, IAsyncDisposable
{
    private readonly IDriver _driver;
    public Neo4jGraphReader(IDriver driver) => _driver = driver;

    public VmDossierInput ReadVmDossier(string vmName) =>
        ReadVmDossierAsync(vmName).GetAwaiter().GetResult();

    public IfaceUnitInput ReadIfaceUnit(string ifaceMethodName) =>
        ReadIfaceUnitAsync(ifaceMethodName).GetAwaiter().GetResult();

    public IReadOnlyList<string> ListIfaceMethods(string interfaceName) =>
        ListIfaceMethodsAsync(interfaceName).GetAwaiter().GetResult();

    private async Task<IReadOnlyList<string>> ListIfaceMethodsAsync(string interfaceName)
    {
        await using var s = _driver.AsyncSession();
        var cur = await s.RunAsync(@"
            MATCH (i:Interface {name:$n})-[:DECLARES]->(m:Method)
            WHERE EXISTS { (m)<-[:IMPLEMENTS_METHOD]-(impl:Method)
                           WHERE impl.fullName STARTS WITH 'Torba.Service' }
            RETURN DISTINCT m.name AS name ORDER BY name",
            new { n = interfaceName });
        var rows = await cur.ToListAsync();
        return rows.Select(r => r["name"].As<string>()).ToList();
    }

    private async Task<VmDossierInput> ReadVmDossierAsync(string vmName)
    {
        await using var s = _driver.AsyncSession();
        var cur = await s.RunAsync(@"
            MATCH (vm:ViewModel {name:$vm})
            MATCH (vm)-[:DEFINES_COMMAND]->(:Command)-[:EXECUTES]->(h:Method)
            WHERE h.sourcePath IS NOT NULL
            RETURN vm.pk AS vmPk,
                   collect(DISTINCT {pk:h.pk, name:h.name, sp:h.sourcePath}) AS handlers",
            new { vm = vmName });
        var rows = await cur.ToListAsync();
        if (rows.Count == 0)
            throw new InvalidOperationException($"ViewModel not found in graph: {vmName}");
        var rec = rows[0];
        var handlers = rec["handlers"].As<List<Dictionary<string, object>>>();
        var refs = handlers
            .Select(h => new HandlerRef(h["pk"].As<string>(), h["name"].As<string>()))
            .ToList();
        // Handlers live in the same VM.cs file — derive VM.cs path from first handler's sourcePath
        var vmCsPath = handlers.Select(h => h["sp"].As<string>()).FirstOrDefault() ?? string.Empty;
        return new VmDossierInput(rec["vmPk"].As<string>(), vmCsPath, refs);
    }

    private async Task<IfaceUnitInput> ReadIfaceUnitAsync(string ifaceMethodName)
    {
        await using var s = _driver.AsyncSession();
        var cur = await s.RunAsync(@"
            MATCH (im:Method {name:$m})<-[:IMPLEMENTS_METHOD]-(impl:Method)
            WHERE impl.fullName STARTS WITH 'Torba.Service' AND impl.sourcePath IS NOT NULL
            OPTIONAL MATCH (impl)-[:CALLS]->(hlp:Method)
            WHERE hlp.sourcePath IS NOT NULL AND hlp.fullName STARTS WITH 'Torba.Service'
            RETURN im.pk AS ipk,
                   impl.sourcePath AS sp, impl.startLine AS sl, impl.endLine AS el,
                   collect(DISTINCT {sp:hlp.sourcePath, sl:hlp.startLine, el:hlp.endLine}) AS helpers",
            new { m = ifaceMethodName });
        var rows = await cur.ToListAsync();
        if (rows.Count == 0)
            throw new InvalidOperationException($"Interface method not found in graph: {ifaceMethodName}");
        var rec = rows[0];
        var slices = new List<SliceRef>
        {
            new(rec["sp"].As<string>(), ParseLine(rec["sl"]), ParseLine(rec["el"]))
        };
        foreach (var h in rec["helpers"].As<List<Dictionary<string, object>>>())
        {
            if (h.TryGetValue("sp", out var sp) && sp is not null)
                slices.Add(new SliceRef(
                    sp.As<string>(),
                    ParseLine(h["sl"]),
                    ParseLine(h["el"])));
        }
        // RootDir is intentionally empty — caller (Program) injects the Vanuatu root path
        return new IfaceUnitInput(rec["ipk"].As<string>(), "", slices);
    }

    /// <summary>
    /// Robust line-number parser: ndjson stores startLine/endLine as strings, so Neo4j may
    /// hold them as string properties. Tries long→int first (numeric prop), then falls back
    /// to string parsing. Both paths are covered to survive schema variations without needing
    /// a schema migration or re-load.
    /// </summary>
    private static int ParseLine(object value)
    {
        if (value is null) return 0;
        try { return (int)value.As<long>(); }
        catch { /* fall through */ }
        try { return value.As<int>(); }
        catch { /* fall through */ }
        return int.Parse(value.As<string>());
    }

    public ValueTask DisposeAsync() => _driver.DisposeAsync();
}
