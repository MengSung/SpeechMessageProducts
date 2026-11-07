/*
 * 檔案整體說明
 *
 * 目的與定位：保留既有 ChurchReport assembly 對公開型別
 * ChurchReport.Logging.ExceptionLoggerProvider 的 CLR 型別轉送。唯一實作由
 * SpeechMessage.Hosting 編譯；本檔不建立產品 logger、diagnostics owner 或第二套輸出管線。
 *
 * 主要功能與流程：
 * TypeForwardedTo 讓已編譯呼叫端仍以原 assembly 名稱與完整型別名稱解析 provider，
 * 新來源則由 Hosting 專案參考同一實作。轉送目標具有相同 public constructor 與
 * ILoggerProvider 契約；Program 仍擁有 diagnostics 並負責 drain，不將 Dispose 責任轉給 provider。
 *
 * 使用邊界與維護：
 * 本檔只描述 CLR metadata，不接收 request、Session、使用者資料、credential 或組態，
 * 無可變狀態與獨立資源。修改型別名稱、constructor、Hosting 參考或轉送屬性時，必須
 * 執行 ChurchReport 相容測試與 Debug／Release 架構清冊檢查，不能以刪除此檔掩蓋 ABI 破壞。
 */
using System.Runtime.CompilerServices;
using ChurchReport.Logging;

[assembly: TypeForwardedTo(typeof(ExceptionLoggerProvider))]
