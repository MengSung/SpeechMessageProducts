/*
 * 檔案整體說明
 *
 * 目的與定位：本檔由 SpeechMessageProducts.GoodShepherd.Hosting 擁有，提供
 * P5-07 課程 QR 圖片組的單筆待辦消費器，將
 * LessonQrBundlePlan 的完整候選逐張交給 renderer，全部成功後才交給
 * 上層原子發布委派。它不直接更新 D365，也不保存跨工作設定或連線。
 *
 * 主要功能與流程：輸入是已驗證的 LessonQrBundlePlan，輸出是由 publisher
 * 消費的完整圖片 stream 候選；先建立 31／32 張純值候選，再以固定順序產圖；
 * 任一張失敗、
 * 取消或 renderer 回傳非法 stream，立即停止且不呼叫 publisher。全部產圖
 * 完成後，publisher 在同一工作 scope 內消費每張 stream 並提交欄位更新。
 * handler 是唯一的 stream owner，成功或失敗都在 finally 逐一釋放，避免
 * 圖片、非同步工作或下游例外造成資源滯留。
 *
 * 使用邊界與維護：輸入前綴、課程 ID、數量及欄位唯一性由 plan 驗證；
 * publisher 必須自行以伺服器授權的課程／教會範圍完成原子回寫及圖片位置
 * 發布。本檔不讀 token、不連 LINE、不使用 Session、cache、timer 或無界
 * 背景工作；取消只接受呼叫端 token，已取得的 stream 仍由本 handler 排空
 * 並確定性 Dispose。
 *
 * 驗證方式：離線測試注入正常 renderer、產圖失敗及 publisher 觀察器，確認
 * 完整候選才發布、部分候選不發布以及所有 stream 回到關閉狀態。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Hosting.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Hosting.QrImages;

/// <summary>一張已產生圖片及其正式回寫欄位；stream 由 handler 暫時擁有。</summary>
public sealed record LessonQrRenderedImage(LessonQrImageDescriptor Descriptor, Stream Png);

/// <summary>課程 QR 圖片組的可注入產圖邊界，便於測試資源生命週期。</summary>
public interface ILessonQrImageRenderer
{
    /// <summary>以單次內容產生可讀且可定位的 PNG stream，失敗不得交回部分資源。</summary>
    Task<Stream> RenderAsync(string content, CancellationToken cancellationToken);
}

/// <summary>課程 QR 完整候選的原子發布委派；成功後才可回寫所有圖片欄位。</summary>
public delegate Task LessonQrBundlePublisher(
    IReadOnlyList<LessonQrRenderedImage> images, CancellationToken cancellationToken);

/// <summary>逐張產圖、全部成功後才發布的有界課程 QR handler。</summary>
public sealed class LessonQrBundleHandler : IOutboundTaskHandler
{
    private readonly LessonQrBundlePlan _plan;
    private readonly LessonQrBundlePublisher _publish;
    private readonly ILessonQrImageRenderer _renderer;

    /// <summary>
    /// 建立單筆待辦 scope；plan、renderer 及 publisher 均只屬本次工作，
    /// 不轉移任何跨工作可變狀態。
    /// </summary>
    public LessonQrBundleHandler(LessonQrBundlePlan plan, LessonQrBundlePublisher publish,
        ILessonQrImageRenderer renderer)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>全部圖片產生成功後才呼叫一次 publisher；所有 stream 均由 finally 關閉。</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rendered = new List<LessonQrRenderedImage>(_plan.Count);
        try
        {
            foreach (var descriptor in _plan.Images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var png = await _renderer.RenderAsync(descriptor.Content, cancellationToken)
                    .ConfigureAwait(false);
                if (png is null || !png.CanRead || !png.CanSeek ||
                    png.Length is < 1 or > QrImageRenderer.MaximumPngBytes)
                {
                    png?.Dispose();
                    throw new InvalidDataException("QR renderer returned an invalid PNG stream.");
                }
                png.Position = 0;
                rendered.Add(new LessonQrRenderedImage(descriptor, png));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _publish(rendered.AsReadOnly(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var image in rendered)
                image.Png.Dispose();
        }
    }
}
