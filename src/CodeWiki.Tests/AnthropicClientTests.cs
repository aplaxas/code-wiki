using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CodeWiki.Semantic;
using Xunit;

namespace CodeWiki.Tests;

public class AnthropicClientTests
{
    sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body)
            });
    }

    static AnthropicClient Client(string body) =>
        new("test-key", "claude-haiku-4-5-20251001", new HttpClient(new StubHandler(body)));

    [Fact]
    public async Task EnrichAsync_truncated_toolUse_throws_clear_error_not_keyNotFound()
    {
        // max_tokens 절단 → tool_use input에 완성된 items 없음(빈 객체).
        // 가드 없으면 GetProperty("items")가 KeyNotFoundException("key not present")를
        // 불투명하게 던진다. 명확한 InvalidOperationException을 기대한다.
        var truncated = """
            {"stop_reason":"max_tokens","content":[
              {"type":"tool_use","name":"record_semantics","input":{}}
            ]}
            """;
        var client = Client(truncated);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.EnrichAsync(new LlmRequest("sys", "usr")));
        Assert.Contains("max_tokens", ex.Message);
    }

    [Fact]
    public async Task EnrichAsync_parses_wellFormed_toolUse()
    {
        var ok = """
            {"stop_reason":"tool_use","content":[
              {"type":"tool_use","name":"record_semantics","input":{"items":[
                {"key":"VM","summary":"화면","effects":null,"caveats":null}
              ]}}
            ]}
            """;
        var client = Client(ok);

        var fields = await client.EnrichAsync(new LlmRequest("sys", "usr"));

        Assert.Single(fields);
        Assert.Equal("VM", fields[0].Key);
        Assert.Equal("화면", fields[0].Summary);
    }
}
