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
            foreach (string candidatePath in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidatePath))
                    continue;
                try
                {
                    return CampModel.Decode(File.ReadAllText(candidatePath));
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
            string directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            byte[] bytes = new UTF8Encoding(false).GetBytes(Model.Serialize());
            string temporaryPath = Path + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(Path))
                File.Replace(temporaryPath, Path, Path + ".bak");
            else
                File.Move(temporaryPath, Path);
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
            Change(() => Model.Restore(candidate));
        }
    }
}
