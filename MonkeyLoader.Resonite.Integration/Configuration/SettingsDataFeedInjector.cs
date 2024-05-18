﻿using Elements.Assets;
using Elements.Core;
using Elements.Quantity;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using MonkeyLoader.Components;
using MonkeyLoader.Configuration;
using MonkeyLoader.Meta;
using MonkeyLoader.Patching;
using MonkeyLoader.Resonite.Locale;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MonkeyLoader.Resonite.Configuration
{
    [HarmonyPatch(typeof(SettingsDataFeed))]
    [HarmonyPatchCategory(nameof(SettingsDataFeedInjector))]
    internal sealed class SettingsDataFeedInjector : ResoniteAsyncEventHandlerMonkey<SettingsDataFeedInjector, FallbackLocaleGenerationEvent>
    {
        public const string ConfigKeyChangeLabel = "Settings";
        public const string ConfigSections = "ConfigSections";

        public const string MonkeyToggles = "MonkeyToggles";

        private const string EarlyMonkeys = "EarlyMonkeys";
        private const string Monkeys = "Monkeys";

        private const string ResetConfig = "ResetConfig";
        private const string SaveConfig = "SaveConfig";

        private static readonly MethodInfo _generateEnumField = AccessTools.Method(typeof(SettingsDataFeedInjector), nameof(GenerateEnumField));
        private static readonly MethodInfo _generateItemForConfigKeyMethod = AccessTools.Method(typeof(SettingsDataFeedInjector), nameof(GenerateItemForConfigKey));
        private static readonly MethodInfo _generateQuantityField = AccessTools.Method(typeof(SettingsDataFeedInjector), nameof(GenerateQuantityField));

        private static readonly Stack<float> _scrollAmounts = new();
        private static RootCategoryView? _cachedRootCategoryView = null;
        private static Slider<float>? _cachedScrollSlider = null;

        private static bool _colorXTemplateCleanupDone = false;
        private const string InjectedColorXTemplateName = "Injected DataFeedValueField<colorX>";

        public override int Priority => HarmonyLib.Priority.Normal;

        protected override bool AppliesTo(FallbackLocaleGenerationEvent eventData) => true;

        protected override IEnumerable<IFeaturePatch> GetFeaturePatches() => Enumerable.Empty<IFeaturePatch>();

        protected override Task Handle(FallbackLocaleGenerationEvent eventData)
        {
            foreach (var configSection in Mod.Loader.Config.Sections)
            {
                eventData.AddMessage($"{configSection.FullId}.Name", configSection.Name);

                foreach (var configKey in configSection.Keys)
                {
                    eventData.AddMessage(configKey.GetLocaleKey("Name"), configKey.Id);
                    eventData.AddMessage(configKey.GetLocaleKey("Description"), configKey.Description ?? "No Description");
                }
            }

            foreach (var mod in Mod.Loader.Mods)
            {
                var modNameKey = mod.GetLocaleKey("Name");

                eventData.AddMessage(modNameKey, mod.Title);
                eventData.AddMessage($"Settings.{mod.Id}.Breadcrumb", eventData.GetMessage(modNameKey));

                eventData.AddMessage(mod.GetLocaleKey("Description"), mod.Description);

                foreach (var configSection in mod.Config.Sections)
                {
                    eventData.AddMessage(configSection.GetLocaleKey("Name"), configSection.Name);

                    foreach (var configKey in configSection.Keys)
                    {
                        eventData.AddMessage(configKey.GetLocaleKey("Name"), configKey.Id);
                        eventData.AddMessage(configKey.GetLocaleKey("Description"), configKey.Description ?? "No Description");
                    }
                }

                foreach (var monkey in mod.Monkeys)
                {
                    eventData.AddMessage(monkey.GetLocaleKey("Name"), monkey.Name);
                    eventData.AddMessage(monkey.GetLocaleKey("Description"), "No Description");
                }

                foreach (var earlyMonkey in mod.EarlyMonkeys)
                {
                    eventData.AddMessage(earlyMonkey.GetLocaleKey("Name"), earlyMonkey.Name);
                    eventData.AddMessage(earlyMonkey.GetLocaleKey("Description"), "No Description");
                }
            }

            return Task.CompletedTask;
        }

        protected override bool OnEngineReady()
        {
            var monkeyLoaderCategory = new SettingCategoryInfo(OfficialAssets.Graphics.Icons.Dash.Tools, 255);
            monkeyLoaderCategory.InitKey("MonkeyLoader");

            Settings._categoryInfos.Add(monkeyLoaderCategory.Key, monkeyLoaderCategory);

            return base.OnEngineReady();
        }

        private static void EnsureColorXTemplate(DataFeedItemMapper mapper)
        {
            if (!_colorXTemplateCleanupDone)
            {
                Logger.Info(() => "Looking for previously injected colorX templates.");
                foreach (var mapping in mapper.Mappings.Where(mapping => mapping.MatchingType == typeof(DataFeedValueField<colorX>) && mapping.Template.Target != null && mapping.Template.Target.Slot.Name == InjectedColorXTemplateName).ToArray())
                {
                    mapping.Template.Target.Slot.Destroy();
                    mapper.Mappings.Remove(mapping);
                    Logger.Info(() => "Cleaned up a previously injected colorX template.");
                }
                _colorXTemplateCleanupDone = true;
            }
            if (!mapper.Mappings.Any(mapping => mapping.MatchingType == typeof(DataFeedValueField<colorX>) && mapping.Template.Target != null))
            {
                var templatesRoot = mapper.Slot.Parent?.FindChild("Templates");
                if (templatesRoot != null)
                {
                    bool changeIndex = false;
                    DataFeedItemMapper.ItemMapping mapping = mapper.Mappings.FirstOrDefault(mapping => mapping.MatchingType == typeof(DataFeedValueField<colorX>) && mapping.Template.Target == null);
                    if (mapping == null)
                    {
                        mapping = mapper.Mappings.Add();
                        mapping.MatchingType.Value = typeof(DataFeedValueField<colorX>);
                        changeIndex = true;
                    }

                    var template = templatesRoot.AddSlot(InjectedColorXTemplateName);
                    template.ActiveSelf = false;
                    template.PersistentSelf = false;
                    template.AttachComponent<LayoutElement>().MinHeight.Value = 96f;
                    var ui = new UIBuilder(template);
                    RadiantUI_Constants.SetupBaseStyle(ui);
                    ui.ForceNext = template.AttachComponent<RectTransform>();
                    ui.HorizontalLayout(11.78908f, 11.78908f);
                    var text = ui.Text("Label");
                    text.Size.Value = 24f;
                    text.HorizontalAlign.Value = TextHorizontalAlignment.Left;
                    ui.Style.MinHeight = 32f;
                    var field = template.AttachComponent<ValueField<colorX>>();
                    var editor = ui.ColorXMemberEditor(field.Value);
                    editor.Slot.GetComponentInChildren<VerticalLayout>().PaddingLeft.Value = 64f;
                    var feedValueFieldInterface = template.AttachComponent<FeedValueFieldInterface<colorX>>();
                    feedValueFieldInterface.ItemName.Target = text.Content;
                    feedValueFieldInterface.Value.Target = field.Value;

                    var innerInterfaceSlot = templatesRoot.FindChild("InnerContainerItem");
                    if (innerInterfaceSlot != null)
                    {
                        var innerInterface = innerInterfaceSlot.GetComponent<FeedItemInterface>();
                        feedValueFieldInterface.ParentContainer.Target = innerInterface;
                    }
                    else
                    {
                        Logger.Error(() => "InnerContainerItem slot is null in EnsureColorXTemplate!");
                    }

                    mapping.Template.Target = feedValueFieldInterface;

                    if (changeIndex)
                    {
                        // Move the new mapping above the previous last element (default DataFeedItem mapping) in the list
                        mapper.Mappings.MoveToIndex(mapper.Mappings.Count() - 1, mapper.Mappings.Count() - 2);
                    }

                    Logger.Info(() => $"Injected DataFeedValueField<colorX> template");
                }
                else
                {
                    Logger.Error(() => "Could not find Templates slot in EnsureColorXTemplate!");
                }
            }
            else
            {
                // This could cause some log spam
                Logger.Trace(() => "Existing DataFeedValueField<colorX> template found.");
            }
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateConfigAsync(IReadOnlyList<string> path, Config config)
        {
            var generateConfigButtons = false;

            foreach (var configSection in config.Sections.Where(section => !section.InternalAccessOnly))
            {
                var sectionGroup = new DataFeedGroup();
                sectionGroup.InitBase(configSection.Id, path, null, $"{configSection.FullId}.Name".AsLocaleKey());
                yield return sectionGroup;

                await foreach (var sectionItem in EnumerateConfigSectionAsync(path, configSection))
                {
                    generateConfigButtons = true;
                    yield return sectionItem;
                }
            }

            if (generateConfigButtons)
            {
                var saveConfigButton = new DataFeedCategory();
                saveConfigButton.InitBase(SaveConfig, path, null, Mod.GetLocaleString("SaveConfig"));
                yield return saveConfigButton;

                var resetConfigButton = new DataFeedCategory();
                resetConfigButton.InitBase(ResetConfig, path, null, Mod.GetLocaleString("ResetConfig"));
                yield return resetConfigButton;
            }
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateConfigSectionAsync(IReadOnlyList<string> path, ConfigSection configSection)
        {
            foreach (var configKey in configSection.Keys.Where(key => !key.InternalAccessOnly))
            {
                //if (setting is SettingIndicatorProperty)
                //{
                //    return (DataFeedItem)_generateIndicator.MakeGenericMethod(type).Invoke(null, new object[4] { identity, setting, path, grouping });
                //}

                if (configKey.ValueType == typeof(bool))
                {
                    yield return GenerateToggle(path, (IDefiningConfigKey<bool>)configKey);

                    continue;
                }

                if (configKey.ValueType.IsEnum)
                {
                    yield return (DataFeedItem)_generateEnumField
                        .MakeGenericMethod(configKey.ValueType)
                        .Invoke(null, new object[] { path, configKey });

                    continue;
                }

                yield return (DataFeedItem)_generateItemForConfigKeyMethod
                    .MakeGenericMethod(configKey.ValueType)
                    .Invoke(null, new object[] { path, configKey });
            }
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateModMetadataAsync(IReadOnlyList<string> path, Mod mod)
        {
            var modGroup = new DataFeedGroup();
            modGroup.InitBase("Metadata", path, null, Mod.GetLocaleString("Mod.Metadata"));
            yield return modGroup;

            var grouping = new[] { "Metadata" };

            var id = new DataFeedIndicator<string>();
            id.InitBase("Id", path, grouping, Mod.GetLocaleString("Mod.Id"));
            id.InitSetupValue(field => field.Value = mod.Id);
            yield return id;

            var version = new DataFeedIndicator<string>();
            version.InitBase("Version", path, grouping, Mod.GetLocaleString("Mod.Version"));
            version.InitSetupValue(field => field.Value = mod.Version.ToString());
            yield return version;

            var authors = new DataFeedIndicator<string>();
            authors.InitBase("Authors", path, grouping, Mod.GetLocaleString("Mod.Authors", ("count", mod.Authors.Count())));
            authors.InitSetupValue(field => field.Value = mod.Authors.Join());
            yield return authors;

            var project = new DataFeedIndicator<string>();
            project.InitBase("Project", path, grouping, Mod.GetLocaleString("Mod.Project"));
            project.InitSetupValue(field =>
            {
                if (mod.ProjectUrl is null)
                {
                    field.AssignLocaleString(Mod.GetLocaleString("Mod.Project.None"));
                    return;
                }

                field.Value = $"<u>{mod.ProjectUrl}</u>";
                var text = field.FindNearestParent<Text>();

                text.Slot.AttachComponent<Hyperlink>().URL.Value = mod.ProjectUrl;

                var drive = text.Slot.AttachComponent<Button>().ColorDrivers.Add();
                drive.ColorDrive.Target = text.Color;
            });
            yield return project;

            var description = new DataFeedIndicator<string>();
            description.InitBase("Description", path, grouping, Mod.GetLocaleString("Mod.Description"));
            description.InitSetupValue(field => field.AssignLocaleString(mod.GetLocaleString("Description")));
            yield return description;
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateModMonkeysAsync(IReadOnlyList<string> path, Mod mod)
        {
            await foreach (var feedItem in EnumerateMonkeysAsync(path, mod, Monkeys))
                yield return feedItem;

            await foreach (var feedItem in EnumerateMonkeysAsync(path, mod, EarlyMonkeys))
                yield return feedItem;
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateModsAsync(IReadOnlyList<string> path)
        {
            var modsGroup = new DataFeedGroup();
            modsGroup.InitBase("Mods.Group", path, null, Mod.GetLocaleString("Mods"));
            yield return modsGroup;

            var modsGrid = new DataFeedGrid();
            modsGrid.InitBase("Mods.Grid", path, new[] { "Mods.Group" }, Mod.GetLocaleString("Mods"));
            yield return modsGrid;

            var modsGrouping = new[] { "Mods.Group", "Mods.Grid" };

            foreach (var mod in Mod.Loader.RegularMods.OrderBy(GetLocalizedModName))
            {
                var modSubCategory = new DataFeedCategory();
                modSubCategory.InitBase(mod.Id, path, modsGrouping, mod.GetLocaleString("Name"));
                yield return modSubCategory;
            }

            var gamePacksGroup = new DataFeedGroup();
            gamePacksGroup.InitBase("GamePacks.Group", path, null, Mod.GetLocaleString("GamePacks"));
            yield return gamePacksGroup;

            var gamePacksGrid = new DataFeedGrid();
            gamePacksGrid.InitBase("GamePacks.Grid", path, new[] { "GamePacks.Group" }, Mod.GetLocaleString("GamePacks"));
            yield return gamePacksGrid;

            var gamePacksGrouping = new[] { "GamePacks.Group", "GamePacks.Grid" };

            foreach (var gamePack in Mod.Loader.GamePacks.OrderBy(GetLocalizedModName))
            {
                var gamePackCategory = new DataFeedCategory();
                gamePackCategory.InitBase(gamePack.Id, path, gamePacksGrouping, gamePack.GetLocaleString("Name"));
                yield return gamePackCategory;
            }

            var monkeyLoaderGroup = new DataFeedGroup();
            monkeyLoaderGroup.InitBase("MonkeyLoader", path, null, Mod.GetLocaleString("MonkeyLoader.Name"));
            yield return monkeyLoaderGroup;

            var monkeyLoaderGrouping = new[] { Mod.Loader.Id };

            var openMonkeyLoaderSettings = new DataFeedCategory();
            openMonkeyLoaderSettings.InitBase("MonkeyLoader.OpenMonkeyLoader", path, monkeyLoaderGrouping, Mod.GetLocaleString("OpenMonkeyLoader.Name"), Mod.GetLocaleString("OpenMonkeyLoader.Description"));
            openMonkeyLoaderSettings.SetOverrideSubpath("MonkeyLoader");
            yield return openMonkeyLoaderSettings;

            var monkeys = Mod.Loader.Mods.SelectMany(mod => mod.Monkeys);
            var monkeyCountIndicator = new DataFeedIndicator<string>();
            monkeyCountIndicator.InitBase("MonkeyLoader.MonkeyCount", path, monkeyLoaderGrouping, Mod.GetLocaleString("MonkeyLoader.MonkeyCount.Name"));
            monkeyCountIndicator.InitSetupValue(field => field.SetLocalized(Mod.GetLocaleString("MonkeyLoader.MonkeyCount.Value", ("available", monkeys.Count()), ("active", monkeys.Count(monkey => monkey.Enabled)))));
            yield return monkeyCountIndicator;

            var earlyMonkeys = Mod.Loader.Mods.SelectMany(mod => mod.EarlyMonkeys);
            var earlyMonkeyCountIndicator = new DataFeedIndicator<string>();
            earlyMonkeyCountIndicator.InitBase("MonkeyLoader.EarlyMonkeyCount", path, monkeyLoaderGrouping, Mod.GetLocaleString("MonkeyLoader.EarlyMonkeyCount.Name"));
            earlyMonkeyCountIndicator.InitSetupValue(field => field.SetLocalized(Mod.GetLocaleString("MonkeyLoader.EarlyMonkeyCount.Value", ("available", earlyMonkeys.Count()), ("active", earlyMonkeys.Count(monkey => monkey.Enabled)))));
            yield return earlyMonkeyCountIndicator;
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateModSettingsAsync(IReadOnlyList<string> path)
        {
            // path.Count >= 2 because otherwise other methods are called
            // Format: MonkeyLoader / modId / [page]
            if (!Mod.Loader.TryFindModById(path[1], out var mod))
            {
                Logger.Error(() => $"Tried to access non-existant mod's settings: {path[1]}");
                yield break;
            }

            if (path.Count == 2)
            {
                await foreach (var feedItem in EnumerateConfigAsync(path, mod.Config))
                    yield return feedItem;

                await foreach (var feedItem in EnumerateModMetadataAsync(path, mod))
                    yield return feedItem;

                await foreach (var feedItem in EnumerateModMonkeysAsync(path, mod))
                    yield return feedItem;

                yield break;
            }

            switch (path[2])
            {
                case ConfigSections:
                    await foreach (var feedItem in EnumerateConfigAsync(path, mod.Config))
                        yield return feedItem;

                    break;

                case MonkeyToggles:
                    await foreach (var feedItem in EnumerateModMonkeysAsync(path, mod))
                        yield return feedItem;

                    break;

                default:
                    Logger.Error(() => $"Tried to access non-existant mod settings page: {path[2]}");
                    break;
            }
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateMonkeyLoaderSettingsAsync(IReadOnlyList<string> path)
        {
            await foreach (var feedItem in EnumerateConfigAsync(path, Mod.Loader.Config))
                yield return feedItem;
        }

        private static async IAsyncEnumerable<DataFeedItem> EnumerateMonkeysAsync(IReadOnlyList<string> path, Mod mod, string monkeyType)
        {
            var monkeys = monkeyType switch
            {
                Monkeys => mod.Monkeys.ToArray(),
                EarlyMonkeys => mod.EarlyMonkeys.ToArray(),
                _ => Array.Empty<IMonkey>()
            };

            var group = new DataFeedGroup();
            group.InitBase(monkeyType, path, null, Mod.GetLocaleString($"{monkeyType}.Name"), Mod.GetLocaleString($"{monkeyType}.Description"));
            yield return group;

            var monkeysGrouping = new[] { monkeyType };

            var monkeyCount = new DataFeedIndicator<string>();
            monkeyCount.InitBase($"{monkeyType}.Count", path, monkeysGrouping, Mod.GetLocaleString($"{monkeyType}.Count.Name"), Mod.GetLocaleString($"{monkeyType}.Count.Description"));
            monkeyCount.InitSetupValue(field => field.Value = monkeys.Length.ToString());
            yield return monkeyCount;

            foreach (var monkey in monkeys)
            {
                var monkeyGroup = new DataFeedGroup();
                monkeyGroup.InitBase($"{monkey.Id}", path, monkeysGrouping, monkey.GetLocaleKey("Name").AsLocaleKey());
                yield return monkeyGroup;

                var monkeyGrouping = new[] { monkeyType, monkey.Id };

                if (monkey.CanBeDisabled)
                {
                    var toggle = new DataFeedToggle();
                    toggle.InitBase($"{monkey.Id}.Enabled", path, monkeyGrouping, Mod.GetLocaleString($"{monkeyType}.Enabled.Name"), Mod.GetLocaleString($"{monkeyType}.Enabled.Description"));
                    toggle.InitSetupValue(field => field.SyncWithConfigKey(mod.MonkeyToggles.GetToggle(monkey)));
                    yield return toggle;
                }
                else
                {
                    var enabledIndicator = new DataFeedIndicator<string>();
                    enabledIndicator.InitBase($"{monkey.Id}.Enabled", path, monkeyGrouping, Mod.GetLocaleString($"{monkeyType}.Enabled.Name"), Mod.GetLocaleString($"{monkeyType}.Enabled.Description"));
                    enabledIndicator.InitSetupValue(field => field.Value = "Always Enabled");
                    yield return enabledIndicator;
                }

                var descriptionIndicator = new DataFeedIndicator<string>();
                descriptionIndicator.InitBase($"{monkey.Id}.Description", path, monkeyGrouping, Mod.GetLocaleString("Monkeys.Description.Name"), Mod.GetLocaleString("Monkeys.Description.Description"));
                descriptionIndicator.InitSetupValue(field => field.AssignLocaleString(monkey.GetLocaleKey("Description").AsLocaleKey()));
                yield return descriptionIndicator;

                var typeIndicator = new DataFeedIndicator<string>();
                typeIndicator.InitBase($"{monkey.Id}.Type", path, monkeyGrouping, Mod.GetLocaleString("Monkeys.Type.Name"), Mod.GetLocaleString($"{monkeyType}.Type.Description"));
                typeIndicator.InitSetupValue(field => field.Value = monkey.Type.BaseType.CompactDescription());
                yield return typeIndicator;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(SettingsDataFeed.Enumerate))]
        private static bool EnumeratePrefix(SettingsDataFeed __instance, IReadOnlyList<string> path, ref IAsyncEnumerable<DataFeedItem> __result)
        {
            if (__instance.World.IsUserspace() && _cachedRootCategoryView.FilterWorldElement() == null)
            {
                _cachedRootCategoryView = __instance.Slot.GetComponent<RootCategoryView>();
                if (_cachedRootCategoryView != null)
                {
                    _cachedRootCategoryView.Path.ElementsAdded += OnElementsAdded;
                    _cachedRootCategoryView.Path.ElementsRemoved += OnElementsRemoved;
                    Logger.Debug(() => "Cached RootCategoryView and subscribed to events.");
                }
            }

            if (__instance.World.IsUserspace() && _cachedScrollSlider.FilterWorldElement() == null)
            {
                Slot settingsListSlot = __instance.Slot.FindChild(s => s.Name == "Settings List", maxDepth: 2);
                if (settingsListSlot != null)
                {
                    Slot scrollBarSlot = settingsListSlot.FindChild(s => s.Name == "Scroll Bar", maxDepth: 2);
                    if (scrollBarSlot != null)
                    {
                        var slider = scrollBarSlot.GetComponentInChildren<Slider<float>>();
                        if (slider != null)
                        {
                            _cachedScrollSlider = slider;
                            Logger.Debug(() => "Cached settings scroll slider.");
                        }
                    }
                }
            }

            if (path.Count == 0 || path[0] != "MonkeyLoader")
                return true;

            if (!__instance.World.IsUserspace())
            {
                __result = WorldNotUserspaceWarning(path);
                return false;
            }

            switch (path.Last())
            {
                case SaveConfig:
                    SaveModOrLoaderConfig(path[1]);

                    _cachedRootCategoryView?.RunSynchronously(() => MoveUpFromCategory(_cachedRootCategoryView, SaveConfig));

                    __result = YieldBreakAsync();
                    return false;

                case ResetConfig:
                    ResetModOrLoaderConfig(path[1]);

                    _cachedRootCategoryView?.RunSynchronously(() => MoveUpFromCategory(_cachedRootCategoryView, ResetConfig));

                    __result = YieldBreakAsync();
                    return false;

                default:
                    break;
            }

            var mapper = __instance.Slot.GetComponent((DataFeedItemMapper m) => m.Mappings.Count > 1);
            mapper?.RunSynchronously(() => EnsureColorXTemplate(mapper));

            __result = path.Count switch
            {
                1 => EnumerateModsAsync(path),
                2 => path[1] == "MonkeyLoader" ? EnumerateMonkeyLoaderSettingsAsync(path) : EnumerateModSettingsAsync(path),
                _ => EnumerateModSettingsAsync(path),
            };

            return false;
        }

        private static DataFeedEnum<T> GenerateEnumField<T>(IReadOnlyList<string> path, IDefiningConfigKey<T> configKey)
            where T : Enum
        {
            var enumField = new DataFeedEnum<T>();
            InitBase(enumField, path, configKey);
            enumField.InitSetupValue(field => field.SyncWithConfigKey(configKey, ConfigKeyChangeLabel));

            return enumField;
        }

        private static DataFeedIndicator<T> GenerateIndicator<T>(IReadOnlyList<string> path, IDefiningConfigKey<T> configKey)
        {
            var indicator = new DataFeedIndicator<T>();
            InitBase(indicator, path, configKey);
            indicator.InitSetupValue(field => field.SyncWithConfigKey(configKey, ConfigKeyChangeLabel));

            return indicator;
        }

        private static DataFeedItem GenerateItemForConfigKey<T>(IReadOnlyList<string> path, IEntity<IDefiningConfigKey<T>> configKey)
        {
            if (configKey.Components.TryGet<IConfigKeyRange<T>>(out var range))
            {
                if (configKey.Components.TryGet<IConfigKeyQuantity<T>>(out var quantity))
                {
                    return (DataFeedItem)_generateQuantityField
                        .MakeGenericMethod(configKey.Self.ValueType, quantity.QuantityType)
                        .Invoke(null, new object[] { path, configKey.Self, quantity });
                }

                return GenerateSlider(path, configKey.Self, range);
            }

            return GenerateValueField(path, configKey.Self);
        }

        private static DataFeedQuantityField<TQuantity, T> GenerateQuantityField<T, TQuantity>(IReadOnlyList<string> path, IDefiningConfigKey<T> configKey, IConfigKeyQuantity<T> quantity)
            where TQuantity : unmanaged, IQuantity<TQuantity>
        {
            var quantityField = new DataFeedQuantityField<TQuantity, T>();
            InitBase(quantityField, path, configKey);
            quantityField.InitUnitConfiguration(quantity.DefaultConfiguration, quantity.ImperialConfiguration);
            quantityField.InitSetup(quantityField => quantityField.SyncWithConfigKey(configKey, ConfigKeyChangeLabel), quantity.Min, quantity.Max);

            return quantityField;
        }

        private static DataFeedSlider<T> GenerateSlider<T>(IReadOnlyList<string> path, IDefiningConfigKey<T> configKey, IConfigKeyRange<T> range)
        {
            var slider = new DataFeedSlider<T>();
            InitBase(slider, path, configKey);
            slider.InitSetup(field => field.SyncWithConfigKey(configKey, ConfigKeyChangeLabel), range.Min, range.Max);

            //if (!string.IsNullOrWhiteSpace(configKey.TextFormat))
            //    slider.InitFormatting(configKey.TextFormat);

            return slider;
        }

        private static DataFeedToggle GenerateToggle(IReadOnlyList<string> path, IDefiningConfigKey<bool> configKey)
        {
            var toggle = new DataFeedToggle();
            InitBase(toggle, path, configKey);
            toggle.InitSetupValue(field => field.SyncWithConfigKey(configKey, ConfigKeyChangeLabel));

            return toggle;
        }

        private static DataFeedValueField<T> GenerateValueField<T>(IReadOnlyList<string> path, IDefiningConfigKey<T> configKey)
        {
            var valueField = new DataFeedValueField<T>();
            InitBase(valueField, path, configKey);
            valueField.InitSetupValue(field => field.SyncWithConfigKey(configKey, ConfigKeyChangeLabel));

            return valueField;
        }

        private static string GetLocalizedModName(Mod mod)
            => mod.GetLocaleString("Name").Format()!;

        private static void InitBase(DataFeedItem item, IReadOnlyList<string> path, IDefiningConfigKey configKey)
            => item.InitBase(configKey.FullId, path, new[] { configKey.Section.Id },
                $"{configKey.FullId}.Name".AsLocaleKey(), $"{configKey.FullId}.Description".AsLocaleKey());

        private static void MoveUpFromCategory(RootCategoryView rootCategoryView, string category)
        {
            if (rootCategoryView.FilterWorldElement() != null && rootCategoryView.Path.Last() == category)
                rootCategoryView.MoveUpInCategory();
        }

        private static void OnElementsAdded(SyncElementList<Sync<string>> list, int start, int count)
        {
            Logger.Trace(() => $"OnElementsAdded. start: {start} count: {count}");

            if (_cachedScrollSlider.FilterWorldElement() != null)
            {
                _scrollAmounts.Push(_cachedScrollSlider!.Value.Value);
                Logger.Trace(() => $"Pushed value {_cachedScrollSlider!.Value.Value}. _scrollAmounts count: {_scrollAmounts.Count}");
            }
        }

        private static void OnElementsRemoved(SyncElementList<Sync<string>> list, int start, int count)
        {
            Logger.Trace(() => $"OnElementsRemoved. start: {start} count: {count}");

            if (start == 0)
            {
                _scrollAmounts.Clear();
                Logger.Trace(() => $"Cleared _scrollAmounts.");
                return;
            }

            var poppedValue = 0f;

            for (var i = 0; i < count; i++)
            {
                if (_scrollAmounts.Count > 0)
                {
                    poppedValue = _scrollAmounts.Pop();
                    Logger.Trace(() => $"Popped value {poppedValue}. _scrollAmounts count: {_scrollAmounts.Count}");
                }
            }

            if (_cachedScrollSlider.FilterWorldElement() != null)
            {
                _cachedScrollSlider!.RunInUpdates(3, () =>
                {
                    if (_cachedScrollSlider.FilterWorldElement() != null)
                    {
                        _cachedScrollSlider.Value.Value = poppedValue;
                        Logger.Debug(() => $"Set scroll slider to value {poppedValue}");
                    }
                });
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(SettingsDataFeed.PathSegmentName))]
        private static bool PathSegmentNamePrefix(string pathSegment, int depth, ref LocaleString __result)
        {
            __result = depth switch
            {
                1 => $"Settings.Category.{pathSegment}".AsLocaleKey(),
                _ => $"Settings.{pathSegment}.Breadcrumb".AsLocaleKey()
            };

            return false;
        }

        private static void ResetModOrLoaderConfig(string modOrLoaderId)
        {
            if (modOrLoaderId == Mod.Loader.Id)
            {
                Logger.Info(() => $"Resetting config to default for loader: {modOrLoaderId}");
                foreach (var key in Mod.Loader.Config.ConfigurationItemDefinitions)
                {
                    key.TryComputeDefault(out var defaultValue);
                    key.SetValue(defaultValue, "Default");
                }
            }
            else
            {
                if (!Mod.Loader.TryFindModById(modOrLoaderId, out var mod))
                {
                    Logger.Error(() => $"Tried to reset config to default for non-existent mod: {modOrLoaderId}");
                    return;
                }
                Logger.Info(() => $"Resetting config to default for mod: {modOrLoaderId}");
                foreach (var key in mod.Config.ConfigurationItemDefinitions)
                {
                    key.TryComputeDefault(out var defaultValue);
                    key.SetValue(defaultValue, "Default");
                }
            }
        }

        private static void SaveModOrLoaderConfig(string modOrLoaderId)
        {
            if (modOrLoaderId == Mod.Loader.Id)
            {
                Logger.Info(() => $"Saving config for loader: {modOrLoaderId}");
                Mod.Loader.Config.Save();
            }
            else
            {
                if (!Mod.Loader.TryFindModById(modOrLoaderId, out var mod))
                {
                    Logger.Error(() => $"Tried to save config for non-existent mod: {modOrLoaderId}");
                    return;
                }
                Logger.Info(() => $"Saving config for mod: {modOrLoaderId}");
                mod.Config.Save();
            }
        }

        private static async IAsyncEnumerable<DataFeedItem> WorldNotUserspaceWarning(IReadOnlyList<string> path)
        {
            var warning = new DataFeedIndicator<string>();
            warning.InitBase("Information", path, null, Mod.GetLocaleString("Information"));
            warning.InitSetupValue(field => field.AssignLocaleString(Mod.GetLocaleKey("WorldNotUserspace").AsLocaleKey()));
            yield return warning;
        }

        private static async IAsyncEnumerable<DataFeedItem> YieldBreakAsync()
        {
            yield break;
        }
    }
}