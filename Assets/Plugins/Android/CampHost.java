package com.rubezh.camp;
import android.app.Activity;
import android.webkit.WebView;
import android.webkit.WebViewClient;

/** One-shot reader of the pre-native save. Never displays or runs the old menu.
 * Retains the old origin solely to preserve the existing localStorage namespace.
 * The WebView is destroyed immediately after reading; the C# save is authoritative.
 */
public final class CampHost {
 private static volatile String result;
 public static String migrationResult() { return result; }
 public static void migrate(final Activity activity, final boolean developer) {
  result=null;
  activity.runOnUiThread(()->{
   try {
   if(!new java.io.File(activity.getApplicationInfo().dataDir,"app_webview").exists()){result="\"\"";return;}
   final WebView reader=new WebView(activity);
   reader.getSettings().setJavaScriptEnabled(true);
   reader.getSettings().setDomStorageEnabled(true);
   reader.getSettings().setAllowFileAccess(false);
   reader.getSettings().setAllowContentAccess(false);
   reader.setWebViewClient(new WebViewClient(){
    @Override public void onPageFinished(WebView view,String url){
     String key="rubezh-unity-56-"+(developer?"dev":"release");
     view.evaluateJavascript("(function(){return localStorage.getItem('"+key+"')||'';})()",value->{result=value;reader.destroy();});
    }
   });
   reader.loadDataWithBaseURL("https://camp.rubezh.invalid/","<!doctype html><html><body></body></html>","text/html","UTF-8",null);
   } catch(Throwable failure) { result="null"; }
  });
 }
}
