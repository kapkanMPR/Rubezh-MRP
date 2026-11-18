#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Collections.Generic;
using System.Threading;
namespace Rubezh.UnityPort.Editor
{
    // Streaming metadata scan. Never creates a battle, a Unity object, or decodes embedded PNG.
    public static class TestMapDependencies
    {
        public sealed class Result
        {
            public readonly HashSet<string> assets = new HashSet<string>();
            public readonly HashSet<string> materials = new HashSet<string>();
        }
        public static Result Read(string path, CancellationToken token)
        {
            using (var file = File.OpenRead(path))
            {
                int a = file.ReadByte(), b = file.ReadByte(); file.Position = 0;
                if (a == 31 && b == 139)
                    using (var zip = new GZipStream(file, CompressionMode.Decompress))
                    using (var reader = new StreamReader(zip, Encoding.UTF8, true, 65536)) return Scan(reader, token);
                using (var reader = new StreamReader(file, Encoding.UTF8, true, 65536)) return Scan(reader, token);
            }
        }
        public static Result Scan(TextReader reader, CancellationToken token)
        {
            var result = new Result(); int c, count = 0;
            while ((c = reader.Read()) >= 0)
            {
                if ((++count & 4095) == 0) token.ThrowIfCancellationRequested();
                if (c != '"') continue;
                string name = String(reader, token);
                do { c = reader.Read(); } while (c >= 0 && char.IsWhiteSpace((char)c));
                if (c != ':') continue;
                do { c = reader.Read(); } while (c >= 0 && char.IsWhiteSpace((char)c));
                if (c != '"') continue;
                string value = String(reader, token);
                if (string.IsNullOrEmpty(value)) continue;
                if (name == "assetId") result.assets.Add(value);
                if (name == "material") result.materials.Add(value);
            }
            token.ThrowIfCancellationRequested();
            return result;
        }
        static string String(TextReader reader, CancellationToken token)
        {
            var value = new StringBuilder(64); bool tooLong = false; int count = 0;
            while (true)
            {
                if ((++count & 4095) == 0) token.ThrowIfCancellationRequested();
                int c = reader.Read();
                if (c < 0) throw new InvalidDataException("Незакрытая строка в карте");
                if (c == '"') return tooLong ? null : value.ToString();
                if (c == '\\')
                {
                    c = reader.Read(); if (c < 0) throw new InvalidDataException("Оборванная строка карты");
                    if (c == 'u')
                    {
                        int code = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            int h = reader.Read(), n = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                            if (n < 0) throw new InvalidDataException("Некорректный Unicode в карте");
                            code = code * 16 + n;
                        }
                        c = code;
                    }
                    else if (c == 'n') c = '\n';
                    else if (c == 'r') c = '\r';
                    else if (c == 't') c = '\t';
                }
                if (value.Length < 512) value.Append((char)c); else tooLong = true;
            }
        }
    }
}
#endif
