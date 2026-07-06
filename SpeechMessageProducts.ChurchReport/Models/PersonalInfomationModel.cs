// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/PersonalInfomationModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class PersonalInfomationModel
// 主要成員：SetPersonalInfomationViewModel、UploadPersonalInfomation、MappingPersonalInfomation、m_LoginContact
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ChurchReport.ViewModels、ChurchReport.Models.CrmTransmitModule、ChurchReport.WebServiceConnector、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
