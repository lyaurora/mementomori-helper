using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MementoMori.Ortega.Share.Master.Data;
using MementoMori.Ortega.Share.Utils;

namespace MementoMori
{
    public class TimeManager
    {
        public TimeSpan DiffFromUtc => TimeSpan.Parse(_timeServerMb.DifferenceDateTimeFromUtc);

        private TimeServerMB _timeServerMb;

        public void SetTimeServerMb(TimeServerMB timeServerMb)
        {
            _timeServerMb = timeServerMb;
        }

        public DateTime ServerNow=> DateTime.UtcNow + DiffFromUtc;

        public bool IsInTime(IHasStartEndTime hasStartEndTime)
        {
            // Master Start/EndTime are server-local, like the game client's GetLocalDateTime().
            var now = ServerNow;
            var start = DateTime.Parse(hasStartEndTime.StartTime, CultureInfo.InvariantCulture);
            var end = DateTime.Parse(hasStartEndTime.EndTime, CultureInfo.InvariantCulture);
            return now >= start && now <= end;
        }
    }
}
