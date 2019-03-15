using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.ViewModels;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector;

namespace ChurchReport.Models
{
    public class NewPersonModel
    {
        public PersonFormViewModel m_PersonFormViewModel = new PersonFormViewModel
        {
            ID = 1,
            FirstName = "張",
            LastName = "",
            Gender = "男性",
            Phone = "",
            HomePhone = "",
            Position = "",
            MerrageState="未知",
            BirthDate = DateTime.Parse("1975/01/1"),
            //HireDate = DateTime.Parse("2017/08/25"),
            HireDate = DateTime.Now,
            Notes = "",
            Address = "",
            ReadBibleNumber = 5,
            Status = "未信主",

            Introducer = "",
            IntroducerPhone = "",
            IntroducerGroup ="",
            IntroducerRelation = ""
        };

        public NewContact m_NewContact = new NewContact();

        public void SetupNewPersonModel(PersonFormViewModel aPersonFormViewModel)
        {
            CopyPersonFormViewModel(aPersonFormViewModel);
        }

        public String UploadNewPerson(SmallGroupDataList SmallGroupDataList, PersonFormViewModel aPersonFormViewModel)
        {
            CopyPersonFormViewModel(aPersonFormViewModel);

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = SmallGroupDataList.m_Account,
                Password = SmallGroupDataList.m_Password
            };

            NewPerson aNewPersonManager = new NewPerson();

            MappingPersonFormViewModelToNewContact(SmallGroupDataList, aPersonFormViewModel);

            return aNewPersonManager.CreateNewContact( aAccountPasswordData, m_NewContact );
        }

        public void CopyPersonFormViewModel(PersonFormViewModel aPersonFormViewModel)
        {
            m_PersonFormViewModel.FirstName = aPersonFormViewModel.FirstName;
            m_PersonFormViewModel.LastName = aPersonFormViewModel.LastName;
            m_PersonFormViewModel.Gender = aPersonFormViewModel.Gender;
            m_PersonFormViewModel.Phone = aPersonFormViewModel.Phone;
            m_PersonFormViewModel.HomePhone = aPersonFormViewModel.HomePhone;
            m_PersonFormViewModel.Position = aPersonFormViewModel.Position;
            m_PersonFormViewModel.BirthDate = aPersonFormViewModel.BirthDate;
            m_PersonFormViewModel.HireDate = aPersonFormViewModel.HireDate;
            m_PersonFormViewModel.Notes = aPersonFormViewModel.Notes;
            m_PersonFormViewModel.Address = aPersonFormViewModel.Address;
            m_PersonFormViewModel.ReadBibleNumber = aPersonFormViewModel.ReadBibleNumber;
            m_PersonFormViewModel.Status = aPersonFormViewModel.Status;
            m_PersonFormViewModel.Introducer = aPersonFormViewModel.Introducer;
            m_PersonFormViewModel.IntroducerPhone = aPersonFormViewModel.IntroducerPhone;
            m_PersonFormViewModel.IntroducerRelation = aPersonFormViewModel.IntroducerRelation;
            m_PersonFormViewModel.IntroducerGroup = aPersonFormViewModel.IntroducerGroup;
            m_PersonFormViewModel.MerrageState = aPersonFormViewModel.MerrageState;
        }
        public void MappingPersonFormViewModelToNewContact( SmallGroupDataList SmallGroupDataList , PersonFormViewModel aPersonFormViewModel)
        {
            //m_NewContact.Name             = aPersonFormViewModel.FirstName;
            m_NewContact.Name               = aPersonFormViewModel.LastName;
            m_NewContact.MobilePhone        = aPersonFormViewModel.Phone;
            m_NewContact.HomePhone          = aPersonFormViewModel.HomePhone;
            m_NewContact.Note               = aPersonFormViewModel.Notes;
            m_NewContact.Address            = aPersonFormViewModel.Address;
            m_NewContact.BirthDate          = aPersonFormViewModel.BirthDate;
            m_NewContact.FirstActionDate    = aPersonFormViewModel.HireDate;
            m_NewContact.FirstChurchDate    = aPersonFormViewModel.HireDate;
            m_NewContact.Introducer         = aPersonFormViewModel.Introducer;
            m_NewContact.IntroducerPhone    = aPersonFormViewModel.IntroducerPhone;
            m_NewContact.IntroducerRelation = aPersonFormViewModel.IntroducerRelation;
            m_NewContact.IntroducerGroup    = aPersonFormViewModel.IntroducerGroup;

            // 性別
            if (aPersonFormViewModel.Gender == "男性")
            {
                m_NewContact.Gender = true;
            }
            else
            {
                m_NewContact.Gender = false;
            }

            // "未知", "已婚", "未婚", "離異", "喪偶","單身"
            m_NewContact.MerrageState = aPersonFormViewModel.MerrageState;

            if (aPersonFormViewModel.Position == "0" || aPersonFormViewModel.Position == "1" || aPersonFormViewModel.Position == "2" || aPersonFormViewModel.Position == "3" || aPersonFormViewModel.Position == "4" || aPersonFormViewModel.Position == "5")
            {
                int GroupIndex = Convert.ToInt32(aPersonFormViewModel.Position);

                // 幸福小組長上傳新人有可能沒有所屬小組可選
                if (SmallGroupDataList.m_AssignSmallGroupList.AssignSmallGroupListData.Count > 0)
                {
                    // 如果有所屬小組
                    m_NewContact.GroupName = SmallGroupDataList.m_AssignSmallGroupList.AssignSmallGroupListData[Convert.ToInt32(aPersonFormViewModel.Position)].Name;
                }
                else
                {
                    // 沒有所屬小組，有可能是個人或是幸福小組回報
                    m_NewContact.GroupName = "";
                }
            }
            else
            {
                m_NewContact.GroupName = aPersonFormViewModel.Position;
            }

            // "基督徒", "慕道友"
            m_NewContact.FaithStatus = aPersonFormViewModel.Status;

        }

    }
}
