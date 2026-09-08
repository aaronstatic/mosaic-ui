using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties;

namespace Mosaic.UI
{
    /// <summary>
    /// Caches the <c>[CreateProperty]</c> member walk per type. A type's member set cannot change
    /// inside a domain, so the cache never invalidates and a domain reload clears it.
    ///
    /// <para><b>Main thread only.</b> The cache is a plain dictionary and takes no lock.</para>
    /// </summary>
    internal static class StoreReflectionCache
    {
        /// <summary>One <c>[CreateProperty]</c> member on a store type.</summary>
        internal sealed class MemberEntry
        {
            /// <summary>Member name.</summary>
            internal string Name;

            /// <summary>The property or field itself.</summary>
            internal MemberInfo Member;

            /// <summary>Declared value type of the member.</summary>
            internal Type DeclaredType;
        }

        private static readonly Dictionary<Type, MemberEntry[]> Cache = new Dictionary<Type, MemberEntry[]>();

        private static readonly MemberEntry[] Empty = new MemberEntry[0];

        /// <summary>
        /// Returns every public and non-public instance property or field on the type and its base
        /// types that carries <c>[CreateProperty]</c>. Base types come first, properties before
        /// fields at each level, deduplicated by member name with the property winning.
        /// Repeated calls for the same type return the same array instance.
        /// </summary>
        internal static MemberEntry[] GetMembers(Type type)
        {
            if (type == null)
                return Empty;

            if (Cache.TryGetValue(type, out var cached))
                return cached;

            var result = new List<MemberEntry>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Walk the full inheritance chain, base types first for natural ordering.
            var searchTypes = new List<Type>();
            var t = type;
            while (t != null && t != typeof(object))
            {
                searchTypes.Insert(0, t);
                t = t.BaseType;
            }

            foreach (var searchType in searchTypes)
            {
                // Properties first (they shadow backing fields with the same logical name).
                foreach (var pi in searchType.GetProperties(flags | BindingFlags.DeclaredOnly))
                {
                    if (!Attribute.IsDefined(pi, typeof(CreatePropertyAttribute), inherit: false))
                        continue;
                    if (seenNames.Contains(pi.Name))
                        continue;
                    seenNames.Add(pi.Name);
                    result.Add(new MemberEntry { Name = pi.Name, Member = pi, DeclaredType = pi.PropertyType });
                }

                // Fields — only if not already covered by a property of the same name.
                foreach (var fi in searchType.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (!Attribute.IsDefined(fi, typeof(CreatePropertyAttribute), inherit: false))
                        continue;
                    if (seenNames.Contains(fi.Name))
                        continue;
                    seenNames.Add(fi.Name);
                    result.Add(new MemberEntry { Name = fi.Name, Member = fi, DeclaredType = fi.FieldType });
                }
            }

            var array = result.Count == 0 ? Empty : result.ToArray();
            Cache[type] = array;
            return array;
        }
    }
}
