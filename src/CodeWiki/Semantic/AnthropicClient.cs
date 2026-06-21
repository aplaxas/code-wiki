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
            max_tokens = 2048,
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

        var list = new List<LlmField>();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() != "tool_use") continue;
            var items = block.GetProperty("input").GetProperty("items");
            foreach (var it in items.EnumerateArray())
            {
                list.Add(new LlmField(
                    it.GetProperty("key").GetString() ?? "",
                    it.GetProperty("summary").GetString() ?? "",
                    it.TryGetProperty("effects", out var e) ? e.GetString() : null,
                    it.TryGetProperty("caveats", out var c) ? c.GetString() : null));
            }
        }
        return list;
    }
}
