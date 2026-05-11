using System;
using System.Collections.Generic;
using System.Reflection;

namespace WorldCustomizer
{
    /// <summary>
    /// Reflection helpers for reading and writing private/serialized fields on game types.
    /// Caches FieldInfo lookups by (declaring type, field name).
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, FieldInfo> s_FieldCache =
            new Dictionary<string, FieldInfo>();

        public static void SetField<T>(object target, string fieldName, T value)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            FieldInfo fi = GetField(target.GetType(), fieldName);
            fi.SetValue(target, value);
        }

        public static T GetField<T>(object target, string fieldName)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            FieldInfo fi = GetField(target.GetType(), fieldName);
            return (T)fi.GetValue(target);
        }

        /// <summary>
        /// Returns the field if it exists, otherwise null. Use when missing fields are
        /// non-fatal (e.g. settings that depend on game-version-specific internals).
        /// </summary>
        public static FieldInfo TryGetField(Type declaringType, string fieldName)
        {
            string key = MakeKey(declaringType, fieldName);
            if (s_FieldCache.TryGetValue(key, out FieldInfo cached))
                return cached;

            FieldInfo fi = ResolveField(declaringType, fieldName);
            s_FieldCache[key] = fi;
            return fi;
        }

        private static FieldInfo GetField(Type declaringType, string fieldName)
        {
            FieldInfo fi = TryGetField(declaringType, fieldName);
            if (fi == null)
                throw new InvalidOperationException(
                    $"Field '{declaringType.FullName}.{fieldName}' not found");
            return fi;
        }

        private static FieldInfo ResolveField(Type declaringType, string fieldName)
        {
            // Walk up the inheritance chain — private fields aren't inherited so a
            // single GetField call may miss base-class fields.
            Type t = declaringType;
            while (t != null)
            {
                FieldInfo fi = t.GetField(fieldName, InstanceFlags);
                if (fi != null)
                    return fi;
                t = t.BaseType;
            }
            return null;
        }

        private static string MakeKey(Type t, string fieldName)
        {
            return t.FullName + "::" + fieldName;
        }

        /// <summary>For tests / diagnostics. Not used at runtime.</summary>
        internal static int CachedFieldCount => s_FieldCache.Count;
    }
}
