// Addressables EDITOR API stubs (package not downloadable here). Only members our Editor scripts use; names and
// signatures follow com.unity.addressables 2.x.
#pragma warning disable CS8618
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.AddressableAssets.Settings
{
    public class AddressableAssetGroupSchema : ScriptableObject { }
    public class ProfileValueReference { public bool SetVariableByName(AddressableAssetSettings settings, string profileVariableName) => true; }
    public class AddressableAssetEntry
    {
        public string address { get; set; }
        public AddressableAssetGroup parentGroup { get; }
        public bool SetLabel(string label, bool enable, bool force = false, bool postEvent = true) => true;
    }
    public class AddressableAssetGroup : ScriptableObject
    {
        public string Name => name;
        public ICollection<AddressableAssetEntry> entries => null;
        public TSchema GetSchema<TSchema>() where TSchema : AddressableAssetGroupSchema => null;
        public TSchema AddSchema<TSchema>(bool postEvent = true) where TSchema : AddressableAssetGroupSchema => null;
    }
    public class AddressablesPlayerBuildResult { public string Error; public double Duration; }
    public class AddressableAssetSettings : ScriptableObject
    {
        public const string kLocalBuildPath = "Local.BuildPath";
        public const string kLocalLoadPath = "Local.LoadPath";
        public enum ModificationEvent { BatchModification }
        public List<AddressableAssetGroup> groups => null;
        public AddressableAssetGroup FindGroup(string name) => null;
        public AddressableAssetGroup CreateGroup(string groupName, bool setAsDefaultGroup, bool readOnly, bool postEvent, List<AddressableAssetGroupSchema> schemasToCopy, params Type[] types) => null;
        public AddressableAssetEntry CreateOrMoveEntry(string guid, AddressableAssetGroup targetParent, bool readOnly = false, bool postEvent = true) => null;
        public AddressableAssetEntry FindAssetEntry(string guid) => null;
        public void SetDirty(ModificationEvent modificationEvent, object eventData, bool postEvent, bool settingsModified = false) { }
        public static void CleanPlayerContent(object builder = null) { }
        public static void BuildPlayerContent(out AddressablesPlayerBuildResult result) { result = null; }
    }
}
namespace UnityEditor.AddressableAssets.Settings.GroupSchemas
{
    using UnityEditor.AddressableAssets.Settings;
    public class BundledAssetGroupSchema : AddressableAssetGroupSchema
    {
        public enum BundlePackingMode { PackTogether, PackSeparately, PackTogetherByLabel }
        public enum BundleCompressionMode { Uncompressed, LZ4, LZMA }
        public BundlePackingMode BundleMode { get; set; }
        public BundleCompressionMode Compression { get; set; }
        public ProfileValueReference BuildPath => null;
        public ProfileValueReference LoadPath => null;
    }
    public class ContentUpdateGroupSchema : AddressableAssetGroupSchema { }
}
namespace UnityEditor.AddressableAssets
{
    using UnityEditor.AddressableAssets.Settings;
    public static class AddressableAssetSettingsDefaultObject
    {
        public static AddressableAssetSettings Settings { get; set; }
        public static AddressableAssetSettings GetSettings(bool create) => null;
    }
}
