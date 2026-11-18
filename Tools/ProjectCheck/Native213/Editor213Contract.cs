// Compile-only Editor API declarations; no Unity import/build execution.
using System;
namespace UnityEditor {public class InitializeOnLoadAttribute:Attribute{}public class InitializeOnLoadMethodAttribute:Attribute{}}

namespace UnityEditor {public static class PlayerSettings {public static string bundleVersion;public static class Android {public static int bundleVersionCode;}}}
