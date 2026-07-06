// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationLoginContactService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationLoginContactService
// 主要成員：GetDonationPaymentLoginContact、CreateDonationContact
// 引用命名空間：System、ChurchReport.Models、ChurchReport.ViewModel、Microsoft.Xrm.Sdk、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ChurchReport.Models;
using ChurchReport.ViewModel;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻登入 contact 比對與新 contact 建立服務。
    ///
    /// 奉獻登入流程同時處理身分證比對、姓名比對、同名/同證件錯誤訊息，以及找不到資料時建立 contact。
    /// 這些規則是 ChurchReport 的 CRM 身分流程，不屬於共用金流核心。
    ///
    /// 將它從 DonationPaymentManager 拆出來之後，manager 只需要保留公開入口與結果回傳；
    /// 具體的比對策略若未來再調整，不會把大型付款協調器弄得更難維護。
    /// </summary>
    public sealed class DonationLoginContactService
    {
        private readonly ToolUtilityClass _utility;
        private readonly DonationContactService _contactService;

        public DonationLoginContactService(ToolUtilityClass utility, DonationContactService contactService)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _contactService = contactService ?? throw new ArgumentNullException(nameof(contactService));
        }

        public Entity GetDonationPaymentLoginContact(GalleryViewModel viewModel, ref string queryResult)
        {
            try
            {
                EntityCollection loginContactCollection = _utility.RetrieveContactCollectionByNationId(viewModel.NationId);

                if (loginContactCollection.Entities.Count > 0)
                {
                    Entity loginContact = _contactService.FilterByFullName(viewModel, loginContactCollection);

                    if (loginContact != null)
                    {
                        queryResult = viewModel.FullName + "成功登入";
                        return loginContact;
                    }

                    queryResult = viewModel.FullName + "登入錯誤:" + "有找到身分證字號，但是姓名卻不一樣";
                    return null;
                }

                EntityCollection fullNameContactCollection = _utility.RetrieveContactCollectionByName(viewModel.FullName);
                if (fullNameContactCollection.Entities.Count > 0)
                {
                    queryResult = viewModel.FullName + "成功登入" + "為您在系統中建立了資料";
                    return CreateDonationContact(viewModel);
                }

                queryResult = viewModel.FullName + "成功登入" + "為您在系統中建立了資料";
                return CreateDonationContact(viewModel);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public Entity CreateDonationContact(GalleryViewModel viewModel)
        {
            return _contactService.CreateContact(viewModel);
        }
    }
}
