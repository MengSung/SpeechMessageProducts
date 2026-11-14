/*
檔案整體說明
目的與定位：P5-03b 活動測試流程的離線安全核心，負責精準改寫既有 workflow XAML 的核定活動參照，以及管理一次測試流程的失敗清理。
主要功能與流程：以禁止 DTD／外部資源的 XML reader 解析流程定義，只修改 AssemblyQualifiedName 與對應的 namespace／local name；ProbeProcessOwner 依建立、啟用、執行後的 finally 順序停用並刪除測試流程。
信任邊界與資源擁有權：XML、流程名稱與委派皆為單次 operation-local 值，不執行 XAML、不保存 SDK／使用者資料、不建立背景工作；清理使用 CancellationToken.None 的同步委派，避免 caller 取消留下測試流程。
適用範圍與驗證：本檔只提供離線改寫／生命週期契約，不宣稱已在 goodshepherdback 執行活動；ActivityProbeTests 以合成 XML 與故障委派驗證精準變更及 finally 清理。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>活動流程探針的純 XML 改寫與有界清理 owner。</summary>
internal static class ActivityProbe
{
    private const long MaxXmlCharacters = 1024 * 1024;

    /// <summary>只改核定活動節點參照，其他流程節點、屬性與文字保持不變；不執行 XML 內的活動。</summary>
    internal static string RewriteOnlyActivityReference(
        string xaml, string oldAssembly, string oldType, string newAssembly, string newType)
    {
        if (string.IsNullOrWhiteSpace(xaml) || newAssembly.Length > 256 || newType.Length > 256)
            throw InvalidDefinition();
        var document = Parse(xaml);
        var oldQualified = oldType + ", " + oldAssembly;
        var newQualified = newType + ", " + newAssembly;
        var oldNamespace = NamespaceOf(oldType);
        var newNamespace = NamespaceOf(newType);
        var changed = 0;
        foreach (var element in document.Descendants())
        {
            var qualified = element.Attribute("AssemblyQualifiedName");
            if (string.Equals(qualified?.Value, oldQualified, StringComparison.Ordinal))
            {
                qualified!.Value = newQualified;
                changed++;
            }
            if (string.Equals(element.Name.LocalName, LocalNameOf(oldType), StringComparison.Ordinal) &&
                string.Equals(element.Name.NamespaceName, "clr-namespace:" + oldNamespace + ";assembly=" + oldAssembly, StringComparison.Ordinal))
            {
                element.Name = XName.Get(LocalNameOf(newType),
                    "clr-namespace:" + newNamespace + ";assembly=" + newAssembly);
                changed++;
            }
        }
        if (changed == 0) throw InvalidDefinition();
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>解析有界 workflow XML，拒絕 DTD、外部資源、超深結構及格式錯誤。</summary>
    private static XDocument Parse(string xaml)
    {
        if (xaml.Length > MaxXmlCharacters) throw InvalidDefinition();
        try
        {
            using var text = new StringReader(xaml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlCharacters,
                MaxCharactersFromEntities = 0
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            throw InvalidDefinition();
        }
    }

    /// <summary>建立流程失敗時使用固定錯誤，不把原始 XAML 或型別內容寫入例外。</summary>
    private static InvalidDataException InvalidDefinition() => new("The activity workflow definition is invalid.");

    /// <summary>取完整型別的 namespace；無點型別以空 namespace 表示。</summary>
    private static string NamespaceOf(string type)
    {
        var index = type.LastIndexOf('.');
        return index < 0 ? string.Empty : type[..index];
    }

    /// <summary>取完整型別最後一段，避免以全文字串替換誤改流程內容。</summary>
    private static string LocalNameOf(string type)
    {
        var index = type.LastIndexOf('.');
        return index < 0 ? type : type[(index + 1)..];
    }
}

/// <summary>一個手動測試流程的唯一清理 owner；成功、失敗與取消都停用後刪除。</summary>
internal sealed class ProbeProcessOwner : IDisposable
{
    private readonly Action disable;
    private readonly Action delete;
    private int created;
    private int disposed;

    /// <summary>建立 owner 時尚未持有遠端流程；建立成功後才進入清理責任。</summary>
    internal ProbeProcessOwner(Action disable, Action delete)
    {
        this.disable = disable ?? throw new ArgumentNullException(nameof(disable));
        this.delete = delete ?? throw new ArgumentNullException(nameof(delete));
    }

    /// <summary>記錄平台已建立流程，之後由本 owner 負責清理。</summary>
    internal void MarkCreated() => created = 1;

    /// <summary>執行一次 probe；清理不使用 caller cancellation，避免留下測試流程。</summary>
    internal void Execute(Action execute)
    {
        try
        {
            execute();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>停用先於刪除，兩個清理動作各自嘗試且不重複執行。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref created, 0) == 0 || Interlocked.Exchange(ref disposed, 1) == 1)
            return;
        Exception? failure = null;
        try { disable(); } catch (Exception exception) { failure = exception; }
        try { delete(); } catch (Exception exception) { failure ??= exception; }
        if (failure is not null) throw new InvalidOperationException("Activity probe cleanup failed.", failure);
    }
}
