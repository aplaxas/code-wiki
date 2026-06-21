using System.Collections.Generic;
using System.Linq;

namespace CodeWiki.Semantic;

public static class VmPromptBuilder
{
    public const string ViewModelKey = "__viewmodel__";

    private const string SystemPrompt =
        "당신은 WPF ViewModel 코드를 읽고 화면 동작의 의미를 요약한다. " +
        "record_semantics 도구로만 답한다. 각 item에 key/summary와, 해당되면 effects(부수효과)·caveats(주의점)를 채운다. " +
        "구조적 사실(어떤 엔티티를 만지는지 등)은 추정하지 말고 동작 의미만 한국어 한 줄로 요약한다. " +
        "필드는 summary·effects·caveats 셋뿐이다.";

    public static LlmRequest Build(string vmCsContent, IReadOnlyList<string> handlerNames)
    {
        var keys = new[] { ViewModelKey }.Concat(handlerNames);
        var user =
            "다음 ViewModel 파일을 요약하라.\n" +
            $"요약할 key 목록: {string.Join(", ", keys)}\n" +
            $"(key '{ViewModelKey}' = 이 화면 전체의 목적, 나머지 = 각 핸들러 메서드의 동작)\n\n" +
            "```csharp\n" + vmCsContent + "\n```";
        return new LlmRequest(SystemPrompt, user);
    }
}
