/*
檔案整體說明

目的與定位：保護P5-13舊專案去向清冊與受版控程式的退役通知端點邊界；測試只讀目前工作樹，不載入舊DLL、不建立D365或LINE連線，不將來源移植誤稱實機相容。

主要功能與流程：獨立列出核定36個專案，核對清冊完整唯一、指定不移植理由、共用程式庫目的及雙向註冊legacy對照。以Git取得受版控程式檔，另取得工作樹明確刪除清單；只略過Git確認已刪除的路徑，其他缺檔仍失敗，存在的來源照常檢查退役Notify主機；合成惡例驗證偵測器確實能攔截端點。

使用邊界與維護：每案JsonDocument由using釋放，候選屬單次測試，沒有共用可變狀態、秘密或快取。Git子程序由本案唯一擁有，30秒取消時Kill整個樹並等待退出；雙管線同時讀取後排空、最後Dispose，不遺留背景工作。錯誤只回固定分類或相對路徑，不回顯來源本文；不掃父工作樹。新增去向必須有真實承接證據，不能捏造舊註冊。
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以離線清冊及版控來源鎖定36專案與不移植邊界，不修改部署metadata。</summary>
public sealed class LegacyDispositionTests
{
    private const string ExpectedProjects = "AllotPlugIn|AllotTlsWorkflow|AllotWorkflow|AppointmentPlugIn|AppointmentSigning|AppointmentSigningLibrary|AppointmentWorkflow|CalculateQrCodeScanNumberWorkflow|DiscipleLessonsPluIn|EquipmentWorkflow|ExampleWorkflow|FeePushLineWorkflow|HappyBirthdayWorkflow|Line.Messaging|LineMessagePlugIn|LineNotifyLibrary|LineUtility|NewPersonCareWorkflow|NewPersonCareWorkflow-ForDebug|PersonalQrCodeWorkflow|PresentRecordLinePlugIn|QrCodeWorkflow|RecursiveWorkflow|StorLessonSigningLibrary|StorLessonSigningWorkflow|StorLessonSignningPlugIn|SundayQrCodeWorkflow|TemplatePlugIn|ToolUtility|Trace|TwoWingsPlugIn|WeeklyReportLinePlugIn|WeeklyReportPlugIn|WeeklyReportWorkflow|WorkflowTemplate|YangMeillcStorLessonsPlugIn";

    /// <summary>缺項、重複身分或把零來源殼算入36項必須失敗；兩暫緩項不更動。</summary>
    [Fact]
    public void LegacyDisposition_Covers36ProjectsExactlyOnce()
    {
        using var document = ReadDisposition();
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var projects = root.GetProperty("projects").EnumerateArray().ToArray();
        var names = projects.Select(x => x.GetProperty("project").GetString()!).ToArray();
        Assert.Equal(36, names.Length);
        Assert.Equal(36, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ExpectedProjects.Split('|').Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
        Assert.Equal("Dynamics365Line", root.GetProperty("excludedShell").GetProperty("project").GetString());
        Assert.NotEmpty(root.GetProperty("excludedShell").GetProperty("reason").GetString()!);
        Assert.Equal("SmsPlugInClassLibrary|SnsPlugInDatabase".Split('|'),
            root.GetProperty("deferredProjects").EnumerateArray().Select(x => x.GetProperty("project").GetString()).ToArray());
        Assert.All(projects, row => Assert.Equal(
            row.GetProperty("project").GetString() == "LineNotifyLibrary" ? "Line.NotifyBot.csproj" : row.GetProperty("project").GetString() + ".csproj",
            row.GetProperty("projectFile").GetString()));
    }

    /// <summary>移植目標必須存在；非空舊註冊以assembly及type複合身分雙向精確比對。</summary>
    [Fact]
    public void LegacyDisposition_PortedEntries_MatchRegistrationLegacyFields()
    {
        using var disposition = ReadDisposition();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "d365/registration/plugins.registration.json")));
        var registrations = manifest.RootElement.GetProperty("assemblies").EnumerateArray()
            .SelectMany(a => a.GetProperty("steps").EnumerateArray().Concat(
                a.TryGetProperty("activities", out var activities) ? activities.EnumerateArray() : Enumerable.Empty<JsonElement>())).ToArray();
        var targets = registrations.Select(x => x.GetProperty("type").GetString()!).ToHashSet(StringComparer.Ordinal);
        var actual = registrations.SelectMany(row => row.GetProperty("legacy").EnumerateArray()
            .Select(old => Link(row.GetProperty("type").GetString()!, old))).ToHashSet(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in disposition.RootElement.GetProperty("projects").EnumerateArray())
        {
            var kind = row.GetProperty("disposition").GetString();
            Assert.Contains(kind, "ported|library|not-ported".Split('|'));
            Assert.NotEmpty(row.GetProperty("reason").GetString()!);
            if (kind == "ported")
            {
                Assert.NotEmpty(row.GetProperty("sourceTypes").EnumerateArray());
                Assert.NotEmpty(row.GetProperty("targets").EnumerateArray());
                foreach (var target in row.GetProperty("targets").EnumerateArray())
                    Assert.Contains(target.GetString()!, targets);
                foreach (var old in row.GetProperty("registrationLegacy").EnumerateArray())
                {
                    Assert.Contains(old.GetProperty("target").GetString(), row.GetProperty("targets").EnumerateArray().Select(x => x.GetString()));
                    declared.Add(Link(old.GetProperty("target").GetString()!, old));
                    Assert.Contains(old.GetProperty("type").GetString(), row.GetProperty("sourceTypes").EnumerateArray().Select(x => x.GetString()));
                }
            }
            else
            {
                Assert.Empty(row.GetProperty("registrationLegacy").EnumerateArray());
                if (kind == "library") Assert.NotEmpty(row.GetProperty("targets").EnumerateArray());
                else Assert.Empty(row.GetProperty("targets").EnumerateArray());
            }
        }
        Assert.NotEmpty(actual);
        Assert.Equal(actual.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));
    }

    /// <summary>指定退役項目不得包裝為已移植；理由與清冊讀取均不產生副作用。</summary>
    [Fact]
    public void LegacyDisposition_NotPorted_HasReason()
    {
        using var document = ReadDisposition();
        var rows = document.RootElement.GetProperty("projects").EnumerateArray().ToArray();
        var expected = new[] { "TemplatePlugIn", "ExampleWorkflow", "WorkflowTemplate", "NewPersonCareWorkflow-ForDebug", "LineNotifyLibrary", "Trace", "AppointmentSigning" };
        foreach (var name in expected)
        {
            var row = Assert.Single(rows, x => x.GetProperty("project").GetString() == name);
            Assert.Equal("not-ported", row.GetProperty("disposition").GetString());
            Assert.True(row.GetProperty("reason").GetString()!.Length >= 12);
        }
    }

    /// <summary>版控程式禁止退役端點；合成惡例須能偵測，Git失敗不能當零命中，且僅可略過Git確認已刪除的檔案。</summary>
    [Fact]
    public async Task Source_NoLineNotifyEndpoint()
    {
        var pattern = new Regex(@"notify-(api|bot)[.]line[.]me", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        Assert.Matches(pattern, "https://notify-" + "api.line.me/api/notify");
        Assert.Matches(pattern, "https://NOTIFY-" + "BOT.LINE.ME/oauth/authorize");
        Assert.DoesNotMatch(pattern, "https://api.line.me/v2/bot/message/push");
        var root = Root();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            },
        };
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add("-z");
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
        }
        Assert.Equal(0, process.ExitCode);
        var extensions = new HashSet<string>(".cs|.cshtml|.js|.ts|.ps1|.py|.cjs|.mjs|.jsx|.tsx|.razor|.html|.htm|.vb|.fs|.fsi|.sh|.bat|.cmd|.php|.java|.go|.cpp|.h|.sql|.css|.vue|.svelte".Split('|'), StringComparer.OrdinalIgnoreCase);
        var files = (await output).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => extensions.Contains(Path.GetExtension(x))).ToArray();
        var deleted = await GetGitDeletedPathsAsync(root);
        Assert.True(files.Length > 100);
        foreach (var relative in files)
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            Assert.StartsWith(root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                Assert.Contains(relative, deleted);
                continue;
            }

            Assert.False(pattern.IsMatch(File.ReadAllText(path)), "Retired notification endpoint: " + relative);
        }
    }

    /// <summary>取得Git明確辨識的工作樹刪除路徑；程序失敗時測試不得把缺檔當成安全略過。</summary>
    private static async Task<HashSet<string>> GetGitDeletedPathsAsync(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            },
        };
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add("--deleted");
        process.StartInfo.ArgumentList.Add("-z");
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
        }
        Assert.Equal(0, process.ExitCode);
        return (await output).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>以新JsonDocument借用單次清冊內容，呼叫案負責using釋放。</summary>
    private static JsonDocument ReadDisposition() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "d365/registration/legacy-disposition.json")));

    /// <summary>保留新目標、舊assembly與type三元身分，不按顯示名合併來源。</summary>
    private static string Link(string target, JsonElement old) => target + "|" + old.GetProperty("assembly").GetString() + "|" + old.GetProperty("type").GetString();

    /// <summary>沿本測試輸出找最近方案根，不越過目前工作樹查找外部舊來源。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Test repository root unavailable.");
    }
}
