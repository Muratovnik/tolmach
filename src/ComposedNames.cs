using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Tolmach
{
    // Texts a mod composes from its own parts ("<item> Pile", "You need 10 resin.") are translated only at
    // the sites the catalog names (Module.composed), never through the module's general table: there a
    // template such as "{0} Pile" would also reach text a player typed. Stored values are never changed.
    internal static class ComposedNames
    {
        private sealed class Site { public Module Module; public TextTable Table; }
        // Keyed by RuntimeAccess.MethodKey of the site method.
        private static readonly Dictionary<string, List<Site>> Sites = new Dictionary<string, List<Site>>(StringComparer.Ordinal);
        private static readonly HashSet<string> PatchedGameSites = new HashSet<string>(StringComparer.Ordinal);

        internal static void Reset() { Sites.Clear(); PatchedGameSites.Clear(); }

        // In the mod's own method the site's rules extend the module's table: the method's other display text
        // stays translated as before. A game method or a container shows every mod's text, so it gets only
        // the site's own rules.
        internal static TextTable OwnTable(Module m, ComposedSpec c)
        {
            Dictionary<string, string> texts = new Dictionary<string, string>(m.texts, StringComparer.Ordinal);
            // CatalogLoader rejects a site text that differs from the module's.
            foreach (KeyValuePair<string, string> t in c.texts) texts[t.Key] = t.Value;
            return new TextTable(texts, m.patterns.Concat(c.patterns), m.mapLabels, m.terms);
        }
        internal static TextTable SharedTable(Module m, ComposedSpec c)
        {
            return new TextTable(c.texts, c.patterns, new Dictionary<string, string>(), m.terms);
        }

        // DisplayPatches calls this for each scanned method of the module; a site's display calls,
        // string result and protected call sites then use DisplaySite with the returned key.
        internal static string RegisterOwn(Module m, Type type, MethodBase method)
        {
            string key = null;
            foreach (ComposedSpec c in m.composed)
                foreach (MethodSpec s in c.sites)
                {
                    if (s.type != type.FullName || s.method != method.Name || !m.OwnTypeName(s.type)) continue;
                    key = RuntimeAccess.MethodKey(method);
                    Add(key, m, OwnTable(m, c));
                }
            return key;
        }
        internal static bool IsSite(string key) { return key != null && Sites.ContainsKey(key); }

        internal static void InstallGameSites(Harmony harmony, Module m)
        {
            if (!m.UiAllowed) return;
            foreach (ComposedSpec c in m.composed)
                foreach (MethodSpec s in c.sites)
                {
                    if (m.OwnTypeName(s.type)) continue;
                    Type type = RuntimeAccess.ExactType(s.type);
                    List<MethodInfo> methods = new List<MethodInfo>();
                    try
                    {
                        if (type != null)
                            methods = AccessTools.GetDeclaredMethods(type).Where(delegate(MethodInfo x) {
                                return x.Name == s.method && x.ReturnType == typeof(string) && RuntimeAccess.ManagedBody(x);
                            }).ToList();
                    }
                    catch (Exception e) { m.Warn("Composed game site skipped " + s.type + "." + s.method + ": " + e.GetType().Name); continue; }
                    if (methods.Count == 0) { m.Warn("Composed game site not found: " + s.type + "." + s.method); continue; }
                    foreach (MethodInfo method in methods) PatchGameSite(harmony, m, c, method);
                }
        }
        private static void PatchGameSite(Harmony harmony, Module m, ComposedSpec c, MethodInfo method)
        {
            string key = RuntimeAccess.MethodKey(method);
            string owner = method.DeclaringType.FullName + "." + method.Name;
            string evidenceKey = owner + " [composed]";
            string unsupported = DisplayPatches.UnsupportedHandler(method);
            if (unsupported != null) { m.Warn("Composed game site skipped " + owner + ": " + unsupported); return; }
            if (!PatchedGameSites.Contains(key))
            {
                try
                {
                    // Priority.Last: the text is complete only after every other mod's postfix.
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(ComposedNames).GetMethod("AfterGameSite", AccessTools.all)) { priority = Priority.Last });
                    PatchedGameSites.Add(key);
                }
                catch (Exception e) { m.Warn("Composed game site skipped " + owner + ": " + e.GetType().Name); return; }
            }
            Add(key, m, SharedTable(m, c));
            if (m.Patches.ContainsKey(evidenceKey)) return;
            m.ScannedMethods++; m.PatchedMethods++;
            m.Patches[evidenceKey] = new PatchEvidence { method = evidenceKey, returnAdapter = true };
        }
        private static void AfterGameSite(MethodBase __originalMethod, ref string __result)
        {
            __result = Translate(__result, RuntimeAccess.MethodKey(__originalMethod));
        }
        private static void Add(string key, Module m, TextTable table)
        {
            List<Site> list;
            if (!Sites.TryGetValue(key, out list)) Sites[key] = list = new List<Site>();
            list.Add(new Site { Module = m, Table = table });
        }

        internal static string Translate(string text, string key)
        {
            List<Site> list;
            if (!TextEngine.IsRussian || String.IsNullOrEmpty(text) || key == null || !Sites.TryGetValue(key, out list)) return text;
            foreach (Site site in list)
            {
                Module current;
                if (!site.Module.UiAllowed || !TextEngine.Modules.TryGetValue(site.Module.id, out current) || !ReferenceEquals(site.Module, current)) continue;
                string translated = site.Table.Translate(text);
                if (translated != text) return translated;
            }
            return text;
        }
    }
}
