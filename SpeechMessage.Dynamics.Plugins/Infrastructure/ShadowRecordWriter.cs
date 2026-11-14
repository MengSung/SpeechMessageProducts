/*
 * 檔案整體說明
 *
 * 目的與定位：將兩個單次 Execute 的 recorder 安全投影寫成版本一 JSON 行，供影子
 * 比對解析。不接受任意 payload，也不使用 record.ToString 或讀取原始 overlay。
 * 主要功能與流程：依序輸出寫入與推播，加上 SMV-SHADOW|1| 及 seq；整次最多 8000
 * 個 UTF-16 字元，包含每個前綴與 CRLF。先保留 overflow／end 空間，只輸出完整行。
 * 最後固定 end=true、count=實際輸出意圖數；超量則在 end 前輸出 overflow=true、
 * total=全部寫入加推播意圖數，解析器不得把截斷當成完整或零差異結果。
 * 使用邊界與維護：借用 tracing、不保存 recorder。外殼在成功或例外路徑的
 * 唯一收尾位置呼叫；每個 writer 最多輸出一次，避免重複超出預算。沒有檔案、網路、
 * 背景工作或可變 static，暫存行最多 8000 字元；投影事先去除秘密與原文，規則失敗
 * 也可安全輸出。tracing 失敗由外殼保留原始規則錯誤，離線測試只驗證格式與容量，
 * 不宣稱已查證 Dataverse 真實保存政策或實機結果。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpeechMessage.Dynamics.Plugins.Infrastructure;

/// <summary>受整次追蹤總量限制、僅輸出安全版本化投影的同步 writer。</summary>
internal sealed class ShadowRecordWriter
{
    private const string Prefix = "SMV-SHADOW|1|";
    private readonly ITracingService tracing;
    private bool written;
    /// <summary>借用本次追蹤服務，不接管其生命週期。</summary>
    public ShadowRecordWriter(ITracingService tracing) => this.tracing = tracing ?? throw new ArgumentNullException(nameof(tracing));
    /// <summary>成功與規則失敗都只輸出安全摘要；完整建立一批行再發布，不接觸原始 Entity。</summary>
    public void Write(RecordingOrganizationService recording, ShadowPushRecorder pushes)
    {
        if (recording is null) throw new ArgumentNullException(nameof(recording));
        if (pushes is null) throw new ArgumentNullException(nameof(pushes));
        if (written) throw new InvalidOperationException("Shadow trace already completed.");
        written = true;
        var total = checked(recording.TotalCount + pushes.TotalCount);
        var totalText = total.ToString(CultureInfo.InvariantCulture);
        var overflowLine = Prefix + "{\"overflow\":true,\"total\":" + totalText + "}";
        // count 不超過 total；依 total 位數先保留 end 行與 CRLF，確保容量邊界仍有完整標記。
        var endReservation = Prefix.Length + "{\"end\":true,\"count\":}".Length + totalText.Length + 2;
        var remaining = 8000 - (overflowLine.Length + 2) - endReservation;
        var lines = new List<string>();
        var overflow = recording.Overflow || pushes.Overflow;
        foreach (var record in recording.Writes)
        {
            var projection = RecordingOrganizationService.ReadProjection(record.ProjectionJson);
            projection.AddFirst(new JProperty("seq", lines.Count + 1));
            if (!TryAppend(projection, lines, ref remaining)) { overflow = true; break; }
        }
        // 已缺少寫入意圖時不輸出後續推播，維持可解析的前綴序列而不製造貌似完整的尾段。
        if (lines.Count == recording.TotalCount)
        {
            foreach (var push in pushes.Records)
            {
                var projection = new JObject
                {
                    ["seq"] = lines.Count + 1, ["operation"] = "Push", ["contactId"] = push.ContactId,
                    ["templateKey"] = push.TemplateKey, ["retryKey"] = push.RetryKey,
                    ["messageCount"] = push.MessageCount, ["contentSha256"] = push.ContentSha256
                };
                if (!TryAppend(projection, lines, ref remaining)) { overflow = true; break; }
            }
        }
        if (lines.Count < total) overflow = true;
        foreach (var line in lines) tracing.Trace("{0}", line);
        if (overflow) tracing.Trace("{0}", overflowLine);
        tracing.Trace("{0}", Prefix + "{\"end\":true,\"count\":" + lines.Count.ToString(CultureInfo.InvariantCulture) + "}");
    }
    /// <summary>只接受可放入剩餘預算的完整 JSON 行，禁止拆開 JSON 或 Unicode 字元。</summary>
    private static bool TryAppend(JObject projection, List<string> lines, ref int remaining)
    {
        var line = Prefix + projection.ToString(Formatting.None);
        if (line.Length + 2 > remaining) return false;
        remaining -= line.Length + 2;
        lines.Add(line);
        return true;
    }
}
