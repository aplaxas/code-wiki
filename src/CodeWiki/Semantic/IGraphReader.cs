using System.Collections.Generic;

namespace CodeWiki.Semantic;

public sealed record HandlerRef(string Pk, string Name);
public sealed record VmDossierInput(string VmPk, string VmCsPath, IReadOnlyList<HandlerRef> Handlers);
public sealed record SliceRef(string SourcePath, int StartLine, int EndLine);
public sealed record IfaceUnitInput(string IfacePk, string RootDir, IReadOnlyList<SliceRef> Slices);

public interface IGraphReader
{
    VmDossierInput ReadVmDossier(string vmName);
    IfaceUnitInput ReadIfaceUnit(string ifaceMethodName);
}
