using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Tolmach
{
    // Tells from the assembly's file whether a method body names a display sink, without building the method the
    // way Harmony does. PatchProcessor.GetOriginalInstructions copies every body it reads into a Harmony
    // DynamicMethodDefinition; on a profile of 108 plugins that is 15,000 bodies and most of the plugin's start.
    // Mono.Cecil, which Harmony itself reads assemblies with and which BepInEx ships, reads the same bodies about
    // thirty times faster because it only decodes them.
    //
    // The answer is a necessary condition: false only when the body has no call whose method name is a sink's
    // (DisplayPatches.MayCallSink) and no store to a display field (MayStoreDisplayField); true whenever the
    // answer is not certain. The caller still reads a body that may call a sink with Harmony and decides from it.
    internal sealed class BodyScreen : IDisposable
    {
        private readonly ModuleDefinition definition;
        private BodyScreen(ModuleDefinition definition) { this.definition = definition; }

        // Null when the assembly has no file to read or the file is not the loaded module.
        internal static BodyScreen Open(Assembly assembly)
        {
            try
            {
                if (assembly == null || assembly.IsDynamic || String.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location)) return null;
                ModuleDefinition module = ModuleDefinition.ReadModule(assembly.Location);
                if (module.Mvid != assembly.ManifestModule.ModuleVersionId) { module.Dispose(); return null; }
                return new BodyScreen(module);
            }
            catch (Exception) { return null; }
        }

        internal bool MayCallSink(MethodBase method)
        {
            try
            {
                if (method.Module.ModuleVersionId != definition.Mvid) return true;
                MethodDefinition body = definition.LookupToken(new MetadataToken((uint)method.MetadataToken)) as MethodDefinition;
                if (body == null || !body.HasBody) return true;
                foreach (Instruction instruction in body.Body.Instructions)
                {
                    MethodReference call = instruction.Operand as MethodReference;
                    if (call != null)
                    {
                        if (DisplayPatches.MayCallSink(call.Name, call.DeclaringType == null ? null : call.DeclaringType.FullName)) return true;
                        continue;
                    }
                    FieldReference field = instruction.Operand as FieldReference;
                    if (field != null && DisplayPatches.MayStoreDisplayField(field.Name)) return true;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        public void Dispose() { definition.Dispose(); }
    }
}
