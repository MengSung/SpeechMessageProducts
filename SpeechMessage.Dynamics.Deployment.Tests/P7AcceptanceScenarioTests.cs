/*
檔案整體說明
目的與定位：P7驗收情境來源的離線安全契約，經產品strict reader讀取固定新目錄，不改原Golden期望。
主要流程：逐案驗證情境宣告的教會與更新／觀察alias一致，並依entrypoint對應manifest新版型別；
這避免R4連線後才發現來源格式錯誤、選錯同事件step，或將共享測試列誤作本輪建立資料清除。
資源與隔離：只讀版控合成JSON及manifest，reader自行關閉文件；沒有SDK、秘密、程序或背景工作。
決定性斷言：案例可完整解析、週報同事件step唯一對應、舊entrypoint映射到新版step、無推播且只觀察核定alias；
資料契約測試不代表R4實機證據。名單seed另依既有metadata及ChurchReport建立契約，
驗證連絡人名單的來源類型、靜態型態及非空目的，避免僅靠lookup排序就把不完整輸入送進SDK。
*/
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>驗收來源先離線通過安全reader，實機證據由後續R4/R6產生。</summary>
public sealed class P7AcceptanceScenarioTests
{
    /// <summary>固定驗收來源能通過strict reader；每筆期望只允許更新情境明列的教會。</summary>
    [Fact]
    public void P7_LocalizationCases_ParseAndOnlyWriteOwnedChurch()
    {
        var cases = ShadowScenarioFiles.LoadLive(Root(), "localization", true);
        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, cases.Select(item => item.Trigger.GetProperty("message").GetString()).Distinct().Count());
        foreach (var item in cases)
        {
            Assert.True(item.LiveShadow); Assert.False(item.IsActivity);
            Assert.Empty(item.ExpectedLineCalls.EnumerateArray());
            var churchAlias = item.Church.GetProperty("alias").GetString();
            Assert.Equal(churchAlias, Assert.Single(item.ExpectedWrites.EnumerateArray()).GetProperty("alias").GetString());
            foreach (var shared in item.Seed.EnumerateArray().Where(row =>
                row.GetProperty("alias").GetString() == churchAlias || row.GetProperty("entity").GetString() == "contact"))
                Assert.Empty(shared.GetProperty("fields").EnumerateObject());
            Assert.Equal(churchAlias, Assert.Single(item.Observe.EnumerateArray()).GetProperty("alias").GetString());
        }
    }
    /// <summary>
    /// 保護R4四個固定模組以entrypoint精確配對manifest新版step；appointments／attendance
    /// 用舊entrypoint對照Legacy，localization／weeklyreports用新版型別。週報兩步共用Update/entity，
    /// fault setup保留這個歧義，決定性斷言為正式事件候選中恰選一個且Step.Type正確。
    /// 此測試只讀合成／版控清單，不連D365或執行Plugin，無外部資源需要清理。
    /// </summary>
    [Fact]
    public void P7_ShadowEntrypointSelectsFormalStepTypeForEveryAcceptanceModule()
    {
        var document = RegistrationManifestLoader.LoadDocument(Root());
        foreach (var module in new[] { "appointments", "attendance", "localization", "weeklyreports" })
        foreach (var file in ShadowScenarioFiles.LoadLive(Root(), module, true).Where(item => item.LiveShadow))
        {
            var eventSteps = document.Assemblies.SelectMany(assembly => assembly.Steps)
                .Where(step => step.Module == module &&
                    step.Message == file.Trigger.GetProperty("message").GetString() &&
                    step.Entity == file.Trigger.GetProperty("entity").GetString())
                .ToArray();
            var selected = Assert.Single(eventSteps.Where(step => DeploymentCli.MatchesShadowEntrypoint(step, file)));
            Assert.NotEmpty(selected.Type);
            if (file.Basis == "new" || selected.Type == file.Entrypoint)
                Assert.Equal(file.Entrypoint, selected.Type);
            else
                Assert.Contains(selected.Legacy, reference => reference.Type == file.Entrypoint);
            if (module == "weeklyreports") Assert.Equal(2, eventSteps.Length);
        }

        var weekly = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "weeklyreports", true));
        var weeklyStep = document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Single(step => step.Module == "weeklyreports" && step.Type == weekly.Entrypoint);
        var oldEntryPoint = weekly with { Entrypoint = Assert.Single(weeklyStep.Legacy).Type };
        var mapped = Assert.Single(document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Where(step => step.Module == "weeklyreports" && DeploymentCli.MatchesShadowEntrypoint(step, oldEntryPoint)));
        Assert.Equal(weeklyStep.Type, mapped.Type);
    }

    /// <summary>四個 P7 情境排序後每筆 owned lookup 都只指向已建立或 shared alias。</summary>
    [Fact]
    public void P7Scenarios_AllSeeds_HaveNoUnresolvedOrderAfterSort()
    {
        foreach (var module in new[] { "appointments", "attendance", "localization", "weeklyreports" })
        {
            var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), module, true));
            var rows = file.Seed.EnumerateArray().ToArray();
            var shared = new HashSet<string>(StringComparer.Ordinal)
            {
                file.Church.GetProperty("alias").GetString()!, "p7-test-contact"
            };
            if (module == "weeklyreports") shared.Add("owner");
            var aliases = rows.Select(row => row.GetProperty("alias").GetString()!)
                .Where(alias => !shared.Contains(alias)).ToArray();
            var owned = aliases.ToHashSet(StringComparer.Ordinal);
            var dependencies = rows.Where(row => owned.Contains(row.GetProperty("alias").GetString()!))
                .ToDictionary(row => row.GetProperty("alias").GetString()!,
                    row => (IReadOnlyCollection<string>)References(row.GetProperty("fields"))
                        .Where(owned.Contains).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var ordered = ShadowSdkScenarioFactory.OrderSeedAliases(aliases, dependencies);
            var created = new HashSet<string>(shared, StringComparer.Ordinal);
            foreach (var alias in ordered)
            {
                Assert.All(dependencies[alias], dependency => Assert.Contains(dependency, created));
                created.Add(alias);
            }
        }
    }

    /// <summary>
    /// appointments 的 activity seed 必須包含當下 metadata 要求的 subject、開始及結束時間；
    /// 這是 Create 前的離線資料契約，不宣稱實機 Create 已通過。
    /// </summary>
    [Fact]
    public void P7Appointments_ActivitySeedContainsApplicationRequiredFields()
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "appointments", true));
        var record = Assert.Single(file.Seed.EnumerateArray(), row =>
            row.GetProperty("alias").GetString() == "record");
        var fields = record.GetProperty("fields");
        Assert.Equal("SMV-TEST-appointment", fields.GetProperty("subject").GetString());
        Assert.Equal("2026-11-15T04:00:00Z", fields.GetProperty("scheduledstart").GetString());
        Assert.Equal("2026-11-15T05:00:00Z", fields.GetProperty("scheduledend").GetString());
    }

    /// <summary>
    /// 保護四個可執行 Live 驗收情境中由即時 metadata 確認為 ApplicationRequired 的 seed 欄位。
    /// 故障輸入注入是移除指定合成 alias 的必要欄位；決定性斷言要求固定 reader 仍讀到該欄，
    /// 且安全測試名稱保持 SMV-TEST- 前綴。測試只讀版控 JSON，不建立 SDK、連線、資料或流程；
    /// 即使通過也不代表 Live 實測，失敗表示該 module 的 Live 準備必須拒絕而不可假綠。
    /// </summary>
    /// <param name="module">固定 P7 驗收情境模組。</param>
    /// <param name="alias">含必要欄位的合成 seed 別名。</param>
    /// <param name="entity">情境中對應的 Dataverse 實體。</param>
    /// <param name="requiredField">即時 metadata 核定為 ApplicationRequired 的欄位。</param>
    [Theory]
    [InlineData("localization", "text-1", "new_localizedtext", "new_name")]
    [InlineData("appointments", "channel", "new_linechannel", "new_name")]
    [InlineData("weeklyreports", "pastor", "contact", "lastname")]
    [InlineData("weeklyreports", "channel", "new_linechannel", "new_name")]
    public void P7LiveSeed_ApplicationRequiredField_IsSupplied(string module, string alias, string entity, string requiredField)
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), module, true));
        var row = Assert.Single(file.Seed.EnumerateArray(), seed =>
            seed.GetProperty("alias").GetString() == alias && seed.GetProperty("entity").GetString() == entity);
        var fields = row.GetProperty("fields");

        Assert.True(fields.TryGetProperty(requiredField, out var value),
            $"Seed '{alias}' for '{entity}' must supply ApplicationRequired field '{requiredField}'.");
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.StartsWith("SMV-TEST-", value.GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 保護attendance與weeklyreports送入SDK的合成名單建立契約：createdfromcode必須是連絡人類型2，
    /// type必須明列靜態名單false，purpose必須非空；依已匯出的list metadata及既有ChurchReport建立路徑取值。
    /// 缺欄位的原始情境是故障輸入，決定性斷言直接檢查正式strict reader讀出的seed，不建假SDK也不連組織。
    /// 所有JSON由reader以命令區域文件解析，測試無秘密、可寫共用狀態或需清理的遠端記錄；通過不等於實機Create成功。
    /// </summary>
    /// <param name="module">含有新建list seed的固定P7驗收模組。</param>
    [Theory]
    [InlineData("attendance")]
    [InlineData("weeklyreports")]
    public void P7ListSeeds_ContactMarketingListCreateContract_IsComplete(string module)
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), module, true));
        var lists = file.Seed.EnumerateArray().Where(row => row.GetProperty("entity").GetString() == "list").ToArray();
        Assert.NotEmpty(lists);
        foreach (var list in lists)
        {
            var fields = list.GetProperty("fields");
            Assert.True(fields.TryGetProperty("createdfromcode", out var source), "List seed must declare createdfromcode.");
            Assert.Equal(2, source.GetProperty("option").GetInt32());
            Assert.True(fields.TryGetProperty("type", out var kind), "List seed must declare type.");
            Assert.False(kind.GetBoolean());
            Assert.True(fields.TryGetProperty("purpose", out var purpose), "List seed must declare purpose.");
            Assert.False(string.IsNullOrWhiteSpace(purpose.GetString()));
        }
    }

    /// <summary>
    /// 保護weeklyreports所有合成contact都不送出D365明確標記為不可Create的contact.statecode；新聯絡人由平台套用預設啟用狀態。
    /// 任一別名漏查都會讓ShadowSdkMetadata.ConvertFields在準備階段拒絕整案；本測試逐列檢查正式strict reader讀到的所有contact seed。
    /// 使用的是版控合成JSON，無SDK、秘密或遠端記錄；失敗時不建立或清理資源，通過不代表R4實機結果。
    /// </summary>
    [Fact]
    public void P7WeeklyReportContactSeeds_NonCreatableStatecodeIsNotSent()
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "weeklyreports", true));
        var contacts = file.Seed.EnumerateArray()
            .Where(row => row.GetProperty("entity").GetString() == "contact")
            .ToArray();

        Assert.NotEmpty(contacts);
        Assert.All(contacts, contact =>
            Assert.False(contact.GetProperty("fields").TryGetProperty("statecode", out _),
                $"Contact seed '{contact.GetProperty("alias").GetString()}' must use the platform state default."));
    }

    /// <summary>
    /// 保護lessons、qrcodes、care及fees的Live情境必須與manifest tier雙向一致，
    /// 並以引用先於使用的順序檢查注入故障；決定性斷言涵蓋唯一註冊映射、lookup先建、
    /// 共享測試contact空欄及不可建立欄位。只讀合成JSON與manifest，不連Dataverse；
    /// 通過不表示活動XAML可執行，也不代表Live實測。
    /// </summary>
    [Fact]
    public void P7RemainingLiveModules_HaveOneSafeScenarioPerLiveRegistration()
    {
        var document = RegistrationManifestLoader.LoadDocument(Root());
        foreach (var module in new[] { "lessons", "qrcodes", "care", "fees" })
        {
            var cases = ShadowScenarioFiles.LoadLive(Root(), module, true);
            Assert.All(cases, item => Assert.True(item.LiveShadow));
            var steps = document.Assemblies.SelectMany(assembly => assembly.Steps)
                .Where(step => step.Module == module && step.Live?.Tier == "live").ToArray();
            var activities = document.Assemblies.SelectMany(assembly => assembly.Activities)
                .Where(activity => activity.Module == module).ToArray();
            Assert.Equal(steps.Length + activities.Count(activity => activity.Live?.Tier == "dormant" &&
                activity.Type is "SpeechMessage.Dynamics.Workflows.Care.ContactAnniversaryActivity" or
                    "SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity" or
                    "SpeechMessage.Dynamics.Workflows.QrCodes.MeetingQrCodeActivity" or
                    "SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity" or
                    "SpeechMessage.Dynamics.Workflows.QrCodes.PersonalQrCodeActivity" or
                    "SpeechMessage.Dynamics.Workflows.Fees.FeeAllotActivity"), cases.Count);
            foreach (var item in cases)
            {
                var matchingSteps = steps.Where(step => !item.IsActivity &&
                    step.Message == item.Trigger.GetProperty("message").GetString() &&
                    step.Entity == item.Trigger.GetProperty("entity").GetString() &&
                    (step.Type == item.Entrypoint || step.Legacy.Any(old => old.Type == item.Entrypoint))).ToArray();
                var matchingActivities = activities.Where(activity => item.IsActivity &&
                    (activity.Type == item.Entrypoint || activity.Legacy.Any(old => old.Type == item.Entrypoint))).ToArray();
                Assert.Equal(1, matchingSteps.Length + matchingActivities.Length);
                if (item.IsActivity)
                {
                    var activity = Assert.Single(matchingActivities);
                    Assert.Equal("dormant", activity.Live?.Tier);
                    Assert.Equal("P7-PROP-006 LIVE-PREP-09：Live 測試工具尚不支援流程活動的樣板建立；使用者 2026-11-19 核定改 dormant，未經實機 Live 驗證。",
                        activity.Live?.Reason);
                    Assert.Empty(activity.Live!.Evidence.File);
                    Assert.Empty(activity.Live.Evidence.Sha256);
                    Assert.Empty(activity.Live.Evidence.At);
                    Assert.Empty(activity.Live.Evidence.Result);
                }
                Assert.DoesNotContain(item.Seed.EnumerateArray(), row =>
                    row.GetProperty("entity").GetString() == "systemuser");
                Assert.DoesNotContain(item.Seed.EnumerateArray(), row =>
                    row.GetProperty("fields").EnumerateObject().Any(field =>
                        field.Name is "statecode" or "fullname" or "new_lineid"));
                Assert.Single(item.Seed.EnumerateArray(), row =>
                    row.GetProperty("entity").GetString() == "contact" &&
                    !row.GetProperty("fields").EnumerateObject().Any());
                var record = Assert.Single(item.Seed.EnumerateArray(), row =>
                    row.GetProperty("alias").GetString() == "record");
                Assert.Equal("account", item.Seed.EnumerateArray().Single(row =>
                    row.GetProperty("alias").GetString() == item.Church.GetProperty("alias").GetString())
                    .GetProperty("entity").GetString());
                Assert.NotEqual("systemuser", record.GetProperty("entity").GetString());

                var seedRows = item.Seed.EnumerateArray().ToArray();
                var positions = seedRows.Select((row, index) =>
                    (Alias: row.GetProperty("alias").GetString()!, Index: index))
                    .ToDictionary(row => row.Alias, row => row.Index, StringComparer.Ordinal);
                foreach (var row in seedRows)
                {
                    var alias = row.GetProperty("alias").GetString()!;
                    foreach (var dependency in References(row.GetProperty("fields")).Where(positions.ContainsKey))
                        Assert.True(positions[dependency] < positions[alias],
                            $"Seed '{alias}' must follow referenced seed '{dependency}'.");
                }
            }
        }
    }

    /// <summary>
    /// 把appointments情境中每個lookup參照當作seed依賴，保證被參照列先於引用列建立。
    /// 故障輸入為目前版控情境的次序；逐筆斷言在引用列前已建立所有本地alias依賴，
    /// 防止Live建立record時指向尚不存在的synthetic資料。只解析離線JSON，不連Dataverse。
    /// </summary>
    [Fact]
    public void P7Appointments_ReferencedSeedsAreCreatedBeforeDependents()
    {
        var scenario = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "appointments", true));
        var rows = scenario.Seed.EnumerateArray().ToArray();
        var positions = rows.Select((row, index) =>
            (Alias: row.GetProperty("alias").GetString()!, Index: index))
            .ToDictionary(row => row.Alias, row => row.Index, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var alias = row.GetProperty("alias").GetString()!;
            foreach (var dependency in References(row.GetProperty("fields")).Where(positions.ContainsKey))
                Assert.True(positions[dependency] < positions[alias],
                    $"Seed '{alias}' must follow referenced seed '{dependency}'.");
        }
    }

    /// <summary>
    /// 保護使用者核定的七個活動只降為dormant，不連帶改變同模組Plugin的live tier。
    /// 故障輸入為目前仍live或理由不同的版控manifest；決定性斷言逐項驗固定理由及空白
    /// pass證據，並確認lessons、appointments、fees的Plugin步驟仍可供live準備。
    /// 測試只解析本機JSON，不連D365、不建立資料或流程；通過不代表任何活動或Plugin實測。
    /// </summary>
    /// <param name="module">核定活動所屬模組。</param>
    /// <param name="activityType">唯一可套用此例外的完整活動型別。</param>
    [Theory]
    [InlineData("care", "SpeechMessage.Dynamics.Workflows.Care.ContactAnniversaryActivity")]
    [InlineData("lessons", "SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity")]
    [InlineData("appointments", "SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity")]
    [InlineData("qrcodes", "SpeechMessage.Dynamics.Workflows.QrCodes.MeetingQrCodeActivity")]
    [InlineData("qrcodes", "SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity")]
    [InlineData("qrcodes", "SpeechMessage.Dynamics.Workflows.QrCodes.PersonalQrCodeActivity")]
    [InlineData("fees", "SpeechMessage.Dynamics.Workflows.Fees.FeeAllotActivity")]
    public void P7ApprovedWorkflowActivities_AreDormantWithoutPassEvidence(string module, string activityType)
    {
        const string reason = "P7-PROP-006 LIVE-PREP-09：Live 測試工具尚不支援流程活動的樣板建立；使用者 2026-11-19 核定改 dormant，未經實機 Live 驗證。";
        var document = RegistrationManifestLoader.LoadDocument(Root());
        var activity = Assert.Single(document.Assemblies.SelectMany(assembly => assembly.Activities)
            .Where(item => item.Module == module && item.Type == activityType));

        Assert.Equal("dormant", activity.Live?.Tier);
        Assert.Equal(reason, activity.Live?.Reason);
        Assert.Empty(activity.Live!.Evidence.File);
        Assert.Empty(activity.Live.Evidence.Sha256);
        Assert.Empty(activity.Live.Evidence.At);
        Assert.Empty(activity.Live.Evidence.Result);

        var liveSteps = document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Where(step => step.Module == module && step.Live?.Tier == "live").ToArray();
        if (module is "appointments" or "fees" or "lessons")
            Assert.NotEmpty(liveSteps);
        Assert.All(liveSteps, step => Assert.Equal("live", step.Live?.Tier));
    }

    private static IEnumerable<string> References(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("ref", out var reference) && reference.ValueKind == JsonValueKind.String)
                yield return reference.GetString()!;
            foreach (var property in value.EnumerateObject())
                foreach (var nested in References(property.Value)) yield return nested;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                foreach (var nested in References(item)) yield return nested;
        }
    }

    /// <summary>由本原始碼位置取得本checkout，不採程序CWD或其他worktree。</summary>
    private static string Root([CallerFilePath] string path = "") => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
}
