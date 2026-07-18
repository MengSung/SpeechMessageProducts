using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Configuration;

public sealed class RuntimeConfigurationSecretScanTests
{
    private static readonly string[] SensitiveKeyManifest =
    [
        "LineMessaging:Jesus:ChannelAccessToken",
        "LineMessaging:JesusBack:ChannelAccessToken",
        "LineLogin:ChannelSecret",
        "MiniApp:ChannelSecret",
        "CrmConnection:Username",
        "CrmConnection:Password",
        "LinePay:ChannelSecret",
        "Payment:Profiles:JesusTest:Credentials:ShopNo",
        "Payment:Profiles:JesusTest:Credentials:A1",
        "Payment:Profiles:JesusTest:Credentials:A2",
        "Payment:Profiles:JesusTest:Credentials:B1",
        "Payment:Profiles:JesusTest:Credentials:B2",
        "Payment:Profiles:JesusTest:Credentials:XKeyId",
        "Payment:Profiles:MyPayProduction:Credentials:Key",
        "Payment:Profiles:MyPayProduction:Credentials:IV",
        "Sinopac:A1",
        "Sinopac:A2",
        "Sinopac:B1",
        "Sinopac:B2",
        "Sinopac:XKeyID",
        "MyPay:Key"
    ];

    private static readonly string[] LegacySensitiveAliasManifest =
    [
        "Sandbox:ShopNo",
        "Sandbox:A1",
        "Sandbox:A2",
        "Sandbox:B1",
        "Sandbox:B2",
        "Sandbox:XKeyID"
    ];

    private static readonly Regex CommentedSensitiveAssignment = new(
        "^\\s*//+\\s*\"(?<key>Username|Password|Key|IV|A1|A2|B1|B2|XKeyID|XKeyId|ChannelSecret|ChannelAccessToken|ShopNo|StoreKey|StoreIV)\"\\s*:\\s*\"[^\"]+\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    [Fact]
    public void Current_repository_has_no_committed_literals_for_the_frozen_manifest()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ChurchReportProjectDirectory(), "appsettings.json")),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        var literalKeys = ScanNonEmptyLiteralKeys(document.RootElement, SensitiveKeyManifest);

        literalKeys.Should().BeEmpty(
            $"SecretLiteralCount={literalKeys.Count}/{SensitiveKeyManifest.Length}; scanner output contains keys only");
    }

    [Fact]
    public void Scanner_returns_key_paths_without_returning_literal_values()
    {
        using var document = JsonDocument.Parse("""
        {
          "LineMessaging": {
            "Jesus": {
              "ChannelAccessToken": "synthetic-fixture-literal"
            }
          }
        }
        """);

        var literalKeys = ScanNonEmptyLiteralKeys(
            document.RootElement,
            ["LineMessaging:Jesus:ChannelAccessToken"]);

        literalKeys.Should().Equal("LineMessaging:Jesus:ChannelAccessToken");
        literalKeys.Should().NotContain("synthetic-fixture-literal");
    }

    [Fact]
    public void Current_repository_has_no_committed_literals_for_legacy_aliases()
    {
        using var document = ParseAppSettings();

        var literalKeys = ScanNonEmptyLiteralKeys(
            document.RootElement,
            LegacySensitiveAliasManifest);

        literalKeys.Should().BeEmpty(
            $"LegacyAliasLiteralCount={literalKeys.Count}/{LegacySensitiveAliasManifest.Length}; scanner output contains keys only");
    }

    [Fact]
    public void Current_repository_has_no_commented_sensitive_assignments()
    {
        var findings = ScanCommentedSensitiveAssignments(ReadAppSettingsSource());

        findings.Should().BeEmpty(
            $"CommentedSensitiveLiteralCount={findings.Count}; scanner output contains line/key/category only");
    }

    [Fact]
    public void Scanner_detects_legacy_alias_and_comment_without_returning_values()
    {
        const string aliasFixture = "synthetic-alias-fixture";
        const string commentFixture = "synthetic-comment-fixture";
        const string source = """
        {
          "Sandbox": {
            "A1": "synthetic-alias-fixture"
          }
        }
        // "Password": "synthetic-comment-fixture"
        """;

        using var document = JsonDocument.Parse(
            source,
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        var aliasFindings = ScanNonEmptyLiteralKeys(document.RootElement, ["Sandbox:A1"]);
        var commentFindings = ScanCommentedSensitiveAssignments(source);
        var diagnostics = string.Join(
            "|",
            aliasFindings.Concat(commentFindings.Select(finding =>
                $"{finding.LineNumber}:{finding.Key}:{finding.Category}")));

        aliasFindings.Should().Equal("Sandbox:A1");
        commentFindings.Should().ContainSingle().Which.Should().Be(
            new SensitiveCommentFinding(6, "Password", "commented-literal"));
        diagnostics.Should().NotContain(aliasFixture);
        diagnostics.Should().NotContain(commentFixture);
    }

    private static IReadOnlyList<string> ScanNonEmptyLiteralKeys(
        JsonElement root,
        IEnumerable<string> keyPaths)
    {
        return keyPaths
            .Where(keyPath => !string.IsNullOrWhiteSpace(ReadString(root, keyPath)))
            .ToArray();
    }

    private static string? ReadString(JsonElement root, string keyPath)
    {
        var current = root;
        foreach (var segment in keyPath.Split(':', StringSplitOptions.None))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static IReadOnlyList<SensitiveCommentFinding> ScanCommentedSensitiveAssignments(
        string source)
    {
        return CommentedSensitiveAssignment
            .Matches(source)
            .Select(match => new SensitiveCommentFinding(
                LineNumber: source.Take(match.Index).Count(character => character == '\n') + 1,
                Key: match.Groups["key"].Value,
                Category: "commented-literal"))
            .ToArray();
    }

    private static JsonDocument ParseAppSettings()
    {
        return JsonDocument.Parse(
            ReadAppSettingsSource(),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
    }

    private static string ReadAppSettingsSource()
    {
        return File.ReadAllText(Path.Combine(ChurchReportProjectDirectory(), "appsettings.json"));
    }

    private static string ChurchReportProjectDirectory()
    {
        return Path.Combine(ProjectRoot(), "SpeechMessageProducts.ChurchReport");
    }

    private static string ProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }

    private sealed record SensitiveCommentFinding(
        int LineNumber,
        string Key,
        string Category);
}
