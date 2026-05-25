using System;

namespace ViaReservaERP
{
    public static class AppTime
    {
        /// <summary>
        /// Returns the current time in the Philippines (UTC+8).
        /// </summary>
        public static DateTime Now => DateTime.UtcNow.AddHours(8);
    }
}
