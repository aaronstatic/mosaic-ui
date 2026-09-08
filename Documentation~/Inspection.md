# Inspection

`MosaicInspector` is a public, read-only view of the MosaicUI runtime. It lives in the `Mosaic.UI` runtime assembly, in the flat `Mosaic.UI` namespace. Every method returns plain serializable data. No method mutates state, and no method throws.

---

## Overview

MosaicUI keeps most of its runtime lists behind `internal`. The editor debugger reads them through `InternalsVisibleTo("Mosaic.UI.Editor")`. A game assembly cannot do that. `MosaicInspector` is the one door that every reader uses.

Three kinds of consumer use the facade:

| Consumer | Use |
|---|---|
| The editor debugger panes | The State pane and the Composition pane build their content from the facade |
| A CLI or an agent that drives the Editor | Reads the live composition, the stores, and the commands as JSON |
| Any assembly that references `Mosaic.UI` | Needs no `InternalsVisibleTo` entry |

The facade only observes. It registers no service, holds no subscription, and starts no timer. To change state, call the public `MosaicUI.Commands.Invoke(id)` instead.

---

## The six methods

| Method | Returns |
|---|---|
| `MosaicInspector.GetServices()` | `ServiceListResult` |
| `MosaicInspector.GetStore(string typeName)` | `StoreInfo` |
| `MosaicInspector.GetStores()` | `StoreListResult` |
| `MosaicInspector.GetComposition()` | `CompositionResult` |
| `MosaicInspector.GetCommands()` | `CommandListResult` |
| `MosaicInspector.GetRecentEvents(int max)` | `EventListResult` |

**`GetServices()`** lists every entry in `MosaicUI.Services`. Each entry carries the key type name, the concrete implementation type name, and an `isStore` flag.

**`GetStore(string typeName)`** returns one store by name. The lookup tries three rules in order: an exact `Type.FullName`, an exact `Type.Name`, then a case-insensitive `Type.Name`. A name that matches nothing gives `found = false`. A null name and an empty name behave the same way.

**`GetStores()`** returns one `StoreInfo` for every registered store. A store is a service value that implements `INotifyBindablePropertyChanged`. A plain service never appears in this list.

**`GetComposition()`** reads `MosaicUIManager.Instance`. It reports the current mode, the mode history, the layout slots, the active panels, the active action maps, the world features, and the world controllers.

**`GetCommands()`** copies `MosaicUI.Commands.RegisteredIds`.

**`GetRecentEvents(int max)`** returns the newest `max` recorded events, oldest first. Read the [event buffer](#the-editor-only-event-buffer) section for the limits.

---

## Result shapes

Every result class is a nested public `[Serializable]` class inside `MosaicInspector`. Every field is a public field. There is no property, no dictionary, and no `object` field. Every list field is initialized at its declaration, so a list is never null.

Both `JsonUtility` and Newtonsoft therefore serialize a result with no converter.

```csharp
[Serializable] public class ServiceInfo
{
    public string typeName;       // key Type.FullName
    public string implTypeName;   // concrete Type.FullName of the value
    public bool   isStore;        // value implements INotifyBindablePropertyChanged
}

[Serializable] public class ServiceListResult
{
    public bool initialized;
    public List<ServiceInfo> services = new List<ServiceInfo>();
}
```

```csharp
[Serializable] public class StoreValue
{
    public string name;    // member name
    public string type;    // CLR type name of the value
    public string value;   // JSON-safe text form
}

[Serializable] public class StoreInfo
{
    public bool initialized;
    public bool found;        // false when no service type matched the name
    public string typeName;   // key Type.FullName
    public long version;      // IDataSourceViewHashProvider.GetViewHashCode(), else 0
    public List<StoreValue> values = new List<StoreValue>();
}

[Serializable] public class StoreListResult
{
    public bool initialized;
    public List<StoreInfo> stores = new List<StoreInfo>();
}
```

```csharp
[Serializable] public class PanelInfo
{
    public string panelName;
    public string slotName;
    public int    sortOrder;
    public bool   isActive;
    public string controllerTypeName;   // PanelDefinition.ControllerTypeName, passed through
}

[Serializable] public class CompositionResult
{
    public bool initialized;
    public bool hasManager;
    public string currentMode = string.Empty;
    public List<string>    history          = new List<string>();  // most recent first
    public List<string>    slots            = new List<string>();
    public List<PanelInfo> panels           = new List<PanelInfo>();
    public List<string>    actionMaps       = new List<string>();
    public List<string>    worldFeatures    = new List<string>();
    public List<string>    worldControllers = new List<string>();
}
```

```csharp
[Serializable] public class CommandListResult
{
    public bool initialized;
    public List<string> commands = new List<string>();
}

[Serializable] public class EventInfo
{
    public string typeName;
    public string summary;    // payload ToString(), trimmed to 200 characters
    public long   sequence;   // monotonically increasing within a session
}

[Serializable] public class EventListResult
{
    public bool initialized;
    public List<EventInfo> events = new List<EventInfo>();
}
```

`controllerTypeName` is the raw string from the `PanelDefinition`. The facade does not resolve it and does not validate it. A broken controller binding therefore shows in the output as the configured text, which is the useful diagnostic.

---

## Value formatting

`StoreValue.value` is always a string that holds the JSON text form of the value. `StoreValue.type` carries the CLR type name beside it. The type is the runtime type of the value. When the value is null, or when the getter throws, the type is the declared member type.

| Input value | `value` field | Example |
|---|---|---|
| `null` | `null` | `null` |
| `bool` | `true` or `false` | `true` |
| numeric primitive | invariant-culture text | `42`, `3.5` |
| `char` | the character | `A` |
| `string` | the text, unchanged | `hello world` |
| enum | the member name | `Idle` |
| `Vector2`, `Vector3`, `Vector4`, `Quaternion` | float array text | `[1, 2, 3]` |
| `float2`, `float3`, `float4` | float array text | `[1, 2, 3]` |
| array or other `IEnumerable`, excluding `string` | `{count: N, items: [first 20]}` | `{count: 5, items: [1, 2, 3, 4, 5]}` |
| any other object | `TypeName: ToString()`, trimmed to 200 characters | `PlayerData: Player(3)` |
| a getter that throws | `<error: ExceptionTypeName>` | `<error: NullReferenceException>` |

Each float component uses `CultureInfo.InvariantCulture`. A collection item uses the scalar rules only. A collection inside `items` therefore renders through the last rule, which bounds the depth at one level. The `count` field always reports the true item count, even when `items` shows the first 20 only.

A type name renders in friendly form. A `List<int>` member reports the type `List<Int32>`, not ``List`1``.

The facade reads every member that carries `[CreateProperty]`, on the store type and on its base types. The walk runs once per type and caches its result for the rest of the domain.

---

## The guard contract

No method throws. A failure is a flag on the result, never an exception. The consumer is a polling agent, and an exception would break its transport for a condition that is normal.

| Flag | Meaning |
|---|---|
| `initialized = false` | `MosaicUI.IsInitialized` is false, or `MosaicUI.Services` is null |
| `hasManager = false` | No `MosaicUIManager.Instance` exists, or the manager is destroyed |
| `found = false` | No registered service type matched the requested name |

Each method builds its result before the first guard, so every list is non-null on every return path. An `initialized = false` result carries empty lists.

Each independent collection sits inside its own `try` block. A read that fails during play exit therefore loses that one collection only. The result keeps the rest, and the method returns the partial data.

No catch logs, and no catch rethrows. A read facade must not fill the console while the Editor leaves play mode.

---

## Ordering guarantees

A CLI diffs its own output between two polls. The order of every list is therefore stable.

| Result list | Order |
|---|---|
| `services`, `stores` | ordinal ascending by `typeName` |
| `panels` | ordinal ascending by `panelName` |
| `slots`, `commands`, `actionMaps`, `worldFeatures`, `worldControllers` | ordinal ascending |
| `values` | declaration order, base types first |
| `history` | stack order, most recent first |
| `events` | ascending `sequence`, oldest first |

The history order carries the meaning of the back stack, so the facade never sorts it. Two calls with no state change between them produce identical JSON.

---

## The editor-only event buffer

`GetRecentEvents` reads a ring buffer that holds the last **64** published events. The buffer, its hook, and the recorder behind it all live under `#if UNITY_EDITOR`.

- `MosaicUI.Initialize()` attaches the recorder to the `EventBus.Published` hook.
- The sequence starts at **1** for each `Initialize`. `MosaicUI.Shutdown()` clears the buffer and resets the sequence.
- The recorder stores the formatted summary only. It never keeps a reference to the payload, so it leaks no consumer state.
- A summary is the payload `ToString()`, trimmed to 200 characters. A null payload records `null`.
- A `max` of 0 or less returns an empty list. A `max` above the stored count returns everything stored.
- After 100 publishes the buffer holds 64 entries. The oldest is publish 37.

In a **player build** the whole recorder compiles to nothing. `GetRecentEvents` there returns `initialized = true` with an empty list. That is the documented behavior, not an error. Every other method works normally in a player build.

The debugger Events pane keeps its own larger buffer. The 64-entry buffer serves the CLI only.

---

## MosaicUIManager.Instance

```csharp
public static MosaicUIManager Instance { get; internal set; }
```

`Awake` assigns the static. `OnDestroy` clears it, but only when it still points at the manager that goes away. A destroyed duplicate therefore cannot null a live registration.

The last manager to awake wins. A second manager logs one `[MosaicUI]` warning and then takes the slot.

`GetComposition()` reads `Instance` through the Unity null check, so a destroyed manager reports as absent.

The setter is `internal` as a test seam. Unity does not call `Awake` outside play mode, so an EditMode test assigns the instance directly.

> **`Awake` runs before `Start`.** `Start` builds the layout and enters the first mode. An early `GetComposition()` call can therefore see a manager with an empty mode, no slots, and no panels. That result is correct. A CLI that needs a built composition polls until `currentMode` is not empty.

---

## Main thread only

Call every method from the Unity main thread. The member cache and the event buffer are plain collections, and no method takes a lock. A call from a background thread is not supported.

---

## Type name collisions

Two types in different assemblies can share a `Type.FullName`. `GetStore(fullName)` walks the sorted service snapshot and takes the first exact match. The answer is therefore stable across calls, but it can be the wrong store.

Pass a name that is unique in your registry. Read `StoreInfo.typeName` on the result to confirm the store you got.

---

## Usage

### From a consumer assembly

Reference the `Mosaic.UI` assembly and call the facade directly. No `InternalsVisibleTo` entry is needed.

```csharp
using Mosaic.UI;
using UnityEngine;

public class CompositionDump : MonoBehaviour
{
    [ContextMenu("Dump composition")]
    private void Dump()
    {
        MosaicInspector.CompositionResult composition = MosaicInspector.GetComposition();

        if (!composition.initialized)
        {
            Debug.Log("MosaicUI is not initialized.");
            return;
        }

        if (!composition.hasManager)
        {
            Debug.Log("No MosaicUIManager is in the scene.");
            return;
        }

        Debug.Log(JsonUtility.ToJson(composition, true));
    }
}
```

### From the Unity CLI

Reach the facade by name and use public members only, so the snippet needs no assembly reference. Save the body to a file, convert the path with `wslpath -w`, and run it.

```csharp
var t = System.Type.GetType("Mosaic.UI.MosaicInspector, Mosaic.UI");
var composition = t.GetMethod("GetComposition").Invoke(null, null);
var stores      = t.GetMethod("GetStores").Invoke(null, null);
var commands    = t.GetMethod("GetCommands").Invoke(null, null);
var events      = t.GetMethod("GetRecentEvents").Invoke(null, new object[] { 10 });
var store       = t.GetMethod("GetStore").Invoke(null, new object[] { "MapCameraStore" });
return UnityEngine.JsonUtility.ToJson(composition, true) + "\n"
     + UnityEngine.JsonUtility.ToJson(stores, true)      + "\n"
     + UnityEngine.JsonUtility.ToJson(commands, true)    + "\n"
     + UnityEngine.JsonUtility.ToJson(events, true)      + "\n"
     + UnityEngine.JsonUtility.ToJson(store, true);
```

```bash
tools/unity command eval_file --file "$(wslpath -w /tmp/inspect.cs)"
```

The result arrives at `data.result.result` in the response envelope.
