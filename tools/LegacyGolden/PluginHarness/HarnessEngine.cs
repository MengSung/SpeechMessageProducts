/*
檔案整體說明

目的與定位：組合 bounded source reader、分類器及 attendance 黃金驗證，形成一次完整的 PluginHarness 候選；本檔不負責檔案發布或外部服務。

主要功能與流程：讀取完整第一層專案清單，逐一分類並排序，建立固定三筆 attendance 情境，最後確認結果沒有重複專案名稱、沒有可疑輸出欄位且至少有一個來源專案。任何操作錯誤在候選交給 writer 前拋出，避免部分結果被發布。

使用邊界與維護：所有輸入、結果與 JSON payload 都只在單次同步呼叫中存在；沒有共享快取、Session、credential、timer、task 或網路連線。來源名稱只保留必要的穩定專案名稱與雜湊，原始碼與診斷本文永不進入候選。
*/
using System;
using Newtonsoft.Json.Linq;
namespace LegacyGolden.PluginHarness;
/// <summary>真實編譯與執行的工具入口；尚未接合時拒絕假資料。</summary>
public static class GoldenHarness
{
    /// <summary>依合成來源及情境執行，未實作前明確失敗。</summary>
    public static JObject RunSources(string[] sources, string scenariosJson) => throw new NotSupportedException();
}
