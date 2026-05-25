using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.SuperAdmin;

public class SuperAdminNotificationsViewModel
{
    public List<Notification> Items { get; set; } = new();
    public int UnreadCount { get; set; }
}
