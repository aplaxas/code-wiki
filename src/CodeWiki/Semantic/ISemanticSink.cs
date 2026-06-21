using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public interface ISemanticSink
{
    Task ApplySemanticAsync(IEnumerable<SemanticRecord> records);
}
