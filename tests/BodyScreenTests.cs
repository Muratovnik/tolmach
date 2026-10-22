using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NUnit.Framework;
using Tolmach;
using Module = Tolmach.Module;

namespace Tolmach.Tests
{
    // The screen decides from the assembly's file, with Mono.Cecil, which bodies the scan reads with Harmony. It may
    // only reject a body that no sink predicate can accept; everything else is read as before.
    [TestFixture, NonParallelizable]
    public sealed class BodyScreenTests : TestContextBase
    {
        private static IEnumerable<MethodBase> MethodsOf(Assembly assembly)
        {
            foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
            {
                List<MethodBase> methods;
                try
                {
                    if (type.ContainsGenericParameters) continue;
                    methods = AccessTools.GetDeclaredMethods(type).Cast<MethodBase>().Concat(AccessTools.GetDeclaredConstructors(type, false).Cast<MethodBase>()).ToList();
                }
                catch (Exception) { continue; } // FixtureOptional refers to an assembly that is absent on purpose.
                foreach (MethodBase method in methods) yield return method;
            }
        }

        // The oracle reads each body with Harmony and asks the production predicates, as the scan does without a
        // screen. Three module ids: two sinks, the HUD message fields and the status name, belong to named modules.
        [Test]
        public void ScreenNeverRejectsABodyThatReachesADisplaySink()
        {
            Module[] modules = new string[] { "wide", "ExpertExplorer", "SpeedyPaths" }.Select(id => FixtureModule(id, "Tolmach.Tests")).ToArray();
            int reaching = 0, rejected = 0, bodies = 0;
            using (BodyScreen screen = BodyScreen.Open(Assembly.GetExecutingAssembly()))
            {
                Assert.That(screen, Is.Not.Null, "The test assembly has a file to read.");
                foreach (MethodBase method in MethodsOf(Assembly.GetExecutingAssembly()))
                {
                    List<CodeInstruction> body;
                    try
                    {
                        if (!RuntimeAccess.ManagedBody(method)) continue;
                        body = PatchProcessor.GetOriginalInstructions(method, null);
                    }
                    catch (Exception) { continue; }
                    bodies++;
                    bool may = screen.MayCallSink(method);
                    if (!may) rejected++;
                    foreach (Module module in modules)
                    {
                        if (!body.Any(i => HarmonyTests.ReachesDisplay(i, module, method))) continue;
                        reaching++;
                        Assert.That(may, Is.True, module.id + ": " + method.DeclaringType.FullName + "." + method.Name + " reaches a display sink");
                    }
                }
            }
            Assert.That(reaching, Is.GreaterThan(10), "The comparison needs bodies that do reach a sink.");
            Assert.That(rejected, Is.GreaterThan(bodies / 2), "Most bodies of the test assembly name no sink.");
        }

        // The screen's names come from the same table as the predicate; every method that StringSink accepts
        // must pass the name screen. The fakes of the Unity and game types are the ones the fixtures call.
        [Test]
        public void EveryMethodThatStringSinkAcceptsPassesTheNameScreen()
        {
            HashSet<string> acceptedTypes = new HashSet<string>();
            foreach (MethodBase method in MethodsOf(Assembly.GetExecutingAssembly()))
            {
                bool[] selected;
                try { selected = DisplayPatches.StringSink(method); }
                catch (Exception) { continue; }
                if (!selected.Any(s => s)) continue;
                acceptedTypes.Add(method.DeclaringType.FullName);
                Assert.That(DisplayPatches.MayCallSink(method.IsConstructor ? ".ctor" : method.Name, method.DeclaringType.FullName), Is.True,
                    method.DeclaringType.FullName + "." + method.Name);
            }
            Assert.That(acceptedTypes, Is.SupersetOf(new string[] { "UnityEngine.GUI", "Localization", "Player" }), "The comparison needs sinks to compare.");
            // A scoped sink is accepted by name for one module's handler.
            Assert.That(DisplayPatches.MayCallSink("AddString", "Terminal"), Is.True);
            // A constructor is keyed by its exact type: any other type's is not a sink.
            Assert.That(DisplayPatches.MayCallSink(".ctor", "UnityEngine.GUIContent"), Is.True);
            Assert.That(DisplayPatches.MayCallSink(".ctor", "System.Text.StringBuilder"), Is.False);
            Assert.That(DisplayPatches.MayCallSink("ToString", "System.Object"), Is.False);
            Assert.That(DisplayPatches.MayStoreDisplayField("m_name"), Is.True);
            Assert.That(DisplayPatches.MayStoreDisplayField("m_other"), Is.False);
        }

        // Anything the screen cannot decide is read with Harmony, as without it.
        [Test]
        public void ScreenLeavesWhatItCannotDecideToTheRead()
        {
            Assert.That(BodyScreen.Open(null), Is.Null);
            AssemblyBuilder dynamic = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("ScreenFallback"), AssemblyBuilderAccess.Run);
            Assert.That(BodyScreen.Open(dynamic), Is.Null, "A dynamic assembly has no file.");
            using (BodyScreen screen = BodyScreen.Open(Assembly.GetExecutingAssembly()))
            {
                MethodInfo foreign = typeof(string).GetMethod("Trim", Type.EmptyTypes);
                Assert.That(screen.MayCallSink(foreign), Is.True, "A method of another module is not the screen's to judge.");
            }
        }
    }
}
