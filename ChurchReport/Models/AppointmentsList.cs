using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class AppointmentsList
    {
        public List<Appointment> Appointments
        {
            get
            {
                return new List<Appointment> {
                    new Appointment {
                        AppointmentId = 1,
                        Text = "Website Re-Design Plan",
                        OwnerId = new int[] { 4 },
                        StartDate = new DateTime(2015, 5, 25, 9, 30, 0),
                        EndDate = new DateTime(2015, 5, 25, 11, 30, 0)
                    },
                    new Appointment {
                        AppointmentId = 2,
                        Text = "Book Flights to San Fran for Sales Trip",
                        OwnerId = new int[] { 2 },
                        StartDate = new DateTime(2015, 5, 25, 12, 0, 0),
                        EndDate = new DateTime(2015, 5, 25, 13, 0, 0),
                        AllDay = true
                    },
                    new Appointment {
                        AppointmentId = 3,
                        Text = "Install New Router in Dev Room",
                        OwnerId = new int[] { 1 },
                        StartDate = new DateTime(2015, 5, 25, 14, 30, 0),
                        EndDate = new DateTime(2015, 5, 25, 15, 30, 0)
                    },
                    new Appointment {
                        AppointmentId = 4,
                        Text = "Approve Personal Computer Upgrade Plan",
                        OwnerId = new int[] { 3 },
                        StartDate = new DateTime(2015, 5, 26, 10, 0, 0),
                        EndDate = new DateTime(2015, 5, 26, 11, 0, 0)
                    },
                    new Appointment {
                        AppointmentId = 5,
                        Text = "Final Budget Review",
                        OwnerId = new int[] { 1 },
                        StartDate = new DateTime(2015, 5, 26, 12, 0, 0),
                        EndDate = new DateTime(2015, 5, 26, 13, 35, 0)
                    },
                    new Appointment {
                        AppointmentId = 6,
                        Text = "New Brochures",
                        OwnerId = new int[] { 4 },
                        StartDate = new DateTime(2015, 5, 26, 14, 30, 0),
                        EndDate = new DateTime(2015, 5, 26, 15, 45, 0)
                    },
                    new Appointment {
                        AppointmentId = 7,
                        Text = "Install New Database",
                        OwnerId = new int[] { 2 },
                        StartDate = new DateTime(2015, 5, 27, 9, 45, 0),
                        EndDate = new DateTime(2015, 5, 27, 11, 15, 0)
                    },
                    new Appointment {
                        AppointmentId = 8,
                        Text = "Approve New Online Marketing Strategy",
                        OwnerId = new int[] { 3, 4 },
                        StartDate = new DateTime(2015, 5, 27, 12, 0, 0),
                        EndDate = new DateTime(2015, 5, 27, 14, 0, 0)
                    },
                    new Appointment {
                        AppointmentId = 9,
                        Text = "Upgrade Personal Computers",
                        OwnerId = new int[] { 2 },
                        StartDate = new DateTime(2015, 5, 27, 15, 15, 0),
                        EndDate = new DateTime(2015, 5, 27, 16, 30, 0)
                    },
                    new Appointment {
                        AppointmentId = 10,
                        Text = "Customer Workshop",
                        OwnerId = new int[] { 3 },
                        StartDate = new DateTime(2015, 5, 28, 11, 0, 0),
                        EndDate = new DateTime(2015, 5, 28, 12, 0, 0),
                        AllDay = true
                    },
                    new Appointment {
                        AppointmentId = 11,
                        Text = "Prepare 2015 Marketing Plan",
                        OwnerId = new int[] { 1, 3 },
                        StartDate = new DateTime(2015, 5, 28, 11, 0, 0),
                        EndDate = new DateTime(2015, 5, 28, 13, 30, 0)
                    },
                    new Appointment {
                        AppointmentId = 12,
                        Text = "Brochure Design Review",
                        OwnerId = new int[] { 4 },
                        StartDate = new DateTime(2015, 5, 28, 14, 0, 0),
                        EndDate = new DateTime(2015, 5, 28, 15, 30, 0)
                    },
                    new Appointment {
                        AppointmentId = 13,
                        Text = "Create Icons for Website",
                        OwnerId = new int[] { 3 },
                        StartDate = new DateTime(2015, 5, 29, 10, 0, 0),
                        EndDate = new DateTime(2015, 5, 29, 11, 30, 0)
                    },
                    new Appointment {
                        AppointmentId = 14,
                        Text = "Upgrade Server Hardware",
                        OwnerId = new int[] { 4 },
                        StartDate = new DateTime(2015, 5, 29, 14, 30, 0),
                        EndDate = new DateTime(2015, 5, 29, 16, 0, 0)
                    },
                    new Appointment {
                        AppointmentId = 15,
                        Text = "Submit New Website Design",
                        OwnerId = new int[] { 1 },
                        StartDate = new DateTime(2015, 5, 29, 16, 30, 0),
                        EndDate = new DateTime(2015, 5, 29, 18, 0, 0)
                    },
                    new Appointment {
                        AppointmentId = 16,
                        Text = "Launch New Website",
                        OwnerId = new int[] { 2 },
                        StartDate = new DateTime(2015, 5, 29, 12, 20, 0),
                        EndDate = new DateTime(2015, 5, 29, 14, 0, 0)
                    },
                    new Appointment {
                        AppointmentId = 17,
                        Text = "Stand-up meeting",
                        OwnerId = new int[] { 1, 2, 3, 4 },
                        StartDate = new DateTime(2015, 5, 25, 9, 0, 0),
                        EndDate = new DateTime(2015, 5, 25, 9, 15, 0),
                        RecurrenceRule = "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR;UNTIL=20150530"
                    }
                };
            }
        }
    }
}


