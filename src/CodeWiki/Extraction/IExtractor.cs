using CodeWiki.Model;

namespace CodeWiki.Extraction;

public interface IExtractor
{
    void Extract(ExtractionContext ctx, Graph graph);
}
