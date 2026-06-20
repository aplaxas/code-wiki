using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CodeWiki.Pipeline;

public interface IWorkspaceBuilder
{
    IEnumerable<Compilation> Build(string slnPath);
}
