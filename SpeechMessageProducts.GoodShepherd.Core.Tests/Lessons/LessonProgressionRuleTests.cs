/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-07 設定式進階評估引擎的固定輸出、資料有效性及
 * profile 差異。所有 profile 與 input 都是合成值，不連 D365、LINE 或檔案系統。
 *
 * 主要功能與流程：以兩組不同門檻／核准設定的 profile 驗證同一規則能產生
 * 各自核定結果；另外掃描 Lessons 原始碼，確保不以組織名稱或組織代碼分支。
 * 測試沒有背景工作、timer、Session 或外部資源，沒有額外清理責任。
 *
 * 使用邊界與維護：本檔保護的是設定式核心契約，不把合成 profile 結果描述成
 * 舊程式實機結果；真正的 legacy-live-only 或 reading 基準仍由階段驗收資料
 * 另行記錄。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Lessons;

/// <summary>保護課程進階評估不依組織名稱分支且尊重設定差異。</summary>
public sealed class LessonProgressionRuleTests
{
    /// <summary>第一組設定的門檻與核准契約產生完成結果。</summary>
    [Fact]
    public void Lessons_Progression_TwoWingsProfile_MatchesLegacy()
    {
        var profile = new LessonProgressionProfile("profile-a", 3, true);
        Assert.Equal(LessonProgressionOutcome.Completed,
            LessonProgressionRule.Evaluate(profile, new(3, true, true)));
    }

    /// <summary>第二組設定可保留不同核准差異，而非被第一組行為覆蓋。</summary>
    [Fact]
    public void Lessons_Progression_SecondProfile_MatchesLegacy()
    {
        var profile = new LessonProgressionProfile("profile-b", 2, false);
        Assert.Equal(LessonProgressionOutcome.Completed,
            LessonProgressionRule.Evaluate(profile, new(2, false, true)));
        Assert.Equal(LessonProgressionOutcome.Blocked,
            LessonProgressionRule.Evaluate(new("profile-c", 2, true), new(2, false, true)));
    }

    /// <summary>Lessons 原始碼只可出現設定 key，不得出現組織名稱或代碼分支。</summary>
    [Fact]
    public void Lessons_Progression_NoChurchNameBranching()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null &&
            !File.Exists(Path.Combine(repository.FullName, "SpeechMessageProducts.sln")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var root = Path.Combine(repository!.FullName, "SpeechMessageProducts.GoodShepherd.Core", "Lessons");
        var forbidden = new[] { "TwoWings", "YangMeillc", "goodshepherdback" };
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var value in forbidden)
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>無效來源與零完成資料必須分別阻擋或維持未開始，不可猜測完成。</summary>
    [Fact]
    public void Evaluate_InvalidOrEmptyInput_FailsClosed()
    {
        var profile = new LessonProgressionProfile("profile-a", 1, false);
        Assert.Equal(LessonProgressionOutcome.Blocked,
            LessonProgressionRule.Evaluate(profile, new(1, true, false)));
        Assert.Equal(LessonProgressionOutcome.NotStarted,
            LessonProgressionRule.Evaluate(profile, new(0, true, true)));
    }
}
