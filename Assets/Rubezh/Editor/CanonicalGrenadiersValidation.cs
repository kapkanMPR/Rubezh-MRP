#if UNITY_EDITOR
using System;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Rubezh.Camp;

public static class CanonicalGrenadiersValidation
{
    [MenuItem("Рубеж/Проверки/Персонажи/гранатомётчиков")]
    public static void Validate()
    {
        var catalog = JsonConvert.DeserializeObject<Catalog>(Resources.Load<TextAsset>("FieldCamp/catalog").text);
        string[] names = { "Вектор", "Пепел", "Фугас" };
        int[] heads = { 107, 134, 136 };
        for (int i = 0; i < 3; i++)
        {
            double roll = (i + .1) / 3;
            var m = new CampModel(catalog, null, () => roll);
            m.S.story = 100;
            m.S.balance = 100000;
            string one = m.Enlist("rpg"),
                two = m.Enlist("rpg");
            Require(one != two, "Duplicate instance IDs");
            Require(m.Soldier(one).name == names[i] && m.Soldier(two).name == names[i], "Wrong grenadier");
            var look = m.S.looks[one];
            Require(
                look.headId == heads[i] && look.lightId == 62 && look.familyId == 89 && look.heavyId == 89,
                "Wrong appearance"
            );
            Require(!ReferenceEquals(m.S.gear[one], m.S.gear[two]), "Shared equipment");
            m.S.career.people[one].level = 8;
            var restored = new CampModel(catalog, CampModel.Decode(m.Serialize()), () => roll);
            Require(
                restored.S.career.people[one].level == 8 && restored.S.career.people[two].level == 1,
                "Independent progression"
            );
            Require(restored.S.looks[one].heavyId == 89 && restored.Soldier(one).name == names[i], "Save migration");
            var gl = new Rubezh.GreenLook
            {
                headId = look.headId,
                lightId = look.lightId,
                familyId = look.familyId,
                heavyId = look.heavyId,
            };
            gl.Ensure(SilentOrders.Core.Role.Grenadier, false);
            for (int rig = 0; rig < 3; rig++)
            for (int frame = 0; frame < 4; frame++)
            {
                int expected = Rubezh.GreenLook.UniformBodyStart + frame;
                Require(gl.Body(2, rig, frame) == expected, "Fixed rifleman body / rig 2");
            }
            Require(
                Resources.Load<Texture2D>("FieldCamp/Art/" + m.PortraitArt(one).Replace(".png", "")) != null,
                "Missing portrait"
            );
        }
        Debug.Log("Canonical grenadiers 111: validation passed");
    }

    static void Require(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }
}
#endif
