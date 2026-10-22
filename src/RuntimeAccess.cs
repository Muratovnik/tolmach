using System;
using System.Reflection;
using HarmonyLib;

namespace Tolmach
{
    // Product policy only. Harmony owns type enumeration and member/base-type lookup.
    internal static class RuntimeAccess
    {
        // The first loaded type whose full name is exactly this one; a short name is never an identity.
        // Every assembly answers from its own type table. AccessTools.TypeByName instead enumerates all
        // types of all loaded assemblies, and once more for a short-name fallback that is discarded here
        // when the name is absent: tens of milliseconds per call on a profile with a hundred plugins,
        // paid for the same Jotunn type once per module on every refresh.
        internal static Type ExactType(string fullName)
        {
            if (String.IsNullOrEmpty(fullName)) return null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try { type = assembly.GetType(fullName, false); }
                catch (Exception) { } // An assembly with an unresolved dependency cannot supply the type.
                if (type != null && type.FullName == fullName) return type;
            }
            return null;
        }
        internal static object Read(object instanceOrType, string name)
        {
            if (instanceOrType == null) return null;
            Type type = instanceOrType as Type ?? instanceOrType.GetType();
            object receiver = instanceOrType is Type ? null : instanceOrType;
            // The same base-type search as AccessTools.Field/Property, without their warning for a
            // member that is a property rather than a field (or absent from this game version).
            FieldInfo field = AccessTools.FindIncludingBaseTypes(type, delegate(Type t) { return t.GetField(name, AccessTools.all); });
            if (field != null) return field.GetValue(receiver);
            PropertyInfo property = AccessTools.FindIncludingBaseTypes(type, delegate(Type t) { return t.GetProperty(name, AccessTools.all); });
            return property == null ? null : property.GetValue(receiver, null);
        }
        internal static string MethodKey(MethodBase method)
        {
            return method.Module.ModuleVersionId.ToString("N") + ":" + method.MetadataToken;
        }
        internal static bool ManagedBody(MethodBase method)
        {
            if (method.IsAbstract) return false;
            // Mono can crash in ContainsGenericParameters when a signature references
            // an absent optional assembly. Resolve it through the checked reflection
            // API first, so the caller can catch the load failure and skip this method.
            method.GetParameters();
            if (method.ContainsGenericParameters) return false;
            try { return method.GetMethodBody() != null; }
            catch (InvalidOperationException) { return false; }
            catch (NotSupportedException) { return false; }
        }
    }
}
