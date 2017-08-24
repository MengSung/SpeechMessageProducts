using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.ViewModels;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.WebServiceConnector;

namespace ChurchReport.Models
{
    public class RegisterManager
    {
        public String Register( String FullName, String Mobile, String Account, String Password, String ConfirmPassword)
        {
            RegisterConnector aRegisterConnector = new RegisterConnector();

            return aRegisterConnector.Register( FullName,  Mobile,  Account,  Password, ConfirmPassword );
        }

    }
}
