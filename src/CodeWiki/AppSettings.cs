using System;
using System.IO;
using System.Text.Json;

namespace CodeWiki;

/// <summary>
/// 로컬 설정·비밀 로더. <c>appsettings.json</c>은 <c>.gitignore</c>됨(.mcp.json과 동일 패턴).
/// 우선순위: 환경변수 &gt; appsettings.json. 파일은 빌드 시 출력 디렉터리로 복사된다.
/// </summary>
public static class AppSettings
{
    private static JsonElement? _root;
    private static bool _loaded;

    private static JsonElement? Root()
    {
        if (_loaded) return _root;
        _loaded = true;
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            try { _root = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone(); }
            catch { _root = null; }
        }
        return _root;
    }

    /// <summary>section.key 문자열 값 조회(예: Get("Anthropic","ApiKey")). 없으면 null.</summary>
    public static string? Get(string section, string key)
    {
        if (Root() is { } root
            && root.TryGetProperty(section, out var s)
            && s.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    public static string? AnthropicApiKey =>
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } e ? e : Get("Anthropic", "ApiKey");

    public static string? VanuatuRoot =>
        Environment.GetEnvironmentVariable("VANUATU_ROOT") is { Length: > 0 } e ? e : Get("Vanuatu", "Root");
}
