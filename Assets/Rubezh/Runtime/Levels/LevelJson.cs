using System.Text.RegularExpressions;

namespace Rubezh.UnityPort
{
    // Portable map files use x/y; Unity serializes Vector2Int using m_X/m_Y.
    // Restrict translation to scenario points: ordinary object-cell x/y are public fields.
    public static class LevelJson
    {
        static readonly Regex Point = new Regex("\"(?:flag|playerSpawn|botSpawn|position)\"\\s*:\\s*\\{[^{}]*\\}");
        public static string ToUnity(string json)
        {
            return Point.Replace(json, m => m.Value.Replace("\"x\"", "\"m_X\"").Replace("\"y\"", "\"m_Y\""));
        }

        public static string ToPortable(string json)
        {
            return Point.Replace(json, m => m.Value.Replace("\"m_X\"", "\"x\"").Replace("\"m_Y\"", "\"y\""));
        }
    }
}
