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
            var target=NamedBuildTarget.Android;
            string product=PlayerSettings.productName,company=PlayerSettings.companyName,version=PlayerSettings.bundleVersion,id=PlayerSettings.GetApplicationIdentifier(target);
            int code=PlayerSettings.Android.bundleVersionCode;
            var backend=PlayerSettings.GetScriptingBackend(target);var architectures=PlayerSettings.Android.targetArchitectures;var sdk=PlayerSettings.Android.minSdkVersion;
            var orientation=PlayerSettings.defaultInterfaceOrientation;bool background=PlayerSettings.runInBackground,strip=PlayerSettings.stripEngineCode;
            var managed=PlayerSettings.GetManagedStrippingLevel(target);
            bool splash=PlayerSettings.SplashScreen.show,logo=PlayerSettings.SplashScreen.showUnityLogo,bundle=EditorUserBuildSettings.buildAppBundle,export=EditorUserBuildSettings.exportAsGoogleAndroidProject;
            bool custom=PlayerSettings.Android.useCustomKeystore;string store=PlayerSettings.Android.keystoreName,storePass=PlayerSettings.Android.keystorePass,alias=PlayerSettings.Android.keyaliasName,keyPass=PlayerSettings.Android.keyaliasPass;
            restore=()=>{
                PlayerSettings.productName=product;PlayerSettings.companyName=company;PlayerSettings.bundleVersion=version;PlayerSettings.Android.bundleVersionCode=code;
                PlayerSettings.SetApplicationIdentifier(target,id);PlayerSettings.SetScriptingBackend(target,backend);PlayerSettings.Android.targetArchitectures=architectures;PlayerSettings.Android.minSdkVersion=sdk;
                PlayerSettings.defaultInterfaceOrientation=orientation;PlayerSettings.runInBackground=background;PlayerSettings.stripEngineCode=strip;PlayerSettings.SetManagedStrippingLevel(target,managed);
                PlayerSettings.SplashScreen.show=splash;PlayerSettings.SplashScreen.showUnityLogo=logo;EditorUserBuildSettings.buildAppBundle=bundle;EditorUserBuildSettings.exportAsGoogleAndroidProject=export;
                PlayerSettings.Android.useCustomKeystore=custom;PlayerSettings.Android.keystoreName=store;PlayerSettings.Android.keystorePass=storePass;PlayerSettings.Android.keyaliasName=alias;PlayerSettings.Android.keyaliasPass=keyPass;
            };
        }
        public void Dispose(){restore();}
    }
}
