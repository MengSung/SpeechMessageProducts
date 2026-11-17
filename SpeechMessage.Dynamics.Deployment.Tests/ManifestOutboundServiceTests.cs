/*
 * 檔案整體說明
 * 目的與定位：P7以獨立人工對照表保護沒有Host consumer的待辦來源不得列live。
 * 主要流程：真loader讀目前工作樹註冊清單，逐步驟／活動型別對應會建立的固定待辦類型；
 * 只有RichMenuGenerate未承接，來源必須dormant且有提案理由；QR／撥款／代送明確列為已承接。
 * 資源與隔離：唯讀本工作樹JSON，不讀秘密、不連組織、不建Host或背景工作；集合操作局部。
 * 這是離線清單守門，不代表清單項目已有Live實機證據；正式切換仍需pass證據與受審plan。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>未承接工作來源的切換分級必須由真清單證明安全。</summary>
public sealed class ManifestOutboundServiceTests
{
    /// <summary>精確型別對照，不以模組名稱猜工作種類；未承接的來源永遠不許live。</summary>
    [Fact]
    public void Manifest_StepsEnqueuingUnservedTaskType_MustBeDormant()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        Assert.NotNull(root); var document = RegistrationManifestLoader.LoadDocument(root!.FullName);
        var map = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["SpeechMessage.Dynamics.Plugins.Localization.ChurchLocaleChangedPlugin"] = 100000000,
            ["SpeechMessage.Dynamics.Plugins.Lessons.LessonLinkPlugin"] = 100000001,
            ["SpeechMessage.Dynamics.Plugins.Fees.FeeAllotPlugin"] = 100000002,
            ["SpeechMessage.Dynamics.Plugins.Messaging.OutboundLetterPlugin"] = 100000003,
            ["SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity"] = 100000001,
            ["SpeechMessage.Dynamics.Workflows.QrCodes.MeetingQrCodeActivity"] = 100000001,
            ["SpeechMessage.Dynamics.Workflows.QrCodes.PersonalQrCodeActivity"] = 100000001,
            ["SpeechMessage.Dynamics.Workflows.Fees.FeeAllotActivity"] = 100000002,
            ["SpeechMessage.Dynamics.Workflows.Lessons.StorLessonSigningActivity"] = 100000003,
            ["SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity"] = 100000003,
            ["SpeechMessage.Dynamics.Workflows.Fees.FeeNotifyActivity"] = 100000003
        };
        var sources = document.Assemblies.SelectMany(a => a.Steps).Where(s => map.ContainsKey(s.Type) && (s.Type != "SpeechMessage.Dynamics.Plugins.Localization.ChurchLocaleChangedPlugin" || s.Stage == "PostOperation")).ToArray();
        var served = new HashSet<int> { 100000001, 100000002, 100000003 };
        Assert.Contains(sources, s => map[s.Type] == 100000000);
        foreach (var activity in document.Assemblies.SelectMany(a => a.Activities).Where(a => map.ContainsKey(a.Type)))
            if (!served.Contains(map[activity.Type])) Assert.Equal("dormant", activity.Live!.Tier);
        foreach (var step in sources.Where(s => map[s.Type] == 100000000))
        { Assert.Equal("dormant", step.Live!.Tier); Assert.StartsWith("P7-PROP-001", step.Live.Reason, StringComparison.Ordinal); }
    }
}
