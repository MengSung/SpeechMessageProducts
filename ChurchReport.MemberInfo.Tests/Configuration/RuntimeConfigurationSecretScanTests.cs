using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
}
