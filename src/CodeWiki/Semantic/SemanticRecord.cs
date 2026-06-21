using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeWiki.Semantic;

public sealed record SemanticRecord(
    string Pk, string Summary, string? Effects, string? Caveats,
    string SummaryHash, string SummaryModel);

public static class SummaryHash
{
    public static string Of(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16];
}
