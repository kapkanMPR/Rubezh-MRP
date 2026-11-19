namespace SilentOrders.Core
{
    public static class DebrisDimensions
    {
        public static readonly int[] Widths =
        {
            320,
            198,
            250,
            218,
            250,
            198,
            294,
            245,
            238,
            259,
            267,
            197,
            284,
            285,
            226,
            174,
            243,
            216,
            227,
            219,
            224,
            196,
            233,
            213,
            243,
            216,
            248,
            251,
            184,
            238,
            292,
            238,
            186,
            235,
            211,
            182,
            88,
            86,
        };
        public static readonly int[] Heights =
        {
            210,
            197,
            238,
            172,
            228,
            151,
            198,
            186,
            189,
            186,
            214,
            178,
            169,
            161,
            219,
            178,
            160,
            163,
            169,
            152,
            152,
            146,
            161,
            143,
            143,
            143,
            141,
            159,
            133,
            169,
            157,
            133,
            128,
            179,
            133,
            125,
            128,
            128,
        };

        public static int Height(int id, int width)
        {
            if (id >= 41)
                return System.Math.Max(2, (int)System.Math.Round(width * DebrisCatalog.Aspect(id)));
            if (id >= 39)
                return System.Math.Max(4, width / 2);
            return System.Math.Max(4, (int)System.Math.Round(width * (double)Heights[id - 1] / Widths[id - 1]));
        }
    }
}
