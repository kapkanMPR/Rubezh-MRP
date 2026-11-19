using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rubezh.Camp;
using Rubezh.UnityPort;
using SilentOrders.Core;

class AimTimingCheck
{
    static int count;

    static void Check(bool ok, string why)
    {
        if (!ok)
            throw new Exception(why);
        count++;
    }

    static void Near(float a, float b, string why) => Check(Math.Abs(a - b) < .0001, why + " " + a + "/" + b);

    static Contact Target(int id, V2 p) =>
        new Contact
        {
            EnemyId = id,
            Position = p,
            Visual = true,
        };

    static void Main()
    {
        var data = JsonConvert.DeserializeObject<Catalog>(
            File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json"),
            CampModel.JsonSettings
        );
        var m = new CampModel(data, rng: () => .37);
        string[] guns = { "ak", "gp", "m4", "m4gp", "pkm", "m249", "svd", "m24" };
        float[] seconds = { 1, 1, 1, 1, 1.5f, 1.5f, 1.5f, 1.75f };
        for (int i = 0; i < guns.Length; i++)
        {
            var b = new BattleSimulation(127);
            var s = EquipmentDetails.Gun(guns[i]);
            s.Position = new V2(10, 10);
            s.Awareness.Look = new V2(1, 0);
            var t = Target(11, new V2(15, 10));
            Near(WeaponBalance.For(s).AimSeconds, seconds[i], "profile " + guns[i]);
            ReactionTiming.Observe(b, s, t.EnemyId, t.Position, s.Awareness.Look);
            Check(!ReactionTiming.WeaponReady(b, s, t), "not immediate " + guns[i]);
            Near(s.Awareness.Reaction.ReadyAt, .5f, "base reaction unchanged");
            Near(s.Awareness.Reaction.AimReadyAt, .5f + seconds[i], "additive front acquisition");
            b.Time = .5f + seconds[i] - .01f;
            Check(!ReactionTiming.WeaponReady(b, s, t), "not before deadline");
            b.Time = .5f + seconds[i];
            Check(ReactionTiming.WeaponReady(b, s, t), "at deadline");
            float deadline = s.Awareness.Reaction.AimReadyAt;
            for (int n = 0; n < 8; n++)
            {
                b.Time += .1f;
                Check(ReactionTiming.WeaponReady(b, s, t), "subsequent shot no aim");
                Near(s.Awareness.Reaction.AimReadyAt, deadline, "deadline stable");
            }
            var t2 = Target(12, new V2(15, 10));
            ReactionTiming.Observe(b, s, 12, t2.Position, s.Awareness.Look);
            Check(!ReactionTiming.WeaponReady(b, s, t2), "switch starts new aim");
            Near(s.Awareness.Reaction.AimReadyAt, b.Time + .5f + seconds[i], "switch additive");
            var fields = EquipmentDetails.Build(m, data.Item(guns[i])).Right;
            Check(fields.Any(f => f.Name == "Время наведения"), "card aim row");
        }
        var rearBattle = new BattleSimulation(128);
        var rifle = EquipmentDetails.Gun("ak");
        rifle.Position = new V2(10, 10);
        rifle.Awareness.Look = new V2(1, 0);
        var rear = Target(9, new V2(5, 10));
        ReactionTiming.Observe(rearBattle, rifle, 9, rear.Position, rifle.Awareness.Look);
        ReactionTiming.WeaponReady(rearBattle, rifle, rear);
        Near(rifle.Awareness.Reaction.ReadyAt, 1.5f, "rear reaction unchanged");
        Near(rifle.Awareness.Reaction.AimReadyAt, 2.5f, "rear additive");
        rearBattle.Time = 3;
        Check(ReactionTiming.WeaponReady(rearBattle, rifle, rear), "ready rear");
        rifle.WeaponVariant = 1;
        rifle.GunRoleOverride = (int)Role.MachineGunner;
        Check(!ReactionTiming.WeaponReady(rearBattle, rifle, rear), "weapon switch re-aims");
        Near(rifle.Awareness.Reaction.AimReadyAt, 4.5f, "MG switch duration");
        rearBattle.Time = 5;
        ReactionTiming.Observe(rearBattle, rifle, 9, rear.Position, rifle.Awareness.Look);
        Check(!ReactionTiming.WeaponReady(rearBattle, rifle, rear), "reacquisition after loss");
        Near(rifle.Awareness.Reaction.AimReadyAt, 8f, "reacquired rear 1.5 plus MG1.5");
        Near(ArcedOrdnance.UnderbarrelAimSeconds, 2, "UGL2");
        Near(ArcedOrdnance.HandAimSeconds, 1, "grenade1");
        Near(SpecialWeapons.LauncherAimSeconds, 2.5f, "launcher2.5");
        foreach (string id in new[] { "gp", "m4gp" })
            Check(
                EquipmentDetails
                    .Build(m, data.Item(id))
                    .Right.Any(f => f.Name == "Наведение подствольника" && f.Value == "2 с"),
                "separate GP aim card"
            );
        foreach (string id in new[] { "rpg", "rshg", "rgd", "f1", "rgn", "rgo", "smoke", "vog" })
            Check(
                EquipmentDetails.Build(m, data.Item(id)).Right.Any(f => f.Name == "Время наведения"),
                "explosive aim card " + id
            );
        var smokeBattle = new BattleSimulation(129);
        var carrier = smokeBattle.Soldiers[0];
        carrier.Position = new V2(10, 10);
        carrier.HandsBusyUntil = 0;
        carrier.SmokeCharges = 0;
        SmokeThrow.Launch(smokeBattle, carrier, new V2(12, 10));
        Check(smokeBattle.Projectiles.Count == 0, "smoke has preparation, no projectile yet");
        smokeBattle.Time = .99f;
        SmokeThrow.Update(smokeBattle);
        Check(smokeBattle.Projectiles.Count == 0, "smoke waits 1s");
        smokeBattle.Time = 1;
        SmokeThrow.Update(smokeBattle);
        Check(smokeBattle.Projectiles.Count == 1, "smoke launches at1s");
        SmokeThrow.Update(smokeBattle);
        Check(smokeBattle.Projectiles.Count == 1, "no duplicate smoke");
        smokeBattle.Time = 2;
        SmokeThrow.Launch(smokeBattle, carrier, new V2(12, 10));
        carrier.Health = 0;
        smokeBattle.Time = 3;
        SmokeThrow.Update(smokeBattle);
        Check(smokeBattle.Projectiles.Count == 1 && carrier.SmokeThrowAt < 0, "dead carrier cannot complete throw");
        Check(carrier.SmokeCharges == 1, "cancelled reserved smoke returned");
        Console.WriteLine("PASS aim timing checks=" + count + "; portable simulation contracts");
    }
}
