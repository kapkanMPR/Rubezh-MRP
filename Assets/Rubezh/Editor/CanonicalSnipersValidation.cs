#if UNITY_EDITOR
using System;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Rubezh.Camp;
public static class CanonicalSnipersValidation
{
    [MenuItem("Рубеж/Проверки/Персонажи/снайперов")]
    public static void Validate()
    {
        var catalog=JsonConvert.DeserializeObject<Catalog>(Resources.Load<TextAsset>("FieldCamp/catalog").text);
        string[] names={"Седой","Морок","Манул"}; int[] heads={143,62,69};
        for(int i=0;i<3;i++) {
            double roll=(i+.1)/3;
            var m=new CampModel(catalog,null,()=>roll);
            m.S.story=100; m.S.balance=100000;
            string one=m.Enlist("sn"),two=m.Enlist("sn");
            Require(one!=two,"Duplicate instance IDs");
            Require(m.Soldier(one).name==names[i] && m.Soldier(two).name==names[i],"Wrong archetype");
            Require(m.S.looks[one].headId==heads[i] && m.S.looks[one].lightId==55 && m.S.looks[one].familyId==95,"Wrong appearance");
            Require(!ReferenceEquals(m.S.gear[one],m.S.gear[two]),"Shared equipment");
            Require(!ReferenceEquals(m.S.career.people[one],m.S.career.people[two]),"Shared progression");
            m.S.career.people[one].level=8;
            var restored=new CampModel(catalog,CampModel.Decode(m.Serialize()),()=>roll);
            Require(restored.S.career.people[one].level==8 && restored.S.career.people[two].level==1,"Progress migration");
            Require(restored.Soldier(one).name==names[i],"Archetype migration");
            Require(Resources.Load<Texture2D>("FieldCamp/Art/"+m.PortraitArt(one).Replace(".png",""))!=null,"Missing art");
        }
        string[] rifleNames={"Клён","Вереск","Гранит"};
        for(int i=0;i<3;i++) {
            double roll=(i+.1)/3;
            var m=new CampModel(catalog,null,()=>roll);m.S.story=100;m.S.balance=100000;
            string x=m.Enlist("dozor"),y=m.Enlist("dozor");
            Require(x!=y && m.Soldier(x).name==rifleNames[i] && m.Soldier(y).name==rifleNames[i],"Rifle recruitment");
            Require(m.S.looks[x].lightId==62 && m.S.looks[x].familyId==89 && m.S.looks[x].heavyId==89,"Rifle armor");
            m.S.career.people[x].level=8;
            var restored=new CampModel(catalog,CampModel.Decode(m.Serialize()));
            Require(restored.S.career.people[x].level==8 && restored.S.career.people[y].level==1,"Rifle duplicate progression");
            Require(Resources.Load<Texture2D>("FieldCamp/Art/"+m.PortraitArt(x).Replace(".png",""))!=null,"Rifle portrait");
        }
        Debug.Log("Canonical snipers and riflemen: validation passed");
    }
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
}
#endif
