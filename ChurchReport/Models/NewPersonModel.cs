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
        public PersonFormViewModel PersonFormViewModel = new PersonFormViewModel
        {
            ID = 1,
            FirstName = "張",
            LastName = "",
            Gender = "男性",
            Phone = "",
            Position = "",
            MerrageState="未婚",
            BirthDate = DateTime.Parse("1975/01/1"),
            HireDate = DateTime.Parse("2017/08/25"),
            Notes = "",
            Address = "",
            ReadBibleNumber = 5,
            Status = "慕道友"

        };

        public NewContact m_NewContact = new NewContact();

        public void SetupNewPersonModel(PersonFormViewModel aPersonFormViewModel)
        {
            CopyPersonFormViewModel(aPersonFormViewModel);
        }

        public String UploadNewPerson( String Account, String Password, PersonFormViewModel aPersonFormViewModel)
        {
            CopyPersonFormViewModel(aPersonFormViewModel);

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            NewPerson aNewPersonManager = new NewPerson();

            MappingPersonFormViewModelToNewContact(aPersonFormViewModel);

            return aNewPersonManager.CreateNewContact( aAccountPasswordData, m_NewContact );
        }

        public void CopyPersonFormViewModel(PersonFormViewModel aPersonFormViewModel)
        {
            PersonFormViewModel.FirstName = aPersonFormViewModel.FirstName;
            PersonFormViewModel.LastName = aPersonFormViewModel.LastName;
            PersonFormViewModel.Gender = aPersonFormViewModel.Gender;
            PersonFormViewModel.Phone = aPersonFormViewModel.Phone;
            PersonFormViewModel.Position = aPersonFormViewModel.Position;
            PersonFormViewModel.BirthDate = aPersonFormViewModel.BirthDate;
            PersonFormViewModel.HireDate = aPersonFormViewModel.HireDate;
            PersonFormViewModel.Notes = aPersonFormViewModel.Notes;
            PersonFormViewModel.Address = aPersonFormViewModel.Address;
            PersonFormViewModel.ReadBibleNumber = aPersonFormViewModel.ReadBibleNumber;
            PersonFormViewModel.Status = aPersonFormViewModel.Status;

        }
        public void MappingPersonFormViewModelToNewContact(PersonFormViewModel aPersonFormViewModel)
        {
            //m_NewContact.Name               = aPersonFormViewModel.FirstName;
            m_NewContact.Name               = aPersonFormViewModel.LastName;
            m_NewContact.MobilePhone        = aPersonFormViewModel.Phone;
            m_NewContact.Note               = aPersonFormViewModel.Notes;
            m_NewContact.Address            = aPersonFormViewModel.Address;
            m_NewContact.BirthDate          = aPersonFormViewModel.BirthDate;
            m_NewContact.FirstActionDate    = aPersonFormViewModel.HireDate;
            m_NewContact.FirstChurchDate    = aPersonFormViewModel.HireDate;
            if(aPersonFormViewModel.Gender == "男性")
            {
                m_NewContact.Gender = true;
            }
            else
            {
                m_NewContact.Gender = false;
            }

            m_NewContact.MerrageState = aPersonFormViewModel.MerrageState;

            if (aPersonFormViewModel.Position == "0" || aPersonFormViewModel.Position == "1" || aPersonFormViewModel.Position == "2" || aPersonFormViewModel.Position == "3" || aPersonFormViewModel.Position == "4" || aPersonFormViewModel.Position == "5")
            {
                int GroupIndex = Convert.ToInt32(aPersonFormViewModel.Position);
                m_NewContact.GroupName = AssignSmallGroupList.AssignSmallGroupListData[Convert.ToInt32(aPersonFormViewModel.Position)].Name;
            }
            else
            {
                m_NewContact.GroupName = aPersonFormViewModel.Position;
            }

            m_NewContact.FaithStatus = aPersonFormViewModel.Status;

            //PersonFormViewModel.FirstName = aPersonFormViewModel.FirstName;
            //PersonFormViewModel.LastName = aPersonFormViewModel.LastName;
            //PersonFormViewModel.Gender = aPersonFormViewModel.Gender;
            //PersonFormViewModel.Phone = aPersonFormViewModel.Phone;
            //PersonFormViewModel.Position = aPersonFormViewModel.Position;
            //PersonFormViewModel.BirthDate = aPersonFormViewModel.BirthDate;
            //PersonFormViewModel.HireDate = aPersonFormViewModel.HireDate;
            //PersonFormViewModel.Notes = aPersonFormViewModel.Notes;
            //PersonFormViewModel.Address = aPersonFormViewModel.Address;
            //PersonFormViewModel.ReadBibleNumber = aPersonFormViewModel.ReadBibleNumber;
            //PersonFormViewModel.Status = aPersonFormViewModel.Status;

        }

    }
}
