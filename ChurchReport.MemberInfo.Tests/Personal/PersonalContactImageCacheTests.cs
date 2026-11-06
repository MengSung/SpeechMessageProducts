/*
 * 檔案整體說明
 *
 * 目的與定位：驗證個人照片更新的快取失效邊界。 測試故障注入是預先放入代表舊照片的完整尺寸與縮圖資料， 決定性斷言是所有已支援的個人照片快取鍵都不再可讀。
 *
 * 主要功能與流程：
 * 本檔宣告 PersonalContactImageCacheTests。
 * 測試案例入口包含 InvalidatePersonalImageCache_removes_full_image_and_all_thumbnail_sizes。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
