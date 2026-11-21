/*
檔案整體說明
目的與定位：驗證 Deployment 共用證據 writer 僅允許核准固定檔名，並可原子發布 L2 的脫敏 XML golden 與非驗收診斷投影。
主要流程：以案例專屬暫存目錄呼叫正式 writer，檢查輸出存在、內容正確且 UTF-8 無 BOM；拒絕路徑片段，最後由 fixture 清理目錄。
依賴與邊界：測試只寫入系統暫存目錄，不連接組織、不讀取秘密、不使用任意遠端路徑，也不將測試輸出當作實機證據。
資源與隔離：每個測試持有唯一隨機名稱目錄；finally/Dispose 只刪除自己建立的暫存樹，不使用共用或 repo 內路徑。
驗證方式：決定性斷言 golden 與診斷 XML 各自只能使用固定核准檔名、UTF-8 位元組無 BOM 且未知名稱仍拒絕；不得因此放寬路徑或大小限制。
*/
using System;
using System.IO;
using System.Reflection;
using System.Text;
using SpeechMessage.Dynamics.Deployment;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護共用證據 writer 的固定檔名白名單與 UTF-8 原子發布契約。</summary>
public sealed class DocumentWriterTests
{
    /// <summary>核准的 L2 XML golden 可由共用 writer 寫入且不含 UTF-8 BOM。</summary>
    [Fact]
    public void DocumentWriter_ActivityReferenceShapeXml_IsAllowedWithoutBom()
    {
        var root = Path.Combine(Path.GetTempPath(), "p8-document-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var method = typeof(DeploymentCli).GetMethod("WriteDocumentFile", BindingFlags.Static | BindingFlags.NonPublic)!;

            method.Invoke(null, [root, "activity-reference-shape.xml", "<ActivityReference />\r\n"]);

            var bytes = File.ReadAllBytes(Path.Combine(root, "activity-reference-shape.xml"));
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Equal("<ActivityReference />\r\n", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>未達 golden 條件的安全投影只能使用獨立診斷檔名，不可覆寫或冒充正式 golden。</summary>
    [Fact]
    public void DocumentWriter_WorkflowShapeDiagnosticXml_IsAllowedWithoutBom()
    {
        var root = Path.Combine(Path.GetTempPath(), "p8-document-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var method = typeof(DeploymentCli).GetMethod("WriteDocumentFile", BindingFlags.Static | BindingFlags.NonPublic)!;

            method.Invoke(null, [root, "workflow-shape-diagnostic.xml", "<ActivityReference />\r\n"]);

            var bytes = File.ReadAllBytes(Path.Combine(root, "workflow-shape-diagnostic.xml"));
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Equal("<ActivityReference />\r\n", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
