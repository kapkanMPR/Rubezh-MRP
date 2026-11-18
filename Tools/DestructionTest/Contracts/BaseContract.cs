using System;
namespace UnityEngine {
public class TextAreaAttribute:Attribute {public TextAreaAttribute(int minLines,int maxLines){} }
public enum TouchPhase{Began,Moved,Stationary,Ended,Canceled}
public struct Touch{public int fingerId;public Vector2 position;public TouchPhase phase;}
public static class Input{public static bool GetKeyDown(KeyCode k)=>false;public static Touch[] samples=new Touch[0];public static int touchCount=>samples.Length;public static Touch GetTouch(int i)=>samples[i];}
public static class GUIUtility{public static string systemCopyBuffer;public static Vector2 GUIToScreenPoint(Vector2 p)=>new Vector2(GUI.matrix.tx+(p.x+GUI.offset.x)*GUI.matrix.sx,GUI.matrix.ty+(p.y+GUI.offset.y)*GUI.matrix.sy);}
public class Object {public string name;public static T FindFirstObjectByType<T>() where T:new()=>default(T);public static void Destroy(Object o){} public static void DestroyImmediate(Object o){} }
public class Transform {public Quaternion rotation;public Vector3 position;public void SetParent(Transform t){} public void SetParent(Transform t,bool worldPositionStays){} }
public class MonoBehaviour:Object {public bool enabled;public GameObject gameObject=new GameObject("stub");public System.Collections.IEnumerator routine;public void StartCoroutine(System.Collections.IEnumerator e){routine=e;}public Transform transform=new Transform();}
public class GameObject:Object {public void SetActive(bool value){}public Transform transform=new Transform();public int layer;public GameObject(string s){} public T AddComponent<T>()where T:new()=>new T();public T GetComponent<T>()where T:new()=>new T();}
public class Camera:MonoBehaviour {public int cullingMask;public CameraClearFlags clearFlags;public Color backgroundColor;public bool enabled,orthographic;public float orthographicSize,aspect,nearClipPlane,farClipPlane;public RenderTexture targetTexture;public void Render(){} }
public enum CameraClearFlags{SolidColor}public enum TextureFormat{RGBA32,RFloat}public enum FilterMode{Point,Bilinear}public enum TextureWrapMode{Clamp,Repeat}public enum ScreenOrientation{LandscapeLeft}public enum FontStyle{Normal,Bold}public enum TextAnchor{MiddleCenter,MiddleLeft}public enum RuntimeInitializeLoadType{AfterSceneLoad,BeforeSceneLoad}
public class RuntimeInitializeOnLoadMethodAttribute:Attribute {public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType x){} }
public class Texture:Object {public int width,height;}public class Texture2D:Texture {public Texture2D(int w,int h){width=w;height=h;}public Color32[] GetPixels32()=>throw new NotSupportedException("PNG rendering not part of simulation test");public byte[] EncodeToPNG()=>new byte[0];public Texture2D(int w,int h,TextureFormat f,bool m,bool linear=false){width=w;height=h;}public static Texture2D whiteTexture=new Texture2D(1,1,0,false);public FilterMode filterMode;public TextureWrapMode wrapMode;public void SetPixels(Color[] c){}public void ReadPixels(Rect r,int x,int y,bool recalculate=true){}public Color GetPixel(int x,int y)=>Color.white;public void Apply(bool a=false,bool b=false){}public void LoadRawTextureData(byte[] b){} public void SetPixelData<T>(T[] b,int mipLevel) where T:struct {} }
public class TextAsset:Object {public byte[] bytes;public string text;}
public enum RenderTextureFormat{ARGB32}
public enum RenderTextureReadWrite{Linear}
public class RenderTexture:Texture {public static RenderTexture active;bool created;public TextureWrapMode wrapMode;public RenderTexture(int w,int h,int d,RenderTextureFormat f,RenderTextureReadWrite rw=RenderTextureReadWrite.Linear){width=w;height=h;}public FilterMode filterMode;public int antiAliasing;public bool Create(){created=true;return true;}public bool IsCreated()=>created;public void Release(){created=false;}}
public class Shader:Object {public static int PropertyToID(string name)=>name.GetHashCode();public bool isSupported=true;public static Shader Find(string name)=>new Shader();}
public enum HideFlags {HideAndDontSave}
public class Material:Object {public HideFlags hideFlags;public void SetFloat(int key,float value){}public void SetTexture(int key,Texture tex){}public Texture mainTexture;public int renderQueue;public void SetFloat(string key,float value){}public Shader shader;public Material(Shader s){shader=s;} public Color color;public static bool PassWorks=true;public bool SetPass(int pass)=>PassWorks;public void SetTexture(string key,Texture tex){}public void SetVector(string key,Vector4 v){} public void SetVectorArray(string key,Vector4[] v){}public void SetMatrix(string key,Matrix4x4 m){}}
public struct Color32{public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}}
public class Mesh:Object {public int subMeshCount;public void SetTriangles(System.Collections.Generic.List<int> indexes,int start,int length,int submesh,bool calculateBounds=true,int baseVertex=0){}public Color32[] colors32;public Vector3[] vertices;public Vector2[] uv;public int[] triangles;public string name;public UnityEngine.Rendering.IndexFormat indexFormat;public void MarkDynamic(){}public void Clear(){}public void SetVertices(System.Collections.Generic.List<Vector3> v){}public void SetUVs(int channel,System.Collections.Generic.List<Vector2> uv){}public void SetColors(System.Collections.Generic.List<Color32> c){}public void SetTriangles(System.Collections.Generic.List<int> indexes,int submesh,bool calculateBounds=true){}public void RecalculateBounds(){}}
public class Sprite:Object {public static Sprite Create(Texture2D tex,Rect bounds,Vector2 pivot,float pixelsPerUnit)=>new Sprite();}
public class SpriteRenderer:MonoBehaviour {public Sprite sprite;public int sortingOrder;}
public class MeshFilter:MonoBehaviour {public Mesh sharedMesh;}
public class MeshRenderer:MonoBehaviour {public Material sharedMaterial;public int sortingOrder;}
public class AudioClip:Object {public float length=180;
 public static AudioClip Create(string name,int count,int channels,int rate,bool stream)=>new AudioClip();
 public void SetData(float[] samples,int offset){}
}
public class AudioSource:MonoBehaviour {
 public bool playOnAwake,loop,isPlaying;public float spatialBlend,volume,time,pitch=1;public AudioClip clip;public void Play(){isPlaying=true;}public void Pause(){isPlaying=false;}public void UnPause(){isPlaying=true;}
 public readonly System.Collections.Generic.List<AudioClip> Played=new System.Collections.Generic.List<AudioClip>();
 public void PlayOneShot(AudioClip clip,float volume=1f){Played.Add(clip);}
 public void Stop(){isPlaying=false;}
}

public class Font:Object {}
public class AudioListener:MonoBehaviour {}
public static class PlayerPrefs {public static float GetFloat(string k,float v)=>v;public static void SetFloat(string k,float v){}static System.Collections.Generic.Dictionary<string,string> store=new System.Collections.Generic.Dictionary<string,string>(); public static bool HasKey(string k)=>store.ContainsKey(k); public static string GetString(string k,string d="")=>store.TryGetValue(k,out var v)?v:d;public static void SetString(string k,string v)=>store[k]=v;public static void DeleteKey(string k)=>store.Remove(k);public static int GetInt(string key,int value=0)=>int.TryParse(GetString(key),out var n)?n:value;public static void SetInt(string key,int value)=>SetString(key,value.ToString());public static void Save(){} }
public class ResourceRequest {public bool isDone=true;public float progress=1;public Object asset;}
public static class Debug {public static void Log(object value){}public static void LogError(object error){}public static void LogException(Exception e){}public static void LogWarning(object value){}}
public static class Resources {public static Action<Object> OnUnload;public static void UnloadAsset(Object o){OnUnload?.Invoke(o);}public static Object[] All=new Object[0];public static T[] LoadAll<T>(string path){return System.Array.ConvertAll(All,x=>(T)(object)x);}public static ResourceRequest LoadAsync<T>(string p)=>new ResourceRequest{asset=Loader(p)};public static System.Func<string,Object> Loader;public static T Load<T>(string s)=>(T)(object)Loader(s);public static T GetBuiltinResource<T>(string s)where T:new()=>new T();}
public static class Application {public static string dataPath="Assets",unityVersion="contract-only";public static void OpenURL(string url){}public static int targetFrameRate;public static bool runInBackground;public static string streamingAssetsPath="Assets/StreamingAssets";public static string persistentDataPath="/tmp";}
public static class Screen {public static ScreenOrientation orientation;public static int width=2200;public static int height=1000;public static Rect safeArea=new Rect(0,0,2200,1000);}
public static class Time {public static int frameCount;public static float unscaledTime,unscaledDeltaTime,realtimeSinceStartup;}
public static class JsonUtility {public static void FromJsonOverwrite(string s,object target){var value=System.Text.Json.JsonSerializer.Deserialize(s,target.GetType(),new System.Text.Json.JsonSerializerOptions{IncludeFields=true});foreach(var f in target.GetType().GetFields())f.SetValue(target,f.GetValue(value));}public static T FromJson<T>(string s)=>System.Text.Json.JsonSerializer.Deserialize<T>(s,new System.Text.Json.JsonSerializerOptions{IncludeFields=true});public static string ToJson(object o,bool pretty=false)=>System.Text.Json.JsonSerializer.Serialize(o,new System.Text.Json.JsonSerializerOptions{IncludeFields=true});}
public struct Vector2 {public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public static Vector2 zero=>new Vector2();public float magnitude=>(float)Math.Sqrt(x*x+y*y);public static Vector2 operator +(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);public static Vector2 operator -(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y);public static float Distance(Vector2 a,Vector2 b)=>(a-b).magnitude;}
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
public struct Quaternion {public static Quaternion identity=>new Quaternion();public static Quaternion Euler(float x,float y,float z)=>new Quaternion();}
public struct Matrix4x4 {public float tx,ty,sx,sy;public static Matrix4x4 Ortho(float l,float r,float b,float t,float n,float f)=>identity;public static Matrix4x4 identity=>new Matrix4x4{sx=1,sy=1};public static Matrix4x4 TRS(Vector3 a,Quaternion b,Vector3 c)=>new Matrix4x4{tx=a.x,ty=a.y,sx=c.x,sy=c.y};}
public struct Color {public static Color white=>new Color(1,1,1,1);public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color clear=>new Color(0,0,0,0);public static Color operator *(Color a,float f)=>a;}
public struct Rect {public float xMin=>x;public float yMin=>y;public Vector2 center=>new Vector2(x+width/2,y+height/2);public static bool operator ==(Rect a,Rect b)=>a.Equals(b);public static bool operator !=(Rect a,Rect b)=>!a.Equals(b);public float x,y,width,height;public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}public float xMax=>x+width;public float yMax=>y+height;public bool Contains(Vector2 p)=>p.x>=x&&p.x<xMax&&p.y>=y&&p.y<yMax;public bool Overlaps(Rect r)=>x<r.xMax&&xMax>r.x&&y<r.yMax&&yMax>r.y;}
public class RectOffset {public RectOffset(int a,int b,int c,int d){} }
public class GUIStyleState {public Color textColor;public Texture2D background;}
public class GUIStyle {public GUIStyle(){}public GUIStyle(GUIStyle s){}public int fontSize;public bool wordWrap;public FontStyle fontStyle;public TextAnchor alignment;public RectOffset border,padding;public float fixedWidth;public GUIStyleState normal=new GUIStyleState(),onNormal=new GUIStyleState(),focused=new GUIStyleState(),onFocused=new GUIStyleState(),hover=new GUIStyleState(),active=new GUIStyleState(),onHover=new GUIStyleState(),onActive=new GUIStyleState();public float CalcHeight(GUIContent c,float w)=>Math.Max(1,(float)Math.Ceiling(c.text.Length*Math.Max(10,fontSize*.55)/Math.Max(1,w)))*Math.Max(25,fontSize*1.25f);public Vector2 CalcSize(GUIContent c)=>new Vector2(c.text.Length*13,30);}
public class GUIContent {public string text;public GUIContent(string s){text=s;}public static GUIContent none=new GUIContent("");}
public class GUISkin {public Font font;public GUIStyle label=new GUIStyle(),button=new GUIStyle(),verticalScrollbar=new GUIStyle(),verticalScrollbarThumb=new GUIStyle();}
public static class GUI { public static void DrawTexture(Rect r,Texture t,ScaleMode s,bool alpha){}public static System.Action<string> LabelObserver;public static GUISkin skin=new GUISkin();public static Color color;public static bool enabled=true;public static Matrix4x4 matrix=Matrix4x4.identity;public static Vector2 offset;static System.Collections.Generic.Stack<Vector2> offsets=new System.Collections.Generic.Stack<Vector2>();public static void DrawTexture(Rect r,Texture t){}public static void DrawTextureWithTexCoords(Rect r,Texture t,Rect uv,bool alpha=true){}public static void Label(Rect r,string s,GUIStyle st){if(LabelObserver!=null)LabelObserver(s);if(r.width<0||r.height<0)throw new Exception("Negative label: "+s);}public static void Box(Rect r,GUIContent c,GUIStyle s){}public static Func<string,bool> TestClick;public static bool Button(Rect r,string s,GUIStyle st){if(r.width<0||r.height<0)throw new Exception("Negative button: "+s);return TestClick?.Invoke(s)??false;}public static Vector2 BeginScrollView(Rect r,Vector2 v,Rect content){offsets.Push(offset);offset+=new Vector2(r.x-v.x,r.y-v.y);return v;}public static void EndScrollView(){offset=offsets.Pop();}public static void BeginGroup(Rect r){offsets.Push(offset);offset+=new Vector2(r.x,r.y);}public static void EndGroup(){offset=offsets.Pop();} }
public static class Mathf { public static float Floor(float v)=>(float)Math.Floor(v);public static int FloorToInt(float f)=>(int)Math.Floor(f);public static float Pow(float a,float b)=>(float)Math.Pow(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public const float PI=(float)Math.PI;public const float Deg2Rad=(float)Math.PI/180f;public static float Atan2(float y,float x)=>(float)Math.Atan2(y,x);public static float Cos(float v)=>(float)Math.Cos(v);public static float MoveTowards(float a,float b,float d)=>Math.Abs(b-a)<=d?b:a+Math.Sign(b-a)*d;public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static int Min(int a,int b)=>Math.Min(a,b);public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v));public static int Clamp(int v,int a,int b)=>Math.Max(a,Math.Min(b,v));public static float Clamp01(float v)=>Clamp(v,0,1);public static int RoundToInt(float v)=>(int)Math.Round(v);public static float Ceil(float v)=>(float)Math.Ceiling(v);public static float Sqrt(float v)=>(float)Math.Sqrt(v);public static int CeilToInt(float v)=>(int)Math.Ceiling(v);public static float Sin(float v)=>(float)Math.Sin(v);}
public enum EventType {Repaint,MouseDown,MouseUp,MouseDrag,ScrollWheel}public class Event {public static Event current=new Event();public EventType type;public bool isMouse;public int button;public Vector2 mousePosition,delta;public void Use(){} }
}

namespace UnityEngine.Rendering{public enum IndexFormat{UInt32}}

namespace UnityEngine {
 public struct Vector4 {public Vector4(float x,float y,float z,float w){}}
 public static class SystemInfo {public static int maxTextureSize=8192;public static bool SupportsTextureFormat(TextureFormat f)=>true;}
 public static class Graphics {public static void Blit(Texture src,RenderTexture dst,Material mat){}public static void Blit(Texture src,RenderTexture dst,Material mat,int pass){}public static void DrawMeshNow(Mesh mesh,Matrix4x4 matrix){}public static void ExecuteCommandBuffer(UnityEngine.Rendering.CommandBuffer c){if(!Material.PassWorks)throw new InvalidOperationException("Simulated command buffer failure");}}
 public static class GL {public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 m,bool rt)=>m;public const int QUADS=7;public static int stack;public static void PushMatrix(){stack++;}public static void PopMatrix(){stack--;}public static void LoadPixelMatrix(float a,float b,float c,float d){}public static void Clear(bool depth,bool color,Color value){}public static void Begin(int mode){}public static void End(){}public static void TexCoord2(float x,float y){}public static void Vertex3(float x,float y,float z){}}
}

namespace UnityEngine {
 public class ScriptableObject:Object {public static T CreateInstance<T>() where T:ScriptableObject,new()=>new T();}
 public sealed class CreateAssetMenuAttribute:System.Attribute {public string menuName,fileName;}
 public sealed class HeaderAttribute:System.Attribute {public HeaderAttribute(string title){}}
 public sealed class SerializeField:System.Attribute {}
 public struct Vector2Int {
  public int x,y;public Vector2Int(int x,int y){this.x=x;this.y=y;}
  public static bool operator ==(Vector2Int a,Vector2Int b)=>a.x==b.x&&a.y==b.y;
  public static bool operator !=(Vector2Int a,Vector2Int b)=>!(a==b);
  public override bool Equals(object o)=>o is Vector2Int && this==(Vector2Int)o;
  public override int GetHashCode()=>x*397^y;
 }
}

namespace UnityEngine.Rendering {public sealed class CommandBuffer {public string name;public void Clear(){}public void Release(){}public void SetRenderTarget(UnityEngine.RenderTexture t){}public void SetViewport(UnityEngine.Rect r){}public void DisableScissorRect(){}public void ClearRenderTarget(bool d,bool c,UnityEngine.Color color){}public void DrawMesh(UnityEngine.Mesh m,UnityEngine.Matrix4x4 matrix,UnityEngine.Material material,int submesh,int pass){}}}

namespace UnityEngine {public static class ImageConversion {public static bool LoadImage(Texture2D t,byte[] b)=>throw new System.NotSupportedException("PNG decoding intentionally excluded");}}
