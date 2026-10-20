using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Tolmach;
using Module = Tolmach.Module;

namespace Tolmach.Tests
{
    // Composed texts ("<item> Pile") are translated only at the sites the catalog names.
    [TestFixture, NonParallelizable]
    public sealed class ComposedNamesTests : TestContextBase
    {
        private static List<PatternSpec> StylePatterns()
        {
            return new List<PatternSpec> {
                new PatternSpec { source = "{0} Pile", target = "{0} (груда)", singleLine = true, arguments = new Dictionary<string, string> { { "0", "name" } } },
                new PatternSpec { source = "{0} Barrel", target = "{0} (бочка)", singleLine = true, arguments = new Dictionary<string, string> { { "0", "name" } } },
                new PatternSpec { source = "{0} Crate", target = "{0} (ящик)", singleLine = true, arguments = new Dictionary<string, string> { { "0", "name" } } }
            };
        }
        private static ComposedSpec StorageNames(params MethodSpec[] sites)
        {
            var c = new ComposedSpec { patterns = StylePatterns() };
            c.texts["Components"] = "Компоненты";
            c.sites.AddRange(sites);
            return c;
        }
        private static string IteratorType()
        {
            return typeof(FixtureComposed.Storage).GetNestedTypes(BindingFlags.NonPublic).Single(t => t.Name.StartsWith("<Notify>", StringComparison.Ordinal)).FullName;
        }

        [Test]
        public void NameArgumentKeepsALocalizedNameAndRejectsAnUntranslatedEnglishOne()
        {
            ComposedSpec c = StorageNames();
            var table = new TextTable(c.texts, c.patterns, new Dictionary<string, string>(), new Dictionary<string, Dictionary<string, string>>());
            Assert.That(table.Translate("Кремень Pile"), Is.EqualTo("Кремень (груда)"));
            Assert.That(table.Translate("Черный металл Crate"), Is.EqualTo("Черный металл (ящик)"));
            Assert.That(table.Translate("Components Barrel"), Is.EqualTo("Компоненты (бочка)"), "A site text translates an English group name.");
            Assert.That(table.Translate("Wood Pile"), Is.EqualTo("Wood Pile"), "Half-English output is worse than the original.");
            Assert.That(table.Translate("Кремень <b>x</b> Pile"), Is.EqualTo("Кремень <b>x</b> Pile"), "Markup has Latin letters too.");
            Assert.That(table.Translate("  Pile"), Is.EqualTo("  Pile"));
        }

        [Test]
        public void OwnSiteTranslatesItsDisplayCallsResultAndCallSitesButNotTheModulesOtherMethods()
        {
            Module m = FixtureModule("composed", "FixtureComposed");
            m.texts["Price"] = "Цена";
            m.composed.Add(StorageNames(
                new MethodSpec { type = "FixtureComposed.Storage", method = "Summary" },
                new MethodSpec { type = "FixtureComposed.Storage", method = "Title" },
                new MethodSpec { type = IteratorType(), method = "MoveNext" }));
            DisplayPatches.Install(Patcher, Activate(m));

            FixtureComposed.Storage.Summary("Pile");
            Assert.That(UnityEngine.GUI.LastText, Is.EqualTo("Кремень (груда)"));
            FixtureComposed.Storage.Price();
            Assert.That(UnityEngine.GUI.LastText, Is.EqualTo("Цена"), "The module's own table still applies elsewhere.");
            FixtureComposed.Storage.Label("Кремень Pile");
            Assert.That(UnityEngine.GUI.LastText, Is.EqualTo("Кремень Pile"), "A label the player typed is not a site.");
            Assert.That(FixtureComposed.Storage.Title("Barrel"), Is.EqualTo("Кремень (бочка)"));
            Player player = new Player();
            FixtureComposed.Storage.Notify(player, "Crate").ToList();
            Assert.That(player.LastMessage, Is.EqualTo("Кремень (ящик)"), "A fault-protected site is translated where its call arrives.");

            FixtureComposed.Storage.Assigned = "Wood";
            try
            {
                FixtureComposed.Storage.Summary("Pile");
                Assert.That(UnityEngine.GUI.LastText, Is.EqualTo("Wood Pile"));
            }
            finally { FixtureComposed.Storage.Assigned = "Кремень"; }
            TextEngine.IsRussian = false;
            FixtureComposed.Storage.Summary("Pile");
            Assert.That(UnityEngine.GUI.LastText, Is.EqualTo("Кремень Pile"));
            Assert.That(m.Warnings, Is.Empty);
        }

        [Test]
        public void MissingOwnSiteIsReported()
        {
            Module m = FixtureModule("composed-missing", "FixtureComposed");
            m.composed.Add(StorageNames(new MethodSpec { type = "FixtureComposed.Storage", method = "Absent" }));
            DisplayPatches.Install(Patcher, Activate(m));
            Assert.That(m.Warnings, Has.Some.EqualTo("Expected composed site not found: FixtureComposed.Storage.Absent"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GameSiteTranslatesTheFinalResultAfterTheModsPostfix(bool modPatchedFirst)
        {
            Module m = FixtureModule("composed-game", "FixtureComposedMod");
            var c = new ComposedSpec();
            c.sites.Add(new MethodSpec { type = "ComposedPlant", method = "GetHoverText" });
            c.texts["Bone Meal"] = "Костная мука";
            m.composed.Add(c);
            Activate(m);
            MethodInfo hover = AccessTools(typeof(ComposedPlant), "GetHoverText");
            var modPostfix = new HarmonyLib.HarmonyMethod(typeof(FixtureComposedMod.PlantHoverTextPatch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static));
            if (modPatchedFirst) Patcher.Patch(hover, postfix: modPostfix);
            ComposedNames.InstallGameSites(Patcher, m);
            if (!modPatchedFirst) Patcher.Patch(hover, postfix: modPostfix);

            var plant = new ComposedPlant { Fertilized = true };
            Assert.That(plant.GetHoverText(), Is.EqualTo("Морковь\nРастет\nКостная мука"));
            plant.Fertilized = false;
            Assert.That(plant.GetHoverText(), Is.EqualTo("Морковь\nРастет"));
            Assert.That(m.Patches.Keys, Has.Some.EqualTo("ComposedPlant.GetHoverText [composed]"));
            m.UiAllowed = false;
            plant.Fertilized = true;
            Assert.That(plant.GetHoverText(), Is.EqualTo("Морковь\nРастет\nBone Meal"));
        }

        [Test]
        public void ItemRuleAtAGameSiteTranslatesOnlyTheNamedItems()
        {
            // More World Locations gives two offerings the vanilla crypt key description.
            Module m = FixtureModule("composed-items", "FixtureComposedMod");
            var c = new ComposedSpec();
            c.sites.Add(new MethodSpec { type = "ComposedItem", method = "GetTooltip" });
            c.items.Add("BFD_CryptKey");
            c.texts["$item_cryptkey_description"] = "Ключ от потайной двери в Подземных руинах.";
            m.composed.Add(c);
            ComposedNames.InstallGameSites(Patcher, Activate(m));
            var offering = new ComposedItem { m_dropPrefab = new ComposedPrefab { name = "BFD_CryptKey" } };
            var vanilla = new ComposedItem { m_dropPrefab = new ComposedPrefab { name = "CryptKey" } };
            Assert.That(ComposedItem.GetTooltip(offering, 1), Is.EqualTo("Ключ от потайной двери в Подземных руинах.\n$item_weight: 0.1"));
            Assert.That(offering.GetTooltip(), Is.EqualTo("Ключ от потайной двери в Подземных руинах.\n$item_weight: 0.1"), "The instance overload names the item too.");
            Assert.That(ComposedItem.GetTooltip(vanilla, 1), Is.EqualTo("$item_cryptkey_description\n$item_weight: 0.1"), "The vanilla key keeps its description.");
            Assert.That(ComposedItem.GetTooltip(new ComposedItem(), 1), Is.EqualTo("$item_cryptkey_description\n$item_weight: 0.1"), "An item without a drop prefab is not named.");
            Assert.That(m.Warnings, Is.Empty);
        }

        [Test]
        public void MissingGameSiteIsReported()
        {
            Module m = FixtureModule("composed-game-missing", "FixtureComposedMod");
            var c = new ComposedSpec();
            c.sites.Add(new MethodSpec { type = "ComposedPlant", method = "GetHoverName" });
            c.texts["Bone Meal"] = "Костная мука";
            m.composed.Add(c);
            ComposedNames.InstallGameSites(Patcher, Activate(m));
            Assert.That(m.Warnings, Has.Some.EqualTo("Composed game site not found: ComposedPlant.GetHoverName"));
        }

        [Test]
        public void ContainerNameIsTranslatedOnlyOnAContainerCarryingAnOwnerComponent()
        {
            Module m = FixtureModule("composed-containers", "FixtureComposed");
            var c = new ComposedSpec { patterns = StylePatterns() };
            c.containerOwners.Add("FixtureComposed.StorageMarker");
            m.composed.Add(c);
            Activate(m);
            ContainerNameDisplay.Install(Patcher, typeof(MarkedContainer));
            var owned = new MarkedContainer { m_name = "Кремень Pile", Marker = new FixtureComposed.StorageMarker() };
            Assert.That(owned.GetHoverName(), Is.EqualTo("Кремень (груда)"));
            Assert.That(owned.GetHoverText(), Is.EqualTo("Кремень (груда) ( EMPTY )"));
            Assert.That(owned.m_name, Is.EqualTo("Кремень Pile"));
            var other = new MarkedContainer { m_name = "Кремень Pile" };
            Assert.That(other.GetHoverName(), Is.EqualTo("Кремень Pile"), "Another mod's or a renamed container keeps its name.");
            owned.m_name = "Wood Pile";
            Assert.That(owned.GetHoverName(), Is.EqualTo("Wood Pile"));
            Assert.That(m.Patches[typeof(MarkedContainer).FullName + ".GetHoverName [container name]"].displayFields, Is.EqualTo(1));
        }

        [Test]
        public void FoodTooltipRegenerationUnitIsTranslatedOnlyInItsOwnLine()
        {
            // ItemDrop.ItemData.GetTooltip (Valheim 1.0.20) appends "$item_food_regen: <color=orange>{0} hp/tick</color>".
            Module m = CatalogLoader.Read(Catalog("Valheim"));
            ComposedSpec rule = m.composed.Single(c => c.sites.Any(s => s.type == "ItemDrop+ItemData" && s.method == "GetTooltip"));
            TextTable table = ComposedNames.SharedTable(m, rule);
            string tooltip = "$item_food_health: <color=orange>20</color>\n$item_food_regen: <color=orange>2 hp/tick</color>";
            Assert.That(table.Translate(tooltip), Is.EqualTo("$item_food_health: <color=orange>20</color>\n$item_food_regen: <color=orange>2 ОЗ/тик</color>"));
            Assert.That(table.Translate("$item_food_regen: <color=orange>1.5 hp/tick</color>"), Is.EqualTo("$item_food_regen: <color=orange>1.5 ОЗ/тик</color>"));
            Assert.That(table.Translate("$item_food_regen: <color=#ffa500ff>5 hp/tick</color>"), Is.EqualTo("$item_food_regen: <color=#ffa500ff>5 ОЗ/тик</color>"),
                "Seen on the stand: the color reaches the tooltip as a hex value.");
            Assert.That(table.Translate("Heals 2 hp/tick"), Is.EqualTo("Heals 2 hp/tick"), "Text outside the game's line stays.");
        }

        [Test]
        public void FoodTooltipDurationUnitsAreTranslatedInEveryShapeOfTheGameString()
        {
            // ItemDrop.GetDurationString (Valheim 1.0.20): "25m 30s", "25m " (trailing space) or "30s".
            Module m = CatalogLoader.Read(Catalog("Valheim"));
            ComposedSpec rule = m.composed.Single(c => c.sites.Any(s => s.type == "ItemDrop+ItemData" && s.method == "GetTooltip"));
            TextTable table = ComposedNames.SharedTable(m, rule);
            Assert.That(table.Translate("$item_food_duration: <color=orange>25m 30s</color>"), Is.EqualTo("$item_food_duration: <color=orange>25 мин 30 с</color>"));
            Assert.That(table.Translate("$item_food_duration: <color=orange>25m </color>"), Is.EqualTo("$item_food_duration: <color=orange>25 мин </color>"));
            Assert.That(table.Translate("$item_food_duration: <color=#ffa500ff>30s</color>"), Is.EqualTo("$item_food_duration: <color=#ffa500ff>30 с</color>"));
            Assert.That(table.Translate("Lasts 25m 30s"), Is.EqualTo("Lasts 25m 30s"), "Text outside the game's line stays.");
        }

        [Test]
        public void GroupHudMinutesAndMetresAreTranslatedOnlyInTheirOwnMethods()
        {
            // FormatFoodTime returns "15m" for minutes, DistanceText "120m" or "120m С" for metres.
            Module m = CatalogLoader.Read(Catalog("GroupHUD"));
            TextTable food = ComposedNames.OwnTable(m, m.composed.Single(c => c.sites.Single().method == "FormatFoodTime"));
            TextTable distance = ComposedNames.OwnTable(m, m.composed.Single(c => c.sites.Single().method == "DistanceText"));
            Assert.That(food.Translate("15m"), Is.EqualTo("15 мин"));
            Assert.That(food.Translate("30s"), Is.EqualTo("30 с"));
            Assert.That(distance.Translate("120m"), Is.EqualTo("120 м"));
            Assert.That(distance.Translate("120m СВ"), Is.EqualTo("120 м СВ"));
            Assert.That(food.Translate("Xm"), Is.EqualTo("Xm"), "Only a number takes the unit.");
        }

        // MyLittleUI's hover postfix: rebuilds the whole hover from the m_name field.
        private static void RebuiltHover(MarkedContainer __instance, ref string __result)
        {
            __result = __instance.m_name + " 3/32\n[E] Открыть";
        }
        [TestCase(true)]
        [TestCase(false)]
        public void HoverRebuiltByAnotherModFromTheFieldStillShowsTheTranslatedName(bool modPatchedFirst)
        {
            Module m = FixtureModule("composed-rebuilt-hover", "FixtureComposed");
            var c = new ComposedSpec { patterns = StylePatterns() };
            c.containerOwners.Add("FixtureComposed.StorageMarker");
            m.composed.Add(c);
            Activate(m);
            MethodInfo hover = AccessTools(typeof(MarkedContainer), "GetHoverText");
            var rebuild = new HarmonyLib.HarmonyMethod(typeof(ComposedNamesTests), "RebuiltHover");
            if (modPatchedFirst) Patcher.Patch(hover, postfix: rebuild);
            ContainerNameDisplay.Install(Patcher, typeof(MarkedContainer));
            if (!modPatchedFirst) Patcher.Patch(hover, postfix: rebuild);
            var owned = new MarkedContainer { m_name = "Кремень Pile", Marker = new FixtureComposed.StorageMarker() };
            Assert.That(owned.GetHoverText(), Is.EqualTo("Кремень (груда) 3/32\n[E] Открыть"));
            Assert.That(owned.m_name, Is.EqualTo("Кремень Pile"));
            var other = new MarkedContainer { m_name = "Кремень Pile" };
            Assert.That(other.GetHoverText(), Is.EqualTo("Кремень Pile 3/32\n[E] Открыть"), "Another mod's container keeps its name.");
            m.UiAllowed = false;
            Assert.That(owned.GetHoverText(), Is.EqualTo("Кремень Pile 3/32\n[E] Открыть"));
        }

        [Test]
        public void MissingOwnerComponentDisablesTheRuleWithAWarning()
        {
            Module m = FixtureModule("composed-owner-missing", "FixtureComposed");
            var c = new ComposedSpec { patterns = StylePatterns() };
            c.containerOwners.Add("FixtureComposed.Absent");
            m.composed.Add(c);
            Activate(m);
            ContainerNameDisplay.Install(Patcher, typeof(MarkedContainer));
            var container = new MarkedContainer { m_name = "Кремень Pile", Marker = new FixtureComposed.StorageMarker() };
            Assert.That(container.GetHoverName(), Is.EqualTo("Кремень Pile"));
            Assert.That(m.Warnings, Has.Some.EqualTo("Container owner component not found: FixtureComposed.Absent; its composed names stay unchanged."));
        }

        [Test]
        public void LoaderRejectsUnsafeOrInconsistentComposedRules()
        {
            Func<ComposedSpec, Module> module = rule => {
                var m = new Module { id = "x", package = "x", version = "1.0.0", pluginVersion = "1.0.0", assembly = "x" };
                m.guids.Add("x"); m.namespaces.Add("FixtureComposed"); m.texts["Price"] = "Цена";
                m.composed.Add(rule);
                return m;
            };
            var bare = new ComposedSpec(); bare.containerOwners.Add("FixtureComposed.StorageMarker");
            bare.patterns.Add(new PatternSpec { source = "{0}", target = "{0}", arguments = new Dictionary<string, string> { { "0", "name" } } });
            Assert.Throws<InvalidDataException>(() => CatalogLoader.Validate(module(bare)), "A container template needs fixed text.");
            var game = new ComposedSpec(); game.sites.Add(new MethodSpec { type = "Plant", method = "GetHoverText" });
            game.patterns.Add(new PatternSpec { source = "{0}", target = "{0}" });
            Assert.Throws<InvalidDataException>(() => CatalogLoader.Validate(module(game)), "A game-site template needs fixed text.");
            var conflict = StorageNames(new MethodSpec { type = "FixtureComposed.Storage", method = "Summary" });
            conflict.texts["Price"] = "Стоимость";
            Assert.Throws<InvalidDataException>(() => CatalogLoader.Validate(module(conflict)));
            var empty = new ComposedSpec(); empty.sites.Add(new MethodSpec { type = "FixtureComposed.Storage", method = "Summary" });
            Assert.Throws<InvalidDataException>(() => CatalogLoader.Validate(module(empty)), "A rule needs texts or patterns.");
            var nowhere = new ComposedSpec { patterns = StylePatterns() };
            Assert.Throws<InvalidDataException>(() => CatalogLoader.Validate(module(nowhere)), "A rule needs a site or container owners.");
            // A bare template is allowed in the mod's own method, as for its display patterns.
            var own = new ComposedSpec(); own.sites.Add(new MethodSpec { type = "FixtureComposed.Storage", method = "Summary" });
            own.patterns.Add(new PatternSpec { source = "{0}", target = "{0}", arguments = new Dictionary<string, string> { { "0", "name" } } });
            Assert.DoesNotThrow(() => CatalogLoader.Validate(module(own)));
            var ownItems = new ComposedSpec(); ownItems.sites.Add(new MethodSpec { type = "FixtureComposed.Storage", method = "Title" });
            ownItems.items.Add("BFD_CryptKey"); ownItems.texts["Barrel"] = "Бочка";
            Assert.Throws<InvalidDataException>(() => CatalogLoader.Validate(module(ownItems)), "Only a game method shows an item.");
        }

        private static MethodInfo AccessTools(Type type, string name) { return HarmonyLib.AccessTools.Method(type, name); }
    }
}

namespace FixtureComposed
{
    // Same shape as Dynamic Storage Materials: a localized item name and an English style word.
    public static class Storage
    {
        public static string Assigned = "Кремень";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Summary(string style) { UnityEngine.GUI.Label(Assigned + " " + style); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Price() { UnityEngine.GUI.Label("Price"); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Label(string typed) { UnityEngine.GUI.Label(typed); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Title(string style) { return Assigned + " " + style; }
        public static bool Done;
        public static IEnumerable<int> Notify(Character user, string style)
        {
            try { user.Message(MessageHud.MessageType.Center, Assigned + " " + style); yield return 0; }
            finally { Done = true; }
        }
    }
    public sealed class StorageMarker { }
}
// Same shape as Fertilizer's PlantHoverTextPatch: a postfix appends an English line to the game's hover.
namespace FixtureComposedMod
{
    internal static class PlantHoverTextPatch
    {
        private static void Postfix(ComposedPlant __instance, ref string __result)
        {
            if (__instance.Fertilized) __result = __result + "\nBone Meal";
        }
    }
}
// Test-only stand-ins for a game method and a container that carries mod components.
public sealed class ComposedPlant
{
    public bool Fertilized;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string GetHoverText() { return "Морковь\nРастет"; }
}
// Same shape as ItemDrop.ItemData: a static tooltip for an item and an instance overload calling it.
public sealed class ComposedPrefab { public string name { get; set; } }
public sealed class ComposedItem
{
    public ComposedPrefab m_dropPrefab;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string GetTooltip() { return GetTooltip(this, 1); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string GetTooltip(ComposedItem item, int quality) { return "$item_cryptkey_description\n$item_weight: 0.1"; }
}
public sealed class MarkedContainer
{
    public string m_name;
    public object Marker;
    public object GetComponent(Type type) { return Marker != null && type.IsInstanceOfType(Marker) ? Marker : null; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string GetHoverName() { return m_name; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string GetHoverText() { return m_name + " ( EMPTY )"; }
}
