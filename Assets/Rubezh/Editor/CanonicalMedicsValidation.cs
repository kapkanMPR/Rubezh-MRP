#if UNITY_EDITOR
using System;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Rubezh.Camp;

public static class CanonicalMedicsValidation
{
    [MenuItem("Рубеж/Проверки/Персонажи/медиков")]
    public static void Validate()
    {
        var catalog = JsonConvert.DeserializeObject<Catalog>(Resources.Load<TextAsset>("FieldCamp/catalog").text);
        string[] names = { "Феникс", "Добрый", "Хирург" };
        int[] heads = { 88, 106, 109 };
        for (int i = 0; i < 3; i++)
        {
            double roll = (i + .1) / 3;
            var m = new CampModel(catalog, null, () => roll);
            m.S.story = 100;
            m.S.balance = 100000;
            string one = m.Enlist("med"),
                two = m.Enlist("med");
            Require(one != two, "Duplicate instance IDs");
            Require(m.Soldier(one).name == names[i] && m.Soldier(two).name == names[i], "Wrong medic");
            var look = m.S.looks[one];
            Require(
                look.headId == heads[i] && look.lightId == 83 && look.familyId == 63 && look.heavyId == 80,
                "Wrong appearance"
            );
            Require(!ReferenceEquals(m.S.gear[one], m.S.gear[two]), "Shared equipment");
            m.S.career.people[one].level = 8;
            var restored = new CampModel(catalog, CampModel.Decode(m.Serialize()), () => roll);
            Require(
                restored.S.career.people[one].level == 8 && restored.S.career.people[two].level == 1,
                "Independent progression"
            );
            Require(restored.S.looks[one].heavyId == 80 && restored.Soldier(one).name == names[i], "Save migration");
            var gl = new Rubezh.GreenLook
            {
                headId = look.headId,
                lightId = look.lightId,
                familyId = look.familyId,
                heavyId = look.heavyId,
            };
            gl.Ensure(SilentOrders.Core.Role.Medic, false);
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
        Debug.Log("Canonical medics 109: validation passed");
    }

    static void Require(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }
}
#endif
