using System;
using Newtonsoft.Json;
using Rubezh.Camp;
using Rubezh.UnityPort;

class RaidUiCheck
{
    static void Check(bool v, string message)
    {
        if (!v)
            throw new Exception(message);
    }

    public static void Main()
    {
        int cases = 0;
        foreach (int wave in new[] { 0, 1, 5, 9, 10, 14, 50, 99, 100 })
        foreach (bool pending in new[] { false, true })
        {
            var p = new RaidProgress();
            int supply = 0;
            for (int i = 1; i <= wave; i++)
                supply += p.Award(i);
            var r = new RaidSession
            {
                start = 1,
                wave = wave + 1,
                cleared = wave,
                pending = pending,
                cloth = p.cloth,
                parts = p.parts,
                copies = p.copies,
                supply = supply,
                seconds = 123,
                kills = 17,
            };
            p.session = JsonConvert.SerializeObject(r);
            int cloth = p.cloth,
                parts = p.parts,
                copies = p.copies,
                record = p.record,
                rewarded = p.rewarded,
                start = p.Start;
            string summary = RaidSession.CloseSaved(p);
            Check(summary != null && p.session == null && p.lastSummary == summary, "close");
            Check(summary.Contains("пройдено боёв: " + wave) && summary.Contains("Уничтожено: 17"), "stats");
            Check(
                p.cloth == cloth
                    && p.parts == parts
                    && p.copies == copies
                    && p.record == record
                    && p.rewarded == rewarded
                    && p.Start == start,
                "no awards or checkpoint mutation"
            );
            Check(RaidSession.CloseSaved(p) == null && p.lastSummary == summary, "close once");
            for (int i = 1; i <= wave; i++)
                Check(p.Award(i) == 0, "no repeated supply");
            Check(p.cloth == cloth && p.parts == parts && p.copies == copies, "no repeated materials");
            cases++;
        }
        Console.WriteLine(
            "PASS "
                + cases
                + " interrupted raid cases: normal/pending, checkpoints, statistics, repeat entry and reward deduplication"
        );
    }
}
