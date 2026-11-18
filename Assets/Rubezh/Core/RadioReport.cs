using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class RadioReport
    {
        public int Sender;
        public SilentOrders.Core.Side Side;
        public SilentOrders.Core.Contact Contact;
        public float DeliverAt;
    }
}
