namespace CodeWiki.Semantic;

public static class IfacePromptBuilder
{
    private const string SystemPrompt =
        "당신은 백엔드 서비스 구현 코드를 읽고 그 의미를 요약한다. " +
        "record_semantics 도구로만 답하며 item 하나만 만든다. key는 메서드 이름. " +
        "summary(동작 한 줄)·effects(부수효과)·caveats(주의점)만 채운다. " +
        "어떤 엔티티를 만지는지는 별도 결정론으로 알므로 추정하지 말라.";

    public static LlmRequest Build(string inputBundle, string methodName)
    {
        var user =
            $"다음 구현을 요약하라. key는 '{methodName}'.\n\n" +
            "```csharp\n" + inputBundle + "\n```";
        return new LlmRequest(SystemPrompt, user);
    }
}
