/*
 * 檔案整體說明
 *
 * 目的與定位：以目前登入聯絡人組裝及更新個人資料畫面，負責ViewModel與CRM欄位間的映射及上傳協調；登入聯絡人與編輯結果只屬目前操作，不能跨使用者重用。
 *
 * 主要功能與流程：
 * 本檔宣告 PersonalInfomationModel。
 * 主要操作入口包含 SetPersonalInfomationViewModel、UploadPersonalInfomation、MappingPersonalInfomation。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.ViewModels;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models
{
    public class PersonalInfomationModel
    {
        public Entity m_LoginContact { get; set; }

        public PersonalInfomationViewModel m_PersonalInfomationViewModel ;

        public PersonalInfomationModel()
        {
        }

        public void SetPersonalInfomationViewModel()
        {
            if( m_PersonalInfomationViewModel == null )
            {
                //m_PersonalInfomationViewModel = new PersonalInfomationViewModel
                //{
                //    ID = 1,
                //    FirstName = "張",
                //    LastName = "",
                //    FullName = "胡夢嵩",
                //    Gender = "未知",
                //    Phone = "0910391931",
                //    HomePhone = "034812856",
                //    OfficePhone = "034316679",
                //    Email = "mengsunghu@gmail.com",
                //    LastSixDigit = "777777",
                //    PersonalId = "A121454929",
                //    Position = "",
                //    MerrageState = "未知",
                //    BirthDate = DateTime.Parse("1962/09/27"),
                //    GroupArray = new List<String>(),
                //    //HireDate = DateTime.Parse("2017/08/25"),
                //    HireDate = DateTime.Now,
                //    Notes = "",
                //    Address = "",
                //    ReadBibleNumber = 5,
                //    Status = "未信主",

                //    Introducer = "",
                //    IntroducerPhone = "",
                //    IntroducerGroup = "",
                //    IntroducerRelation = ""
                //};

                m_PersonalInfomationViewModel = new PersonalInfomationViewModel();

                PersonalInfomatioManager aPersonalInfomatioManager = new PersonalInfomatioManager();

                aPersonalInfomatioManager.SetPersonalInfomationViewModel(m_LoginContact, ref m_PersonalInfomationViewModel);
            }

            return;
        }

        public String UploadPersonalInfomation(String Account, String Password, PersonalInfomationViewModel aPersonalInfomationViewModel)
        {
            //CopyPersonFormViewModel(aPersonFormViewModel);

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            MappingPersonalInfomation(aPersonalInfomationViewModel);

            PersonalInfomatioManager aPersonalInfomatioManager = new PersonalInfomatioManager();

            aPersonalInfomatioManager.UpdatePersonalInfomationViewModel(aPersonalInfomationViewModel, m_LoginContact);

            return "成功更新 " + m_PersonalInfomationViewModel.FullName + " 個人相關資料!";
        }
        public void MappingPersonalInfomation(PersonalInfomationViewModel aPersonalInfomationViewModel)
        {
            if (m_PersonalInfomationViewModel != null)
            {
                m_PersonalInfomationViewModel.ID = aPersonalInfomationViewModel.ID;
                //m_PersonalInfomationViewModel.FirstName = aPersonalInfomationViewModel.FirstName;
                //m_PersonalInfomationViewModel.LastName = aPersonalInfomationViewModel.LastName;
                //m_PersonalInfomationViewModel.FullName = aPersonalInfomationViewModel.FullName;
                m_PersonalInfomationViewModel.Gender = aPersonalInfomationViewModel.Gender;
                m_PersonalInfomationViewModel.Phone = aPersonalInfomationViewModel.Phone;
                m_PersonalInfomationViewModel.HomePhone = aPersonalInfomationViewModel.HomePhone;
                m_PersonalInfomationViewModel.OfficePhone = aPersonalInfomationViewModel.OfficePhone;
                m_PersonalInfomationViewModel.Facebook = aPersonalInfomationViewModel.Facebook;
                m_PersonalInfomationViewModel.Instagram = aPersonalInfomationViewModel.Instagram;
                m_PersonalInfomationViewModel.Email = aPersonalInfomationViewModel.Email;
                m_PersonalInfomationViewModel.LastSixDigit = aPersonalInfomationViewModel.LastSixDigit;
                m_PersonalInfomationViewModel.NtbtOrNot = aPersonalInfomationViewModel.NtbtOrNot;
                m_PersonalInfomationViewModel.PersonalId = aPersonalInfomationViewModel.PersonalId;
                m_PersonalInfomationViewModel.Industry = aPersonalInfomationViewModel.Industry;
                m_PersonalInfomationViewModel.Position = aPersonalInfomationViewModel.Position;
                m_PersonalInfomationViewModel.MerrageState = aPersonalInfomationViewModel.MerrageState;
                m_PersonalInfomationViewModel.BirthDate = aPersonalInfomationViewModel.BirthDate;
                m_PersonalInfomationViewModel.GroupArray = aPersonalInfomationViewModel.GroupArray;
                m_PersonalInfomationViewModel.HireDate = aPersonalInfomationViewModel.HireDate;
                m_PersonalInfomationViewModel.Notes = aPersonalInfomationViewModel.Notes;
                m_PersonalInfomationViewModel.Address = aPersonalInfomationViewModel.Address;
                m_PersonalInfomationViewModel.ReadBibleNumber = aPersonalInfomationViewModel.ReadBibleNumber;
                m_PersonalInfomationViewModel.Status = aPersonalInfomationViewModel.Status;
                m_PersonalInfomationViewModel.Introducer = aPersonalInfomationViewModel.Introducer;
                m_PersonalInfomationViewModel.IntroducerPhone = aPersonalInfomationViewModel.IntroducerPhone;
                m_PersonalInfomationViewModel.IntroducerGroup = aPersonalInfomationViewModel.IntroducerGroup;
                m_PersonalInfomationViewModel.IntroducerRelation = aPersonalInfomationViewModel.IntroducerRelation;
            }
        }
    }

}
