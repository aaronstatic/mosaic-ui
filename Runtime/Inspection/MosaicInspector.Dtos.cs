using System;
using System.Collections.Generic;

namespace Mosaic.UI
{
    /// <summary>
    /// Public, read-only inspection facade over the MosaicUI runtime. Every result is a plain
    /// <see cref="SerializableAttribute"/> class with public fields only, so both
    /// <c>JsonUtility</c> and Newtonsoft serialize it with no converter.
    ///
    /// <para>This file holds the result shapes only. The read methods live in
    /// <c>MosaicInspector.cs</c>.</para>
    ///
    /// <para><b>Main thread only.</b> The facade reads plain collections and takes no lock.</para>
    /// </summary>
    public static partial class MosaicInspector
    {
        [Serializable]
        public class ServiceInfo
        {
            /// <summary>Key <c>Type.FullName</c> the service is registered under.</summary>
            public string typeName;

            /// <summary>Concrete <c>Type.FullName</c> of the registered value.</summary>
            public string implTypeName;

            /// <summary>True when the value implements <c>INotifyBindablePropertyChanged</c>.</summary>
            public bool isStore;
        }

        [Serializable]
        public class ServiceListResult
        {
            public bool initialized;
            public List<ServiceInfo> services = new List<ServiceInfo>();
        }

        [Serializable]
        public class StoreValue
        {
            /// <summary>Member name.</summary>
            public string name;

            /// <summary>CLR type name of the value (declared type when the value is null or the getter throws).</summary>
            public string type;

            /// <summary>JSON-safe text form of the value.</summary>
            public string value;
        }

        [Serializable]
        public class StoreInfo
        {
            public bool initialized;

            /// <summary>False when no service type matched the requested name.</summary>
            public bool found;

            /// <summary>Key <c>Type.FullName</c>.</summary>
            public string typeName;

            /// <summary><c>IDataSourceViewHashProvider.GetViewHashCode()</c>, else 0.</summary>
            public long version;

            public List<StoreValue> values = new List<StoreValue>();
        }

        [Serializable]
        public class StoreListResult
        {
            public bool initialized;
            public List<StoreInfo> stores = new List<StoreInfo>();
        }

        [Serializable]
        public class PanelInfo
        {
            public string panelName;
            public string slotName;
            public int sortOrder;
            public bool isActive;

            /// <summary><c>PanelDefinition.ControllerTypeName</c>, passed through with no validation.</summary>
            public string controllerTypeName;
        }

        [Serializable]
        public class CompositionResult
        {
            public bool initialized;
            public bool hasManager;
            public string currentMode = string.Empty;

            /// <summary>Mode history in stack order, most recent first.</summary>
            public List<string> history = new List<string>();

            public List<string> slots = new List<string>();
            public List<PanelInfo> panels = new List<PanelInfo>();
            public List<string> actionMaps = new List<string>();
            public List<string> worldFeatures = new List<string>();
            public List<string> worldControllers = new List<string>();
        }

        [Serializable]
        public class CommandListResult
        {
            public bool initialized;
            public List<string> commands = new List<string>();
        }

        [Serializable]
        public class EventInfo
        {
            public string typeName;

            /// <summary>Payload <c>ToString()</c>, trimmed to 200 characters.</summary>
            public string summary;

            /// <summary>Monotonically increasing within a session.</summary>
            public long sequence;
        }

        [Serializable]
        public class EventListResult
        {
            public bool initialized;
            public List<EventInfo> events = new List<EventInfo>();
        }
    }
}
