using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace CodeWiki.Semantic;

public sealed class AnthropicClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicClient(string apiKey, string model, HttpClient http)
    {
        _http = http;
        _model = model;
        _http.BaseAddress ??= new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<IReadOnlyList<LlmField>> EnrichAsync(LlmRequest req)
    {
        var tool = new
        {
            name = "record_semantics",
            description = "각 코드 단위의 의미를 기록한다.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    items = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                key = new { type = "string" },
                                summary = new { type = "string" },
                                effects = new { type = "string" },
                                caveats = new { type = "string" }
                            },
                            required = new[] { "key", "summary" }
                        }
                    }
                },
                required = new[] { "items" }
            }
        };

        var body = new
        {
            model = _model,
            // 대형 ViewModel(커맨드 수십 개)은 항목이 많아 출력이 길다. 2048은 잘려
            // tool_use input의 items가 비어 파싱이 깨졌다. haiku 한도(64K) 안에서
            // 비-스트리밍 안전선(~16K)으로 올린다.
            max_tokens = 16000,
            system = new[]
            {
                new { type = "text", text = req.System,
                      cache_control = new { type = "ephemeral" } }
            },
            tools = new[] { tool },
            tool_choice = new { type = "tool", name = "record_semantics" },
            messages = new[]
            {
                new { role = "user", content = req.User }
            }
        };

        using var resp = await _http.PostAsJsonAsync("v1/messages", body);
        var payload = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Anthropic API {(int)resp.StatusCode} {resp.StatusCode}: {payload}");
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;

        // tool_use 블록의 input.items를 안전하게 찾는다. 응답이 잘리면(max_tokens)
        // items가 누락돼 가드 없는 GetProperty가 KeyNotFoundException("key not present")를
        // 불투명하게 던졌다 — 원인을 드러내는 명확한 예외로 바꾼다.
        var items = default(JsonElement);
        var found = false;
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var t) || t.GetString() != "tool_use") continue;
                if (block.TryGetProperty("input", out var input) &&
                    input.TryGetProperty("items", out items))
                {
                    found = true;
                    break;
                }
            }
        }
        if (!found)
            throw new InvalidOperationException(
                $"Anthropic 응답에서 record_semantics의 items를 찾지 못했습니다(stop_reason={stopReason ?? "?"}). " +
                "stop_reason=max_tokens면 출력이 잘린 것 — max_tokens를 늘리거나 대상을 더 작게 나누세요.");

        var list = new List<LlmField>();
        foreach (var it in items.EnumerateArray())
        {
            list.Add(new LlmField(
                it.GetProperty("key").GetString() ?? "",
                it.GetProperty("summary").GetString() ?? "",
                it.TryGetProperty("effects", out var e) ? e.GetString() : null,
                it.TryGetProperty("caveats", out var c) ? c.GetString() : null));
        }
        return list;
    }
}
