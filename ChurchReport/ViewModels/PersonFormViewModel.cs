using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.ViewModels
{
    public class PersonFormViewModel
    {
        public PersonFormViewModel()
        { }

        public int ID { get; set; }

        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String Gender { get; set; }
        public String Phone { get; set; }
        public String Position { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime HireDate { get; set; }
        public String Notes { get; set; }
        public String Address { get; set; }
        public int ReadBibleNumber { get; set; }
        public String Status { get; set; }


        public object FormData { get; set; }
    }
}
