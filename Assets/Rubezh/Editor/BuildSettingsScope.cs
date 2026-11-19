using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    sealed class BuildSettingsScope : IDisposable
    {
        readonly Action restore;

        public BuildSettingsScope()
        {
            var target = NamedBuildTarget.Android;
            string productName = PlayerSettings.productName;
            string companyName = PlayerSettings.companyName;
            string bundleVersion = PlayerSettings.bundleVersion;
            string applicationId = PlayerSettings.GetApplicationIdentifier(target);
            int versionCode = PlayerSettings.Android.bundleVersionCode;
            var backend = PlayerSettings.GetScriptingBackend(target);
            var architectures = PlayerSettings.Android.targetArchitectures;
            var minimumSdk = PlayerSettings.Android.minSdkVersion;
            var orientation = PlayerSettings.defaultInterfaceOrientation;
            bool runInBackground = PlayerSettings.runInBackground;
            bool stripEngineCode = PlayerSettings.stripEngineCode;
            var strippingLevel = PlayerSettings.GetManagedStrippingLevel(target);
            bool showSplash = PlayerSettings.SplashScreen.show;
            bool showUnityLogo = PlayerSettings.SplashScreen.showUnityLogo;
            bool buildAppBundle = EditorUserBuildSettings.buildAppBundle;
            bool exportAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            bool useCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string keystoreName = PlayerSettings.Android.keystoreName;
            string keystorePassword = PlayerSettings.Android.keystorePass;
            string keyAlias = PlayerSettings.Android.keyaliasName;
            string keyPassword = PlayerSettings.Android.keyaliasPass;
            restore = () =>
            {
                PlayerSettings.productName = productName;
                PlayerSettings.companyName = companyName;
                PlayerSettings.bundleVersion = bundleVersion;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                PlayerSettings.SetApplicationIdentifier(target, applicationId);
                PlayerSettings.SetScriptingBackend(target, backend);
                PlayerSettings.Android.targetArchitectures = architectures;
                PlayerSettings.Android.minSdkVersion = minimumSdk;
                PlayerSettings.defaultInterfaceOrientation = orientation;
                PlayerSettings.runInBackground = runInBackground;
                PlayerSettings.stripEngineCode = stripEngineCode;
                PlayerSettings.SetManagedStrippingLevel(target, strippingLevel);
                PlayerSettings.SplashScreen.show = showSplash;
                PlayerSettings.SplashScreen.showUnityLogo = showUnityLogo;
                EditorUserBuildSettings.buildAppBundle = buildAppBundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = exportAndroidProject;
                PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
                PlayerSettings.Android.keystoreName = keystoreName;
                PlayerSettings.Android.keystorePass = keystorePassword;
                PlayerSettings.Android.keyaliasName = keyAlias;
                PlayerSettings.Android.keyaliasPass = keyPassword;
            };
        }

        public void Dispose()
        {
            restore();
        }
    }
}
