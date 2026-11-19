using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Rubezh.UnityPort.Editor;
using UnityEditor;
using UnityEditor.Build;

class MigrationCheck
{
    const string Old = BuildResourcePreparation.LegacyPacked,
        Current = BuildResourcePreparation.Packed;
    const string Key = "0123456789abcdef01234567";
    static int assertions,
        scenarios;
    static string originalDirectory;

    static void Check(bool value, string message)
    {
        assertions++;
        if (!value)
            throw new Exception(message);
    }

    static void Put(string path, string data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, data);
    }

    static string Read(string path) => File.ReadAllText(path);

    static string[] Backups() =>
        Directory.Exists("Build/Backups/MaterialMigration")
            ? Directory.GetDirectories("Build/Backups/MaterialMigration")
            : new string[0];

    static void Throws(Action action, string text)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Check(e.Message.Contains(text), "expected error " + text + ", got " + e.Message);
            return;
        }
        throw new Exception("Expected failure: " + text);
    }

    static void Both(string oldValue = "same", string currentValue = "same")
    {
        Put(Old + "/" + Key + ".bytes", oldValue);
        Put(Old + "/" + Key + ".bytes.meta", "guid: old");
        Put(Old + ".meta", "guid: old-folder");
        Put(Current + "/" + Key + ".bytes", currentValue);
        Put(Current + "/" + Key + ".bytes.meta", "guid: active");
        Put(Current + ".meta", "guid: active-folder");
    }

    static void Case(string name, Action action)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "rubezh-material-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        Directory.SetCurrentDirectory(temporary);
        AssetDatabase.FailMove = AssetDatabase.FailDelete = false;
        UnityEngine.Debug.Warnings.Clear();
        try
        {
            for (int i = 1; i <= 20; i++)
                Put("Assets/Rubezh/Resources/Regenerated/Campaign_" + i.ToString("00") + ".bytes", "map");
            for (int i = 1; i <= 50; i++)
                Put("Assets/Rubezh/Resources/War/war-" + i.ToString("00") + ".bytes", "map");
            action();
            scenarios++;
            Console.WriteLine("PASS " + name);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(temporary, true);
        }
    }

    public static void Main(string[] args)
    {
        originalDirectory = Directory.GetCurrentDirectory();
        if (args.Contains("--reproduce"))
        {
            Case(
                "original 221 fails with duplicate folders",
                () =>
                {
                    Both();
                    Throws(BuildResourcePreparation.Prepare, "Both packed material folders exist");
                    Check(Directory.Exists(Old) && Directory.Exists(Current), "original state preserved");
                }
            );
            return;
        }
        Case(
            "no legacy directory",
            () =>
            {
                BuildResourcePreparation.Prepare();
                Check(Directory.Exists(Current), "target exists");
                Check(Backups().Length == 0, "no unnecessary backup");
            }
        );
        Case(
            "whole-folder migration keeps GUIDs",
            () =>
            {
                Put(Old + "/only.bytes", "only");
                Put(Old + "/only.bytes.meta", "guid: only");
                Put(Old + ".meta", "guid: folder");
                BuildResourcePreparation.Prepare();
                Check(!Directory.Exists(Old), "old removed");
                Check(Read(Current + "/only.bytes") == "only", "payload moved");
                Check(Read(Current + "/only.bytes.meta") == "guid: only", "asset GUID kept");
                Check(Read(Current + ".meta") == "guid: folder", "folder GUID kept");
            }
        );
        Case(
            "identical duplicates and repeat preparation",
            () =>
            {
                Both();
                BuildResourcePreparation.Prepare();
                Check(!Directory.Exists(Old), "legacy removed");
                Check(!File.Exists(Old + ".meta"), "legacy folder meta removed");
                Check(Read(Current + "/" + Key + ".bytes.meta") == "guid: active", "active GUID kept");
                string backup = Backups().Single();
                Check(Read(backup + "/Legacy/" + Key + ".bytes") == "same", "old payload backup");
                Check(Read(backup + "/Legacy/" + Key + ".bytes.meta") == "guid: old", "old meta backup");
                Check(Read(backup + "/Legacy.meta") == "guid: old-folder", "old folder meta backup");
                Check(Read(backup + "/Current/" + Key + ".bytes.meta") == "guid: active", "current meta backup");
                BuildResourcePreparation.Prepare();
                Check(Backups().Length == 1, "idempotent no extra backups");
                Check(Read(Current + "/" + Key + ".bytes") == "same", "idempotent pixels");
            }
        );
        Case(
            "different copies retain both and use active copy",
            () =>
            {
                Both("old pixels", "current pixels");
                BuildResourcePreparation.Prepare();
                string backup = Backups().Single();
                Check(Read(Current + "/" + Key + ".bytes") == "current pixels", "active wins");
                Check(Read(backup + "/Legacy/" + Key + ".bytes") == "old pixels", "old variant backed up");
                Check(Read(backup + "/Current/" + Key + ".bytes") == "current pixels", "current variant backed up");
                Check(UnityEngine.Debug.Warnings.Count == 1, "conflict visible");
                Check(Read(backup + "/migration.txt").Contains("Different; kept Editor copy"), "conflict logged");
            }
        );
        Case(
            "unique files and nested directories keep GUIDs",
            () =>
            {
                Both();
                Put(Old + "/unique.bytes", "unique");
                Put(Old + "/unique.bytes.meta", "guid: unique");
                Put(Current + "/target-only.bytes", "stay");
                Put(Old + "/nested/a.bytes", "a");
                Put(Current + "/nested/b.bytes", "b");
                Put(Old + "/new-folder/c.bytes", "c");
                Put(Old + "/new-folder.meta", "guid: nested-folder");
                BuildResourcePreparation.Prepare();
                Check(Read(Current + "/unique.bytes") == "unique", "unique preserved");
                Check(Read(Current + "/unique.bytes.meta") == "guid: unique", "unique GUID moved");
                Check(Read(Current + "/target-only.bytes") == "stay", "destination-only preserved");
                Check(
                    Read(Current + "/nested/a.bytes") == "a" && Read(Current + "/nested/b.bytes") == "b",
                    "nested merged"
                );
                Check(Read(Current + "/new-folder/c.bytes") == "c", "new folder moved");
                Check(Read(Current + "/new-folder.meta") == "guid: nested-folder", "new folder GUID moved");
            }
        );
        Case(
            "PNG originals regenerate packed data after merge",
            () =>
            {
                Both("old", "active");
                Put(BuildResourcePreparation.Source + "/" + Key + ".png", "png original");
                Put(BuildResourcePreparation.Source + "/" + Key + ".jpg", "jpeg export");
                BuildResourcePreparation.Prepare();
                Check(Read(Current + "/" + Key + ".bytes") == "png original", "lossless source wins");
                Check(
                    Read(Current + "/" + Key + ".bytes.meta") == "guid: active",
                    "regeneration retains canonical GUID"
                );
            }
        );
        Case(
            "metadata-only overlay restores payload",
            () =>
            {
                Both();
                Put(Old + "/missing.bytes", "restored");
                Put(Old + "/missing.bytes.meta", "guid: legacy-missing");
                Put(Current + "/missing.bytes.meta", "guid: active-missing");
                Put(Old + "/orphan.bytes.meta", "orphan metadata");
                BuildResourcePreparation.Prepare();
                Check(Read(Current + "/missing.bytes") == "restored", "payload restored");
                Check(
                    Read(Current + "/missing.bytes.meta") == "guid: active-missing",
                    "existing destination GUID kept"
                );
                Check(
                    Read(Backups().Single() + "/Legacy/orphan.bytes.meta") == "orphan metadata",
                    "orphan metadata backed up"
                );
            }
        );
        Case(
            "backup failure leaves both original trees untouched",
            () =>
            {
                Both("old", "active");
                Put("Build/Backups/MaterialMigration", "blocked");
                Throws(BuildResourcePreparation.Prepare, "MaterialMigration");
                Check(Read(Old + "/" + Key + ".bytes") == "old", "old intact");
                Check(Read(Current + "/" + Key + ".bytes") == "active", "current intact");
                File.Delete("Build/Backups/MaterialMigration");
                BuildResourcePreparation.Prepare();
                Check(!Directory.Exists(Old), "busy flag reset, retry succeeds");
            }
        );
        Case(
            "failed move and retry",
            () =>
            {
                Both();
                Put(Old + "/unique.bytes", "unique");
                AssetDatabase.FailMove = true;
                Throws(BuildResourcePreparation.Prepare, "Injected move failure");
                Check(Read(Old + "/unique.bytes") == "unique", "failed move keeps source");
                AssetDatabase.FailMove = false;
                BuildResourcePreparation.Prepare();
                Check(Read(Current + "/unique.bytes") == "unique", "retry successful");
            }
        );
        Case(
            "failed deletion and retry",
            () =>
            {
                Both();
                AssetDatabase.FailDelete = true;
                Throws(BuildResourcePreparation.Prepare, "Could not remove");
                Check(Directory.Exists(Old), "source remains");
                Check(Backups().Length == 1, "backup complete before failed deletion");
                AssetDatabase.FailDelete = false;
                BuildResourcePreparation.Prepare();
                Check(!Directory.Exists(Old), "retry successful");
            }
        );
        Case(
            "file-directory conflict retains originals",
            () =>
            {
                Both();
                Put(Old + "/collision/nested.bytes", "nested");
                Put(Current + "/collision", "file");
                Throws(BuildResourcePreparation.Prepare, "folder/file conflict");
                Check(Read(Old + "/collision/nested.bytes") == "nested", "old collision preserved");
                Check(Read(Current + "/collision") == "file", "current collision preserved");
                Check(Backups().Length == 1, "collision backup complete");
            }
        );
        Case(
            "missing maps still fail and permit later retry",
            () =>
            {
                Both();
                string map = "Assets/Rubezh/Resources/War/war-50.bytes";
                File.Delete(map);
                Throws(BuildResourcePreparation.Prepare, "Missing war map: 50");
                Put(map, "map");
                BuildResourcePreparation.Prepare();
                Check(!Directory.Exists(Old), "normal validation preserved, retry safe");
            }
        );
        Case(
            "editor startup notices packed-only legacy folder",
            () =>
            {
                Both();
                typeof(BuildResourcePreparation)
                    .GetMethod("ScheduleMigration", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, null);
                EditorApplication.delayCall();
                EditorApplication.delayCall = null;
                Check(!Directory.Exists(Old), "scheduled migration runs");
            }
        );
        foreach (string name in new[] { "SpritePacks", "UnityTestForest", "Exterior", "BuildingMaterials" })
            Case(
                "overlay " + name,
                () =>
                {
                    string old = "Assets/Rubezh/Resources/" + name,
                        active = "Assets/Rubezh/Editor/Archive/" + name;
                    Put(old + "/same.bytes", "same");
                    Put(active + "/same.bytes", "same");
                    Put(old + "/unique.bytes", "unique");
                    Put(old + "/unique.bytes.meta", "guid: unique");
                    BuildResourcePreparation.MigrateSourceFolder(old, active);
                    BuildResourcePreparation.MigrateSourceFolder(old, active);
                    Check(!Directory.Exists(old), "legacy removed");
                    Check(Read(active + "/unique.bytes") == "unique", "unique saved");
                    Check(Read(active + "/unique.bytes.meta") == "guid: unique", "GUID saved");
                }
            );
        Console.WriteLine(
            "PASS "
                + scenarios
                + " scenarios; "
                + assertions
                + " assertions. Real temporary files, production Prepare(); Unity AssetDatabase is a file-backed contract."
        );
    }
}
