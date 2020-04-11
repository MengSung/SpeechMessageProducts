using Line.Pay;
using Line.Pay.Models;
using System;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

using ChurchReport.Models;

using ToolUtilityNameSpace;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ChurchReport.WebServiceConnector
{
    public class DedicationInfo
    {
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        // 客製化
        private const String CONFIRM_URL = "https://yhchurchback.speechmessage.com.tw:454/api/callback/Confirm";

        private String m_FullName = "";

        public DedicationInfo()
        {
        }
        public async Task<string> CreateFeeAsync(String LineId, DedicationInfoModel DedicationInfoModel)
        {
            try
            {
                #region 通知住綁定的輸入格式

                CreateFee(LineId, DedicationInfoModel);
                //Redirect(response.Info.PaymentUrl.Web);

                return m_FullName;

                //RedirectToPage(response.Info.PaymentUrl.App);

                //BindingAction.Add(new UriTemplateAction("不想繳費", response.Info.PaymentUrl.App));

                //await m_PushUtility.SendMessage(DisplayLineId, "付款網址 = " + response.Info.PaymentUrl.Web);

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        public void CreateFee( String LineId, DedicationInfoModel DedicationInfoModel)
        {
            try
            {
                #region 通知住綁定的輸入格式

                Entity aFeeToCreated = new Entity("new_fee");

                SetFeeParameter( LineId, aFeeToCreated, DedicationInfoModel);

                this.m_ToolUtilityClass.CreateEntity(aFeeToCreated);
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public void SetFeeParameter(String LineId, Entity aFeeToCreated, DedicationInfoModel DedicationInfoModel)
        {
            try
            {
                #region 通知住綁定的輸入格式
                // 連絡人姓名
                Entity aContact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LineId);

                // 取得課程名稱
                //String LessonDisplayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLessonEntity, "new_name");

                // 取得報名者的全名
                String FullName = "";
                m_FullName = FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "fullname");

                // 收費單名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aFeeToCreated, "new_name",  FullName + "奉獻");

                // 收費單姓名關聯 LOOKUP
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_contact_new_fee", "contact", aContact.Id);

                // 收費單課程關聯LOOKUP
                //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_disciple_lessons_new_fee", "new_disciple_lessons", aLessonEntity.Id);

                // 收費單應收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_shoud_pay", new Money(DedicationInfoModel.Amount));

                // 收費單實收金額
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFeeToCreated, "new_fee_really_paid", new Money(0));

                // 收費單付款方式
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFeeToCreated, "new_pay_way", 100000005);

                // 收費單上課紀錄關聯
                //this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFeeToCreated, "new_stor_lessons_new_fee", "new_stor_lessons", NewStorLessonId);

                // 收費單收費日期
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFeeToCreated, "new_pay_date", DateTime.Now.ToLocalTime());

                // Line Pay
                //this.m_ToolUtilityClass.SetEntityIntAttribute(ref aFeeToCreated, "new_transaction_id", (int)aReserveResponse.Info.TransactionId);

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

    }
}
