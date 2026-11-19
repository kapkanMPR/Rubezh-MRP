using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class RadioReport
    {
        public int Sender;
        public Side Side;
        public Contact Contact;
        public float DeliverAt;
    }
}
