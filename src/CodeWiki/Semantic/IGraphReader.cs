using System.Collections.Generic;

namespace CodeWiki.Semantic;

public sealed record HandlerRef(string Pk, string Name);
public sealed record VmDossierInput(string VmPk, string VmCsPath, IReadOnlyList<HandlerRef> Handlers);

public interface IGraphReader
{
    VmDossierInput ReadVmDossier(string vmName);
    // ReadIfaceUnit: Task 9에서 정의 — IfaceUnitInput 미정의로 주석 처리
    // IfaceUnitInput ReadIfaceUnit(string ifaceMethodName);
}
