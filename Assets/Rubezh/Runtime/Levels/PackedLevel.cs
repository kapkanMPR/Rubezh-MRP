using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public static class PackedLevel
    {
        // Both legacy JSON and losslessly compressed .bytes use the same Resources key.
        public static string Read(TextAsset asset) => Decode(asset.bytes);

        public static string Decode(byte[] data)
        {
            if (data == null)
                throw new InvalidDataException("Missing level data");
            if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                using (var input = new MemoryStream(data, false))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            return Encoding.UTF8.GetString(data);
        }
    }
}
