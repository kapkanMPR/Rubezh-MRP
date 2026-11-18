using System;
namespace Rubezh.Camp
{
    [Serializable] public sealed class RaidProgress
    {
        public int record, rewarded, cloth, parts, copies;
        public string session, lastSummary;
        public int Start => Math.Max(1,Math.Min(100,record/10*10));
        public static int Supply(int wave) => 50+25*((wave-1)/10);
        public static int Cloth(int wave) => 1+(wave-1)*2/5;
        public static int Parts(int wave) => wave%5==0?1+(wave-1)/20:0;
        // Retained for old serialized data / callers; raids no longer award weapons.
        public static int Copies(int wave) => 0;
        public int Award(int wave)
        {
            if(wave<1||wave>100)throw new ArgumentOutOfRangeException(nameof(wave));
            record=Math.Max(record,wave);
            if(wave<=rewarded)return 0;
            cloth+=Cloth(wave);parts+=Parts(wave);rewarded=wave;
            return Supply(wave);
        }
    }
    public sealed partial class CampModel
    {
        public int AwardRaidWave(int wave)
        {
            int earned=S.raid.Award(wave);
            S.balance+=earned;
            return earned;
        }
    }
}
