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
