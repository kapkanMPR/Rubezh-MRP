using System;
using System.IO;
using System.Text;

namespace Rubezh.Camp
{
    // Save before publishing state to UI/gameplay. On disk failure restore the previous
    // snapshot; an uncommitted purchase or reward must never appear as successful.
    public sealed class CampStore
    {
        public readonly string Path;
        public readonly CampModel Model;
        public CampStore(string path, CampModel model)
        {
            Path = path;
            Model = model;
        }

        public static State Read(string path)
        {
            Exception error = null;
            foreach (string p in new[]
            {
                path,
                path + ".bak"
            }

            )
            {
                if (!File.Exists(p))
                    continue;
                try
                {
                    return CampModel.Decode(File.ReadAllText(p));
                }
                catch (Exception e)
                {
                    error = e;
                }
            }

            if (error != null)
                throw new IOException("Не удалось прочитать прогресс. Исходные файлы сохранены.", error);
            return null;
        }

        public void Save()
        {
            Model.Validate();
            string dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            byte[] bytes = new UTF8Encoding(false).GetBytes(Model.Serialize());
            using (var f = new FileStream(Path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(bytes, 0, bytes.Length);
                f.Flush(true);
            }

            if (File.Exists(Path))
                File.Replace(Path + ".tmp", Path, Path + ".bak");
            else
                File.Move(Path + ".tmp", Path);
        }

        public void Change(Action action)
        {
            string before = Model.Serialize();
            try
            {
                action();
                Save();
            }
            catch
            {
                Model.Restore(CampModel.Decode(before));
                throw;
            }
        }

        public void Import(string json)
        {
            var candidate = CampModel.Decode(json);
            new CampModel(Model.Data, candidate); // validate without touching the live save
            string before = Model.Serialize();
            try
            {
                Model.Restore(candidate);
                Save();
            }
            catch
            {
                Model.Restore(CampModel.Decode(before));
                throw;
            }
        }
    }
}
