using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class DefenseState
    {
        public bool Initialized;
        public bool Active;
        public float NextReview;
        public float Started;
        public float LineX = 50f;
        public float LineY = 18.5f;
        public float LastHealth = 500f;
        public int Cursor;
        public int Plans;
        public int Completed;
        public int Aborted;
        public int Target = -1;
        public string Mode = "Линия обороны";
        public readonly System.Collections.Generic.List<int> Movers = new System.Collections.Generic.List<int>();
    }
}
