/*
 * 檔案整體說明
 *
 * 目的與定位：以舊 QR 產生端的獨立黃金內容保護 GoodShepherd.Core 四種內容格式，
 * 防止新編碼器省略個人尾欄、改變主日分支或讓外來導覽 URL 通過純內容入口。
 *
 * 主要功能與流程：從組件嵌入的八筆合成 TSV 讀取預期值，直接比較真實編碼器輸出，
 * 再以舊內容驗證 ID、課次、屬性與簽到欄位；損毀 ID、空 GUID、外來 URL 及超長
 * 輸入必須回傳 false 並清空所有輸出。預期值來自舊來源運算式而非被測編碼器。
 *
 * 使用邊界與維護：只處理 QrCodeId 參數，沒有 D365、LINE、圖片、秘密或真實個資。
 * 黃金檔 stream 與 reader 由單次讀取方法擁有，using 逆序釋放後才交付區域陣列；
 * 無共享可變狀態、快取、背景工作或跨測試資料保留。個人姓名只是相容尾欄，
 * 不是穩定身分或授權；合法但屬其他教會的 GUID 仍須由伺服器端授權拒絕。
 * net48 與 net10.0 使用相同黃金，產品目標身分另由既有 smoke 測試檢查。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.QrCodes;

/// <summary>
/// 保護舊產生端的內容相容性；任何欄位省略、分隔符變更或失敗輸出洩漏都應使案例失敗。
/// </summary>
public sealed class QrCodeLegacyCompatibilityTests
{
    /// <summary>
    /// 四類舊運算式的八筆輸出必須逐字相符，包含個人尾欄的底線、at 與空字串；
    /// 黃金來源獨立於 codec，省略尾欄或正規化顯示名稱都會被精確比較攔下。
    /// </summary>
    [Fact]
    public void Encode_MatchesLegacyGolden()
    {
        var rows = ReadGoldenRows();
        Assert.Equal(8, rows.Length);
        foreach (var row in rows)
            Assert.Equal(row[4], EncodeGolden(row));
    }

    /// <summary>
    /// 舊內容直接交給真實 decoder，必須還原資料庫 ID 與實際消費欄位；
    /// 個人掃碼只消費 ID，不把合成姓名當成身分，也不冒稱已驗證授權或 QR 圖片。
    /// </summary>
    [Fact]
    public void Decode_LegacyContent_RoundTrip()
    {
        foreach (var row in ReadGoldenRows())
        {
            var expectedId = Guid.Parse(row[1]);
            switch (row[0])
            {
                case "lesson":
                case "enroll":
                    Assert.True(QrCodeContentCodec.TryDecodeLesson(row[4], out var lesson, out var index, out var lessonAction));
                    Assert.Equal(expectedId, lesson);
                    Assert.Equal(row[2], index);
                    Assert.Equal(row[3], lessonAction);
                    break;
                case "meeting-at":
                case "meeting-under":
                    Assert.True(QrCodeContentCodec.TryDecodeMeeting(row[4], out var meeting, out var attribute, out var meetingAction, out var separator));
                    Assert.Equal(expectedId, meeting);
                    Assert.Equal(row[2], attribute);
                    Assert.Equal(row[3], meetingAction);
                    Assert.Equal(row[0] == "meeting-at" ? QrCodePayloadSeparator.AtSign : QrCodePayloadSeparator.Underscore, separator);
                    break;
                case "personal":
                    Assert.True(QrCodeContentCodec.TryDecodePersonal(row[4], out var contact));
                    Assert.Equal(expectedId, contact);
                    break;
                case "group":
                    Assert.True(QrCodeContentCodec.TryDecodeSmallGroup(row[4], out var report, out var groupAction));
                    Assert.Equal(expectedId, report);
                    Assert.Equal(row[2], groupAction);
                    break;
                default:
                    throw new InvalidOperationException("Unknown synthetic QR golden type.");
            }
        }
    }

    /// <summary>
    /// 對每筆舊內容注入損毀或空 ID、外來完整 URL 與超出解析預算輸入，四入口均須
    /// fail closed 且清空輸出；此為語法拒絕，不宣稱能識別合法 GUID 的教會歸屬或防偽。
    /// </summary>
    [Fact]
    public void Decode_TamperedOrForeignContent_Rejected()
    {
        foreach (var row in ReadGoldenRows())
        {
            var suffix = row[4].Substring(36);
            AssertAllRejected("not-a-guid" + suffix);
            AssertAllRejected("00000000-0000-0000-0000-000000000000" + suffix);
            AssertAllRejected("https://foreign.invalid/?QrCodeId=" + row[4]);
        }
        AssertAllRejected(new string('x', 2049));
    }

    /// <summary>
    /// 相容入口須保留舊 concat 的 null／空尾欄與精確預算；空 ID 或多一字元必須拒絕，
    /// 例外不得回顯姓名。原單參數入口仍輸出純 ID，避免破壞先前呼叫端。
    /// </summary>
    [Fact]
    public void Personal_LegacySuffix_PreservesNullAndBoundedOwnership()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        const string emptySuffixContent = "11111111-2222-3333-4444-555555555555@";
        Assert.Equal(emptySuffixContent, QrCodeContentCodec.EncodePersonal(id, null));
        Assert.Equal(emptySuffixContent, QrCodeContentCodec.EncodePersonal(id, string.Empty));
        Assert.Equal("11111111-2222-3333-4444-555555555555", QrCodeContentCodec.EncodePersonal(id));

        var suffix = new string('x', 2011);
        var atLimit = QrCodeContentCodec.EncodePersonal(id, suffix);
        Assert.Equal(2048, atLimit.Length);
        Assert.Equal(emptySuffixContent + suffix, atLimit);
        Assert.True(QrCodeContentCodec.TryDecodePersonal(atLimit, out var decoded));
        Assert.Equal(id, decoded);
        var failure = Assert.Throws<ArgumentException>(() => QrCodeContentCodec.EncodePersonal(id, suffix + "x"));
        Assert.DoesNotContain(suffix, failure.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => QrCodeContentCodec.EncodePersonal(Guid.Empty, "合成姓名"));
    }

    /// <summary>只選擇對應產品入口；預期內容始終來自第五欄，不由本方法建立。</summary>
    private static string EncodeGolden(string[] row)
    {
        var id = Guid.Parse(row[1]);
        return row[0] switch
        {
            "lesson" or "enroll" => QrCodeContentCodec.EncodeLesson(id, row[2], row[3]),
            "meeting-at" => QrCodeContentCodec.EncodeMeeting(id, row[2], row[3], QrCodePayloadSeparator.AtSign),
            "meeting-under" => QrCodeContentCodec.EncodeMeeting(id, row[2], row[3], QrCodePayloadSeparator.Underscore),
            "personal" => QrCodeContentCodec.EncodePersonal(id, row[2]),
            "group" => QrCodeContentCodec.EncodeSmallGroup(id, row[2]),
            _ => throw new InvalidOperationException("Unknown synthetic QR golden type."),
        };
    }

    /// <summary>
    /// 黃金資源由單次方法獨占讀取，嚴格 UTF-8 解碼且保留空尾欄；解析或斷言失敗時
    /// reader／stream 仍依 using 逆序 Dispose，不把開啟資源交給測試或共享快取。
    /// </summary>
    private static string[][] ReadGoldenRows()
    {
        using var stream = typeof(QrCodeLegacyCompatibilityTests).Assembly.GetManifestResourceStream(
            "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.qr-content-legacy.tsv");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!, new UTF8Encoding(false, true));
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] == '#') continue;
            var fields = line.Split('\t');
            Assert.Equal(5, fields.Length);
            rows.Add(fields);
        }
        return rows.ToArray();
    }

    /// <summary>各入口失敗後的全部輸出都必須清空，避免發布部分 ID 或殘留欄位。</summary>
    private static void AssertAllRejected(string content)
    {
        Assert.False(QrCodeContentCodec.TryDecodeLesson(content, out var lesson, out var index, out var lessonAction));
        Assert.Equal(Guid.Empty, lesson);
        Assert.Equal(string.Empty, index);
        Assert.Equal(string.Empty, lessonAction);
        Assert.False(QrCodeContentCodec.TryDecodeMeeting(content, out var meeting, out var attribute, out var meetingAction, out var separator));
        Assert.Equal(Guid.Empty, meeting);
        Assert.Equal(string.Empty, attribute);
        Assert.Equal(string.Empty, meetingAction);
        Assert.Equal(QrCodePayloadSeparator.Underscore, separator);
        Assert.False(QrCodeContentCodec.TryDecodePersonal(content, out var contact));
        Assert.Equal(Guid.Empty, contact);
        Assert.False(QrCodeContentCodec.TryDecodeSmallGroup(content, out var report, out var groupAction));
        Assert.Equal(Guid.Empty, report);
        Assert.Equal(string.Empty, groupAction);
    }
}
