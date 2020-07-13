using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class PollModel
    {
        public PollModel()
        { }

        public bool WorshipPoll { get; set; }                   //敬拜團
        public bool MusicDevice { get; set; }                   //音控
        public bool DisplayPpt { get; set; }                    //放印PPT
        public String PollContent { get; set; }                 //意見回饋調查
    }
}
