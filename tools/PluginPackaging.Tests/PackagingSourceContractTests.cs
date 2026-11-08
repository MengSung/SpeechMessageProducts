/*
 * 檔案整體說明
 *
 * 目的與定位：P2-11 包裝來源契約的離線回歸測試，保護核定七輸入與兩個連結來源子集，
 * 不以 DLL 存在取代來源範圍及隔離檢查，也不載入產品、正式金鑰或外部服務。
 * 主要功能與流程：從目前 checkout 向上定位方案，讀取正式合併 targets 與子集專案 XML；
 * 原四輸入、複製來源、萬用字元或改 AssemblyName 都使測試失敗，不自動修正期待值。
 * 使用邊界與維護：只讀受版控 XML，XDocument 與字串限定單次測試生命週期；沒有 stream、
 * process、credential、可變靜態快取或背景工作。來源實際可執行性另由合併產物鏈測試證明。
 */
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SpeechMessage.Tools.PluginPackaging.Tests;

/// <summary>保護包裝專用投影而非放寬沙箱限制；讀取真實 targets，不用合成副本代替。</summary>
public sealed class PackagingSourceContractTests
{
    /// <summary>舊四輸入必須先紅；七輸入保留完整核心及兩個指定來源投影，禁止隱式 bin 全併。</summary>
    [Fact]
    public void Packaging_SevenExplicitInputs_UsesLinkedSourceSubsets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory!.FullName;
        var targets = XDocument.Load(Path.Combine(root, "build", "PluginMerge.targets"));
        var inputs = targets.Descendants("_SpeechMessageMergeInput").Select(x => (string?)x.Attribute("Include")).ToArray();
        Assert.Equal(7, inputs.Length);
        Assert.All(inputs, input => Assert.DoesNotContain("*", input!, StringComparison.Ordinal));
        foreach (var name in new[] { "Line.Messaging", "ToolUtility.Core" })
        {
            var path = Path.Combine(root, "build", name + ".PluginSubset", name + ".PluginSubset.csproj");
            var project = XDocument.Load(path);
            Assert.Equal(name, Assert.Single(project.Descendants("AssemblyName")).Value);
            var sources = project.Descendants("Compile").Where(x => x.Attribute("Include") is not null).ToArray();
            Assert.NotEmpty(sources);
            Assert.All(sources, source =>
            {
                Assert.NotNull(source.Attribute("Link"));
                Assert.DoesNotContain("*", source.Attribute("Include")!.Value, StringComparison.Ordinal);
                Assert.Contains("../../", source.Attribute("Include")!.Value, StringComparison.Ordinal);
            });
        }
    }
}
