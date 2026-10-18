using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Tolmach
{
    public static class ContainerNameDisplay
    {
        // A composed container name ("<item> Pile") is translated only on a container that carries one of
        // the owner components: another mod's container, or one a player renamed, keeps its name.
        private sealed class Rule { public Module Module; public TextTable Table; public Type[] Owners; }
        private static readonly List<Module> Owners = new List<Module>();
        private static readonly List<Rule> Rules = new List<Rule>();
        private static FieldInfo nameField;
        private static MethodInfo getComponent;
        internal static void Reset() { Owners.Clear(); Rules.Clear(); nameField = null; getComponent = null; }

        internal static void Install(Harmony harmony, Type containerType)
        {
            Reset();
            Owners.AddRange(TextEngine.Modules.Values.Where(m => m.UiAllowed && m.id.StartsWith("DynamicStorage", StringComparison.Ordinal) && m.rawTexts.Count != 0)
                .OrderBy(m => m.id, StringComparer.Ordinal));
            getComponent = containerType == null ? null : AccessTools.Method(containerType, "GetComponent", new[] { typeof(Type) });
            foreach (Module m in TextEngine.Modules.Values.Where(m => m.UiAllowed).OrderBy(m => m.id, StringComparer.Ordinal))
                foreach (ComposedSpec c in m.composed.Where(c => c.containerOwners.Count != 0))
                {
                    Assembly assembly = m.RuntimeCodeAssembly ?? m.RuntimeAssembly;
                    Type[] owners = c.containerOwners.Select(name => assembly == null ? null : assembly.GetType(name, false)).ToArray();
                    foreach (string missing in c.containerOwners.Where((name, i) => owners[i] == null))
                        m.Warn("Container owner component not found: " + missing + "; its composed names stay unchanged.");
                    owners = owners.Where(t => t != null).ToArray();
                    if (owners.Length == 0) continue;
                    if (getComponent == null || getComponent.IsStatic) { m.Warn("Container GetComponent unavailable; composed container names stay unchanged."); continue; }
                    Rules.Add(new Rule { Module = m, Table = ComposedNames.SharedTable(m, c), Owners = owners });
                }
            if (Owners.Count == 0 && Rules.Count == 0) return;
            nameField = containerType == null ? null : AccessTools.Field(containerType, "m_name");
            if (nameField == null || nameField.IsStatic || nameField.FieldType != typeof(string))
            { Warn("Container name field unavailable; original names retained."); return; }
            nameField = AccessTools.DeclaredField(nameField.DeclaringType, nameField.Name);
            foreach (string name in new[] { "GetHoverName", "GetHoverText" })
            {
                try
                {
                    MethodInfo method = AccessTools.Method(containerType, name, Type.EmptyTypes);
                    if (method == null || method.IsStatic || method.ReturnType != typeof(string) || !RuntimeAccess.ManagedBody(method))
                    { Warn("Container name display target unavailable: " + name); continue; }
                    string unsupported = DisplayPatches.UnsupportedHandler(method);
                    if (unsupported != null) { Record(method, 0, unsupported); continue; }
                    harmony.Patch(method, transpiler: new HarmonyMethod(AccessTools.Method(typeof(ContainerNameDisplay), "Transpile")));
                }
                catch (Exception e) { Warn("Container name display target " + name + ": " + e.GetType().Name); }
            }
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                yield return instruction;
                if (i == 0 || instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, nameField) ||
                    code[i - 1].opcode != OpCodes.Ldarg_0 || instruction.labels.Count != 0 ||
                    instruction.blocks.Count != 0 || code[i - 1].blocks.Count != 0) continue;
                // The empty/access suffix is composed afterward. Translating the whole
                // hover line cannot match an exact catalog name once that suffix exists.
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ContainerNameDisplay), "Display"));
                count++;
            }
            Record(__originalMethod, count, count == 0 ? "no safe container name read" : null);
        }

        private static IEnumerable<Module> Modules()
        {
            return Owners.Concat(Rules.Select(r => r.Module)).Distinct();
        }
        private static void Record(MethodBase method, int count, string reason)
        {
            string key = method.DeclaringType.FullName + "." + method.Name + " [container name]";
            foreach (Module owner in Modules())
            {
                PatchEvidence previous;
                if (!owner.Patches.TryGetValue(key, out previous)) owner.ScannedMethods++;
                if (previous != null && previous.displayFields > 0) owner.PatchedMethods--;
                if (count > 0) owner.PatchedMethods++;
                owner.Patches[key] = new PatchEvidence { method = key, displayFields = count, notPatched = reason };
            }
        }

        private static void Warn(string message) { foreach (Module owner in Modules()) owner.Warn(message); }
        private static bool Active(Module m)
        {
            Module current;
            return m.UiAllowed && TextEngine.Modules.TryGetValue(m.id, out current) && ReferenceEquals(m, current);
        }
        public static string Display(string value, object container)
        {
            if (!TextEngine.IsRussian || String.IsNullOrEmpty(value)) return value;
            string translated;
            foreach (Module owner in Owners)
                if (Active(owner) && owner.rawTexts.TryGetValue(value, out translated)) return translated;
            if (container == null) return value;
            foreach (Rule rule in Rules)
            {
                if (!Active(rule.Module)) continue;
                // Hover runs every frame on every chest: the component lookup only follows a match.
                translated = rule.Table.Translate(value);
                if (translated != value && Carries(container, rule.Owners)) return translated;
            }
            return value;
        }
        private static bool Carries(object container, Type[] owners)
        {
            foreach (Type owner in owners)
            {
                object component;
                try { component = getComponent.Invoke(container, new object[] { owner }); }
                catch (Exception) { return false; }
                if (component != null && owner.IsInstanceOfType(component)) return true;
            }
            return false;
        }
    }
}
