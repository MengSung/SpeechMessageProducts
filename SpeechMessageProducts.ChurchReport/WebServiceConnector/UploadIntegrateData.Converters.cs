// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/UploadIntegrateData.Converters.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class UploadIntegrateData
// 主要成員：ConvertIdentityToIndex、ConvertIndexToIdentity、ConvertIndexToClearIdentity、ConvertSpiritualIdentityToIndex、ConvertBaptizedSituationToIndex、ConvertNumberToFollowUpWeekPicker、ConvertNumberToWeekIndex、ConvertFollowUpWeekPickerToIndex、ConvertFollowUpResultPickerToIndex、ConvertFollowUpNextStepPickerToIndex
// 引用命名空間：System、System.Collections.Generic、ChurchReport.Models、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 轉換工具與字典處理 (Partial)
    /// 包含：各種轉換方法、字典操作
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 委身類型轉換

        private int ConvertIdentityToIndex(String Identity)
        {
            try
            {
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null,
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                );

                string cleanedIdentity = System.Text.RegularExpressions.Regex.Replace(Identity, @"^\d+\.\s*", "");
                int optionValue = optionSetService.GetOptionSetValue("contact", "customertypecode", cleanedIdentity);

                System.Diagnostics.Debug.WriteLine($"[ConvertIdentityToIndex] 輸入文字: {Identity}, 清理後: {cleanedIdentity}, 回傳值: {optionValue}");
                return optionValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConvertIdentityToIndex] 動態查詢失敗: {ex.Message}");
                return 100000000;
            }
        }

        private String ConvertIndexToIdentity(int Identity)
        {
            try
            {
                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null,
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                );

                string displayText = optionSetService.GetOptionSetText("contact", "customertypecode", Identity);
                System.Diagnostics.Debug.WriteLine($"[ConvertIndexToIdentity] 輸入值: {Identity}, 回傳文字: {displayText}");
                return displayText;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConvertIndexToIdentity] 動態查詢失敗: {ex.Message}");
                return "未知類型";
            }
        }

        private String ConvertIndexToClearIdentity(int Identity)
        {
            switch (Identity)
            {
                case 100000000: return "新朋友";
                case 100000004: return "未入組";
                case 100000007: return "未入組";
                case 1: return "小組組員";
                default: return "小組組員";
            }
        }

        #endregion

        #region 受洗/洗禮狀態轉換

        private int ConvertSpiritualIdentityToIndex(String SpiritualIdentity)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SpiritualIdentity))
                    return 100000004;

                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null,
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                );

                return optionSetService.GetOptionSetValue("contact", "new_spiriitual_identity", SpiritualIdentity);
            }
            catch
            {
                return 100000004;
            }
        }

        private int ConvertBaptizedSituationToIndex(String BaptizedSituation)
        {
            switch (BaptizedSituation)
            {
                case "堅信禮(籍在)": return 100000000;
                case "成人禮(籍在)": return 100000001;
                case "轉籍(籍在)": return 100000002;
                case "小兒禮(籍不在)": return 100000003;
                case "未受洗(籍不在)": return 100000004;
                default: return -999999999;
            }
        }

        #endregion

        #region 跟進相關轉換

        private String ConvertNumberToFollowUpWeekPicker(int FollowUpWeekIndex)
        {
            string[] weeks = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十",
                              "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十" };
            return FollowUpWeekIndex >= 1 && FollowUpWeekIndex <= 20 ? weeks[FollowUpWeekIndex] : "二十";
        }

        private int ConvertNumberToWeekIndex(int FollowUpWeekIndex)
        {
            return FollowUpWeekIndex >= 1 && FollowUpWeekIndex <= 20
                ? 100000000 + FollowUpWeekIndex - 1
                : 100000007;
        }

        private int ConvertFollowUpWeekPickerToIndex(String FollowUpWeek)
        {
            var weekMap = new Dictionary<String, int>
            {
                { "一", 100000000 }, { "二", 100000001 }, { "三", 100000002 }, { "四", 100000003 },
                { "五", 100000004 }, { "六", 100000005 }, { "七", 100000006 }, { "八", 100000007 },
                { "九", 100000008 }, { "十", 100000009 }, { "十一", 100000010 }, { "十二", 100000011 },
                { "十三", 100000012 }, { "十四", 100000013 }, { "十五", 100000014 }, { "十六", 100000015 },
                { "十七", 100000016 }, { "十八", 100000017 }, { "十九", 100000018 }, { "二十", 100000019 }
            };
            return weekMap.TryGetValue(FollowUpWeek, out int value) ? value : 100000008;
        }

        private int ConvertFollowUpResultPickerToIndex(String FollowUpResult)
        {
            var resultMap = new Dictionary<String, int>
            {
                { "請選擇", 100000000 }, { "熱情回應", 100000001 }, { "渴慕認識信仰", 100000002 },
                { "沒聯絡上", 100000003 }, { "反應冷淡", 100000004 }, { "考慮中，繼續跟進", 100000005 },
                { "入小組", 100000006 }, { "來主日", 100000007 }, { "轉介", 100000008 }, { "其他", 100000009 }
            };
            return resultMap.TryGetValue(FollowUpResult, out int value) ? value : 100000000;
        }

        private int ConvertFollowUpNextStepPickerToIndex(String FollowUpNextStep)
        {
            var stepMap = new Dictionary<String, int>
            {
                { "請選擇", 100000000 }, { "繼續跟進", 100000001 }, { "轉介", 100000002 }
            };
            return stepMap.TryGetValue(FollowUpNextStep, out int value) ? value : 100000000;
        }

        private int ConvertFollowUpOptionToIndex(String FollowUpOption)
        {
            var optionMap = new Dictionary<String, int>
            {
                { "電話", 100000000 }, { "探訪", 100000001 }, { "Line/FB", 100000002 },
                { "出遊/吃飯", 100000003 }, { "懷鄉/其他課程", 100000004 }, { "約談", 100000005 },
                { "沒跟進", 100000006 }, { "其他", 100000007 }
            };
            return optionMap.TryGetValue(FollowUpOption, out int value) ? value : 100000000;
        }

        private String ConvertIndexToFollowUpResultPicker(int FollowUpWeekIndex)
        {
            var resultMap = new Dictionary<int, String>
            {
                { 100000000, "請選擇" }, { 100000001, "熱情回應" }, { 100000002, "渴慕認識信仰" },
                { 100000003, "沒聯絡上" }, { 100000004, "反應冷淡" }, { 100000005, "考慮中，繼續跟進" },
                { 100000006, "入小組" }, { 100000007, "來主日" }, { 100000008, "轉介" }, { 100000009, "其他" }
            };
            return resultMap.TryGetValue(FollowUpWeekIndex, out String value) ? value : "";
        }

        private String ConvertIndexToFollowUpNextStepPicker(int FollowUpWeekIndex)
        {
            var stepMap = new Dictionary<int, String>
            {
                { 100000000, "請選擇" }, { 100000001, "繼續跟進" }, { 100000002, "轉介" }
            };
            return stepMap.TryGetValue(FollowUpWeekIndex, out String value) ? value : "";
        }

        private String ConvertIndexToFollowUpOptionPicker(int FollowUpWays)
        {
            var optionMap = new Dictionary<int, String>
            {
                { 100000000, "電話" }, { 100000001, "探訪" }, { 100000002, "Line/FB" },
                { 100000003, "出遊/吃飯" }, { 100000004, "懷鄉/其他課程" }, { 100000005, "約談" },
                { 100000006, "沒跟進" }, { 100000007, "其他" }
            };
            return optionMap.TryGetValue(FollowUpWays, out String value) ? value : "";
        }

        private int ConvertVisitToIndex(string visit)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(visit))
                    return EMPTY_VALUE;

                var optionSetService = new ChurchReport.Services.OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null,
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())
                );

                return optionSetService.GetOptionSetValue("new_present_record", "new_visit", visit);
            }
            catch
            {
                return EMPTY_VALUE;
            }
        }

        private int ConvertTopicToIndex(String Topic)
        {
            var topicMap = new Dictionary<String, int>
            {
                { "預備週", 100000000 }, { "真幸福", 100000001 }, { "真相大白", 100000002 },
                { "萬世巨星", 100000003 }, { "幸福連線", 100000004 }, { "當上帝來敲門", 100000005 },
                { "十字架的勝利", 100000006 }, { "釋放與自由", 100000007 }, { "幸福的教會", 100000008 }
            };
            return topicMap.TryGetValue(Topic, out int value) ? value : 100000000;
        }

        #endregion

        #region 字典處理

        private void ResetDictionary(DateTime aSunday)
        {
            try
            {
                AddToDictionary(ref this.m_FeedBackReport, "主日出席統計表頭",
                    $"主日日期:{aSunday.ToLocalTime().ToShortDateString()}{Environment.NewLine}---------------------------------{Environment.NewLine}");
                AddToDictionary(ref this.m_FeedBackReport, "小組出席統計表頭", "小組出席統計:");

                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計小組組員未出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計未入組出未席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人出席字串", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計新人未出席字串", "");

                AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", "");

                AddToDictionary(ref this.m_FeedBackReport, "主日統計", "");
                AddToDictionary(ref this.m_FeedBackReport, "小組統計", "");
                AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進", "");
            }
            catch { }
        }

        private void AddToDictionaryByIdentity(Guid aListEntityId, String Type, ref String Identity, ref Entity aContact, bool Presentflag)
        {
            try
            {
                String ContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");
                String presentCount = $"(共出席{GetPresentNumber(aListEntityId, Type, ref aContact)}次) {Environment.NewLine}";

                String key = $"{(Type == "主日" ? "主日" : "小組")}統計{Identity}{(Presentflag ? "出席" : "未出席")}字串";
                AddToDictionary(ref this.m_FeedBackReport, key, ContactName + presentCount);
            }
            catch { }
        }

        private void AddToDictionaryFollowByIdentity(ref String Identity, ref Entity aContact, Member aMemberInfomation)
        {
            try
            {
                String ContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");
                String PersonalFollowUp = SetFollowUpString(ref aMemberInfomation);

                String FollowUp = !string.IsNullOrEmpty(PersonalFollowUp)
                    ? $"\t\t{ContactName}{Environment.NewLine}{PersonalFollowUp}{Environment.NewLine}"
                    : $"\t\t{ContactName} : 沒有跟進活動{Environment.NewLine}";

                if (Identity == "未入組")
                    AddToDictionary(ref this.m_FeedBackReport, "未入組跟進統計內容", FollowUp);
                else if (Identity == "新朋友")
                    AddToDictionary(ref this.m_FeedBackReport, "新朋友跟進統計內容", FollowUp);
            }
            catch { }
        }

        private String SetFollowUpString(ref Member aMemberInfomation)
        {
            return AppendHeadString("\t\t\t跟進週次:", aMemberInfomation.FollowUpWeek) +
                   AppendHeadString("\t\t\t跟進方式:", aMemberInfomation.FollowUpOption) +
                   AppendHeadString("\t\t\t跟進結果:", aMemberInfomation.FollowUpResult) +
                   AppendHeadString("\t\t\t下一步驟:", aMemberInfomation.FollowUpNextStep) +
                   AppendHeadString("\t\t\t跟進摘要:", aMemberInfomation.FollowUpNote);
        }

        private String AppendHeadString(String HeadString, String BodyString)
        {
            return !string.IsNullOrEmpty(BodyString) && BodyString != "." && BodyString != "請選擇"
                ? HeadString + BodyString + Environment.NewLine
                : "";
        }

        private bool AddToDictionary(ref Dictionary<String, String> aDictionary, String Method, String Content)
        {
            try
            {
                if (aDictionary.ContainsKey(Method))
                {
                    aDictionary[Method] += Content;
                    return true;
                }
                else
                {
                    aDictionary.Add(Method, Content);
                    return false;
                }
            }
            catch { return false; }
        }

        private String GetDictionaryValue(ref Dictionary<String, String> aDictionary, String Method)
        {
            try { return aDictionary[Method]; }
            catch { return ""; }
        }

        private String GetDictionaryValue(ref Dictionary<String, String> aDictionary, String HeadString, String Method)
        {
            try
            {
                return !string.IsNullOrEmpty(aDictionary[Method])
                    ? HeadString + aDictionary[Method] + Environment.NewLine
                    : "";
            }
            catch { return ""; }
        }

        #endregion
    }
}
