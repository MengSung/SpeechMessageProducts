// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs
// 檔案責任：固定個人相關資料照片更新後必須清除所有個人照片快取尺寸的回歸契約。
// 測試保護：先建立舊照片的完整尺寸與縮圖快取，再執行清除流程，確認後續讀取不會
//             跨請求保留舊照片；這同時避免瀏覽器重新請求時取得過期的使用者資料。
// 編碼要求：此檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
using System;
using System.Reflection;
using ChurchReport.Controllers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Personal;

/// <summary>
/// 驗證個人照片更新的快取失效邊界。
/// 測試故障注入是預先放入代表舊照片的完整尺寸與縮圖資料，
/// 決定性斷言是所有已支援的個人照片快取鍵都不再可讀。
/// </summary>
public sealed class PersonalContactImageCacheTests
{
    [Fact]
    public void InvalidatePersonalImageCache_removes_full_image_and_all_thumbnail_sizes()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var contactId = Guid.NewGuid();

        cache.Set($"contact-image-full:{contactId:N}", new byte[] { 1 });
        cache.Set($"contact-image-thumb:{contactId:N}:80", new byte[] { 2 });
        cache.Set($"contact-image-thumb:{contactId:N}:256", new byte[] { 3 });

        var invalidator = typeof(PersonalController).GetMethod(
            "InvalidatePersonalImageCache",
            BindingFlags.Static | BindingFlags.NonPublic);

        invalidator.Should().NotBeNull("照片更新必須有集中且可測試的個人照片快取失效流程");
        invalidator!.Invoke(null, new object[] { cache, contactId });

        cache.TryGetValue($"contact-image-full:{contactId:N}", out _).Should().BeFalse();
        cache.TryGetValue($"contact-image-thumb:{contactId:N}:80", out _).Should().BeFalse();
        cache.TryGetValue($"contact-image-thumb:{contactId:N}:256", out _).Should().BeFalse();
    }
}
