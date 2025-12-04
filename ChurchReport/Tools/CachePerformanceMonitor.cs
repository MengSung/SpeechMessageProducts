using System;
using System.Diagnostics;

namespace ChurchReport.Tools
{
    /// <summary>
    /// ? Phase 3.2: §Ö¨ת®Ä¯א÷Ê±±¤u¨ד
    /// ¥Î©ף´ת¶q§Ö¨ת±a¨Ó×÷®Ä¯א§ןµ½
    /// </summary>
    public class CachePerformanceMonitor
    {
        private readonly Stopwatch _stopwatch;
        private long _firstCallTime;
        private long _secondCallTime;
        private string _operationName;

        public CachePerformanceMonitor()
        {
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// ¶}©l÷Ê±±¡]²Ä¤@¦¸©I¥s - Cache Miss¡^
        /// </summary>
        public void StartFirstCall(string operationName)
        {
            _operationName = operationName;
            _stopwatch.Restart();
        }

        /// <summary>
        /// µ²§פ²Ä¤@¦¸©I¥s
        /// </summary>
        public void EndFirstCall()
        {
            _stopwatch.Stop();
            _firstCallTime = _stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// ¶}©l²Ä¤G¦¸©I¥s¡]Cache Hit¡^
        /// </summary>
        public void StartSecondCall()
        {
            _stopwatch.Restart();
        }

        /// <summary>
        /// µ²§פ²Ä¤G¦¸©I¥s
        /// </summary>
        public void EndSecondCall()
        {
            _stopwatch.Stop();
            _secondCallTime = _stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// ¨ת±o®Ä¯א³ר§i
        /// </summary>
        public string GetPerformanceReport()
        {
            if (_firstCallTime == 0 || _secondCallTime == 0)
            {
                return "©|¥¼§¹¦¨´ת¸Õ";
            }

            double improvement = _firstCallTime > 0 
                ? (double)_firstCallTime / _secondCallTime 
                : 0;

            return $@"
שÝששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששש‗
שר         ?? §Ö¨ת®Ä¯א´ת¸Õ³ר§i - {_operationName,-30} שר
שאשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששג
שר ²Ä¤@¦¸©I¥s (Cache Miss): {_firstCallTime,10} ms                    שר
שר ²Ä¤G¦¸©I¥s (Cache Hit):  {_secondCallTime,10} ms                    שר
שר ³t«×´£¤É:                {improvement,10:F1}x ­¿                 שר
שר ®É¶¡¸`¬Ù:                {_firstCallTime - _secondCallTime,10} ms                    שר
שאשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששג
שר ?? µû¦פ:                                                   שר
שר   {GetPerformanceLevel(improvement),-56} שר
שדשששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששששו
";
        }

        /// <summary>
        /// ¨ת±o®Ä¯אµ¥¯Åµû¦פ
        /// </summary>
        private string GetPerformanceLevel(double improvement)
        {
            if (improvement >= 100)
                return "????? ·¥­PÀu¤Æ¡I®Ä¯א´£¤ÉÅו¤H¡I";
            else if (improvement >= 50)
                return "???? ¨פ¶VÀu¤Æ¡I®Ä¯א¤j´T§ןµ½¡I";
            else if (improvement >= 20)
                return "??? ¨}¦nÀu¤Æ¡I®Ä¯א©תÅד´£¤É¡I";
            else if (improvement >= 5)
                return "?? ¤¤µ¥Àu¤Æ¡A®Ä¯א¦³©Ò§ןµ½";
            else if (improvement >= 2)
                return "? »´·LÀu¤Æ¡A¦³§ןµ½×Å¶¡";
            else
                return "?? Àu¤Æ®Ä×G¤£©תÅד¡A«ØÄ³ÀË¬d§Ö¨תµ¦²¤";
        }

        /// <summary>
        /// Â²¤Æ×©³ר§i¡]³ז¦ז¡^
        /// </summary>
        public string GetSimpleReport()
        {
            if (_firstCallTime == 0 || _secondCallTime == 0)
            {
                return "©|¥¼§¹¦¨´ת¸Õ";
            }

            double improvement = _firstCallTime > 0 
                ? (double)_firstCallTime / _secondCallTime 
                : 0;

            return $"[{_operationName}] Cache Miss: {_firstCallTime}ms | Cache Hit: {_secondCallTime}ms | ´£¤É: {improvement:F1}x ­¿";
        }

        /// <summary>
        /// ´ת¶q³ז¦¸¾Þ§@°ץ¦ז®É¶¡
        /// </summary>
        public static long MeasureOperation(Action operation, out string operationTime)
        {
            var stopwatch = Stopwatch.StartNew();
            operation();
            stopwatch.Stop();
            operationTime = $"{stopwatch.ElapsedMilliseconds} ms";
            return stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// ´ת¶q³ז¦¸¾Þ§@°ץ¦ז®É¶¡¡]«D¦P¨B¡^
        /// </summary>
        public static async System.Threading.Tasks.Task<(long elapsed, string display)> MeasureOperationAsync(Func<System.Threading.Tasks.Task> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();
            return (stopwatch.ElapsedMilliseconds, $"{stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
