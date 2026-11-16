/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-11 FeeNotifyActivity 在 P5-PROP-006 Shadow 邊界使用的重試鍵與
 * 通知內容摘要契約。測試不建立 CodeActivity runtime、不連 D365、不建立 LINE client，
 * 也不讀取 token；只驗證活動公開給同組件測試的純值輔助規則。
 *
 * 主要功能與流程：以固定 fee GUID 驗證 Retry Key 原樣等於收費單正式 ID，並以固定舊
 * 通知範本重新計算 SHA-256，核對活動寫入 Shadow recorder 的摘要值。空 GUID 必須在
 * 任何外部 I/O 前拒絕，避免跨案例或跨教會重用錯誤的通知身分。
 *
 * 使用邊界與維護：每個測試只持有區域純值，沒有共享可變狀態、背景工作、連線、timer
 * 或可處置 owner；通過只證明離線意圖摘要與重試鍵，不代表 Live 推播或實機送達。
 */
extern alias Workflows;

using System;
using System.Security.Cryptography;
using System.Text;
using FeeNotifyActivity = Workflows::SpeechMessage.Dynamics.Workflows.Fees.FeeNotifyActivity;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fees;

/// <summary>保護收費通知的正式重試鍵及舊範本摘要。</summary>
public sealed class FeeNotifyActivityTests
{
    private static readonly Guid FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>通知重試鍵必須直接使用 feeId，不能改用 contact 或活動 instance。</summary>
    [Fact]
    public void Fees_Notify_RetryKeyIsFeeId()
    {
        Assert.Equal(FeeId, FeeNotifyActivity.RetryKey(FeeId));
        Assert.Throws<ArgumentException>(() => FeeNotifyActivity.RetryKey(Guid.Empty));
    }

    /// <summary>Shadow 摘要必須與固定舊通知範本的 UTF-8 SHA-256 相同，且不保存原文。</summary>
    [Fact]
    public void Fees_Notify_ContentMatchesLegacy()
    {
        using var hash = SHA256.Create();
        var expected = BitConverter.ToString(hash.ComputeHash(
            Encoding.UTF8.GetBytes("Fees.FeeNotify"))).Replace("-", string.Empty).ToLowerInvariant();

        Assert.Equal(expected, FeeNotifyActivity.ContentSha256());
        Assert.Equal(64, FeeNotifyActivity.ContentSha256().Length);
    }
}
