using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.LessonsOperations
{
    public interface ILessonsService
    {
        EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId);
        EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate);
        EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId);
    }
}
