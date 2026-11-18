// API contract test double. No Unity import, packing, GPU or bundle I/O is executed.
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine.U2D {
 public class SpriteAtlas:UnityEngine.Object {public Dictionary<string,Sprite> Items=new Dictionary<string,Sprite>();public Sprite GetSprite(string name)=>Items.TryGetValue(name,out var s)?s:null;}
}

namespace UnityEngine.ResourceManagement.AsyncOperations {
 public enum AsyncOperationStatus {None,Succeeded,Failed}
 public sealed class State {public object Result;public Exception Error;public bool Done=true,Valid=true;public AsyncOperationStatus Status=AsyncOperationStatus.Succeeded;}
 public struct AsyncOperationHandle {public State State;public bool IsValid()=>State!=null&&State.Valid;}
 public struct AsyncOperationHandle<T> {public State State;public bool IsDone=>State.Done;public T Result=>(T)State.Result;public Exception OperationException=>State.Error;public AsyncOperationStatus Status=>State.Status;public bool IsValid()=>State!=null&&State.Valid;public static implicit operator AsyncOperationHandle(AsyncOperationHandle<T> h)=>new AsyncOperationHandle{State=h.State};}
}
namespace UnityEngine.AddressableAssets {
 using UnityEngine.ResourceManagement.AsyncOperations;
 public static class Addressables {
  public static Func<string,State> Loader;public static readonly List<State> Loaded=new List<State>();public static int Releases;
  public static AsyncOperationHandle<T> LoadAssetAsync<T>(string key){var state=Loader(key);Loaded.Add(state);return new AsyncOperationHandle<T>{State=state};}
  public static void Release(AsyncOperationHandle h){if(!h.IsValid())throw new Exception("Double release");h.State.Valid=false;Releases++;}
  public static void Release<T>(AsyncOperationHandle<T> h)=>Release((AsyncOperationHandle)h);
 }
}
