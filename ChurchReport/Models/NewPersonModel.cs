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
            LastName = "張大同",
            Gender = "男性",
            Phone = "0910-391-931",
            Position = "行銷經理",
            BirthDate = DateTime.Parse("1962/09/27"),
            HireDate = DateTime.Parse("2005/06/25"),
            Notes = "先生換工作，所以剛搬來楊梅，正在尋找教會中....",
            Address = "桃園市楊梅區中山路2段5號3樓",
            ReadBibleNumber = 5,
            Status = "慕道友"

        };

        public NewContact m_NewContact = new NewContact();

        public void SetupNewPersonModel(PersonFormViewModel aPersonFormViewModel)
        {
            CopyPersonFormViewModel(aPersonFormViewModel);
        }

        public void UploadWeeklyReport( String Account, String Password, PersonFormViewModel aPersonFormViewModel)
        {
            CopyPersonFormViewModel(aPersonFormViewModel);


            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            NewPerson aNewPersonManager = new NewPerson();

            MappingPersonFormViewModelToNewContact(aPersonFormViewModel);

            aNewPersonManager.CreateNewContact( aAccountPasswordData, m_NewContact );

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
            m_NewContact.Name           = aPersonFormViewModel.FirstName;
            m_NewContact.MobilePhone    = aPersonFormViewModel.Phone;
            m_NewContact.Note           = aPersonFormViewModel.Notes;
            m_NewContact.Address        = aPersonFormViewModel.Address;

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
