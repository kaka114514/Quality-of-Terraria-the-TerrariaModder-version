using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Terraria;
using TerrariaModder.Core.Config;

namespace QualityOfTerraria
{
    internal static class ConfigProfiles
    {
        private const int FormatVersion = 1;
        private static readonly string[] ReservedNames = { "默认", "上一次" };
        private static string _path;
        private static List<Profile> _profiles = new List<Profile>();
        private static string _selectedName = "默认";

        internal static IReadOnlyList<string> Names => _profiles.Select(p => p.Name).ToArray();
        internal static string SelectedName => _selectedName;

        internal static void Initialize()
        {
            _path = null;
            _profiles = new List<Profile>();
            _selectedName = "默认";
            if (Mod.Config == null || string.IsNullOrEmpty(Mod.Config.FilePath)) return;
            string directory = Path.GetDirectoryName(Mod.Config.FilePath);
            if (string.IsNullOrEmpty(directory)) return;
            _path = Path.Combine(directory, "quality-of-terraria.profiles.json");
            try
            {
                if (File.Exists(_path))
                {
                    var file = ReadFile(_path);
                    if (file != null && file.Version == FormatVersion && file.Profiles != null)
                        _profiles = file.Profiles.Where(IsValidProfile).Select(Clone).ToList();
                }
            }
            catch (Exception error)
            {
                Mod.LogError("读取配置档案失败", error);
            }
            EnsureBuiltIns();
            SaveFile();
        }

        internal static void Reset()
        {
            _path = null;
            _profiles = new List<Profile>();
            _selectedName = "默认";
        }

        internal static bool Select(string name)
        {
            if (_profiles.Any(p => p.Name == name)) { _selectedName = name; return true; }
            return false;
        }

        internal static bool SaveCurrentAsNew(out string name, out string error)
        {
            name = null; error = null;
            if (!TryCapture(out Dictionary<string, string> values, out error)) return false;
            int index = 1;
            string candidate;
            do { candidate = "配置" + index++; } while (_profiles.Any(p => p.Name == candidate));
            name = candidate;
            string previousSelected = _selectedName;
            _profiles.Add(new Profile { Name = name, Values = values });
            _selectedName = name;
            if (!SaveFile()) { _profiles.RemoveAt(_profiles.Count - 1); _selectedName = previousSelected; error = "配置档案保存失败"; return false; }
            return true;
        }

        internal static bool SaveLast()
        {
            if (!TryCapture(out Dictionary<string, string> values, out _)) return false;
            var last = _profiles.FirstOrDefault(p => p.Name == "上一次");
            if (last == null) _profiles.Insert(1, last = new Profile { Name = "上一次" });
            var previous = last.Values;
            last.Values = values;
            if (SaveFile()) return true;
            last.Values = previous;
            return false;
        }

        internal static bool ApplySelected(out string message)
        {
            message = null;
            var profile = _profiles.FirstOrDefault(p => p.Name == _selectedName);
            if (profile == null) { message = "配置档案不存在"; return false; }
            var snapshot = new Dictionary<string, string>(profile.Values, StringComparer.Ordinal);
            if (!SaveLast()) { message = "无法保存当前设置到“上一次”"; return false; }
            return Apply(snapshot, out message);
        }

        internal static bool RenameSelected(string name, out string error)
        {
            error = null;
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 24 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            { error = "配置名称无效"; return false; }
            if (ReservedNames.Contains(_selectedName)) { error = "默认配置和上一次配置不可重命名"; return false; }
            if (_profiles.Any(p => p.Name == name)) { error = "配置名称已存在"; return false; }
            var profile = _profiles.FirstOrDefault(p => p.Name == _selectedName);
            if (profile == null) { error = "配置档案不存在"; return false; }
            string previousName = profile.Name;
            profile.Name = name; _selectedName = name;
            if (!SaveFile()) { profile.Name = previousName; _selectedName = previousName; error = "配置档案保存失败"; return false; }
            return true;
        }

        internal static bool DeleteSelected(out string error)
        {
            error = null;
            if (ReservedNames.Contains(_selectedName)) { error = "默认配置和上一次配置不可删除"; return false; }
            int index = _profiles.FindIndex(p => p.Name == _selectedName);
            if (index < 0) { error = "配置档案不存在"; return false; }
            Profile removed = _profiles[index];
            _profiles.RemoveAt(index); _selectedName = "默认";
            if (!SaveFile()) { _profiles.Insert(index, removed); _selectedName = removed.Name; error = "配置档案保存失败"; return false; }
            return true;
        }

        private static bool Apply(Dictionary<string, string> values, out string message)
        {
            message = null;
            var metadata = GetMetadata().ToDictionary(p => p.Key, StringComparer.Ordinal);
            var changes = new List<Tuple<ConfigPropertyMeta, object>>();
            var previous = new List<Tuple<ConfigPropertyMeta, object>>();
            var skipped = new List<string>();
            foreach (var pair in values)
            {
                ConfigPropertyMeta property;
                if (!metadata.TryGetValue(pair.Key, out property)) continue;
                if (property.Scope == ConfigScope.Server && !CanApplyServerSettings()) { skipped.Add(property.Label); continue; }
                try { changes.Add(Tuple.Create(property, ParseValue(property.PropertyType, pair.Value))); previous.Add(Tuple.Create(property, property.GetValue(Mod.Config))); }
                catch { message = "配置档案包含无效值：" + property.Label; return false; }
            }
            try
            {
                foreach (var change in changes) change.Item1.SetValue(Mod.Config, change.Item2);
                if (ConfigManager.GetCurrentScope() == ConfigScope.Client)
                {
                    string saveError;
                    if (!Networking.SettingsSync.SaveVerified(Mod.Config, out saveError)) throw new IOException(saveError);
                }
                if (Main.netMode == 0)
                {
                    string worldError;
                    if (!Networking.LocalWorldSettings.Save(out worldError)) throw new IOException(worldError);
                }
            }
            catch (Exception error)
            {
                foreach (var change in previous) change.Item1.SetValue(Mod.Config, change.Item2);
                Mod.LogError("应用配置档案失败", error);
                message = "配置档案应用失败";
                return false;
            }
            message = "已应用配置：" + _selectedName;
            if (skipped.Count > 0) message += "；未应用服务器设置（需要房主或服务器确认）";
            return true;
        }

        private static bool CanApplyServerSettings()
        {
            return Main.netMode == 0 || Terraria.Netplay.IsHostAndPlay;
        }

        private static IEnumerable<ConfigPropertyMeta> GetMetadata()
        {
            return Mod.Config.GetPropertyMetadata().Where(p => p.Property.DeclaringType == typeof(QualityOfTerrariaConfig)
                && p.Property.CanRead && p.Property.CanWrite && !p.RestartRequired && IsSupportedType(p.PropertyType)
                && Attribute.IsDefined(p.Property, typeof(LabelAttribute))
                && (Attribute.IsDefined(p.Property, typeof(ClientAttribute)) || Attribute.IsDefined(p.Property, typeof(ServerAttribute))));
        }

        private static bool IsSupportedType(Type type)
        {
            return type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(int[]);
        }

        private static bool TryCapture(out Dictionary<string, string> values, out string error)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal); error = null;
            if (Mod.Config == null) { error = "配置尚未加载"; return false; }
            foreach (var property in GetMetadata())
            {
                try { values[property.Key] = FormatValue(property.PropertyType, property.GetValue(Mod.Config)); }
                catch { error = "读取配置失败：" + property.Label; return false; }
            }
            return true;
        }

        private static string FormatValue(Type type, object value)
        {
            if (type == typeof(int[])) return string.Join(",", ((int[])value ?? new int[0]).Select(v => v.ToString(CultureInfo.InvariantCulture)));
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static object ParseValue(Type type, string value)
        {
            if (type == typeof(bool)) return bool.Parse(value);
            if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (type == typeof(int[]))
                return string.IsNullOrEmpty(value) ? new int[0] : value.Split(',').Where(s => s.Length > 0).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            throw new InvalidDataException("不支持的配置类型");
        }

        private static void EnsureBuiltIns()
        {
            var current = new Dictionary<string, string>(StringComparer.Ordinal);
            string error;
            if (!TryCapture(out current, out error)) return;
            var defaults = _profiles.FirstOrDefault(p => p.Name == "默认");
            if (defaults == null) _profiles.Insert(0, new Profile { Name = "默认", Values = new Dictionary<string, string>(current, StringComparer.Ordinal) });
            var last = _profiles.FirstOrDefault(p => p.Name == "上一次");
            if (last == null) _profiles.Insert(Math.Min(1, _profiles.Count), new Profile { Name = "上一次", Values = new Dictionary<string, string>(current, StringComparer.Ordinal) });
            _profiles = _profiles.Where((p, i) => i < 2 || !ReservedNames.Contains(p.Name)).GroupBy(p => p.Name).Select(g => g.First()).ToList();
            if (_profiles.Count == 0) _profiles.Add(new Profile { Name = "默认", Values = current });
        }

        private static bool IsValidProfile(Profile profile)
        {
            return profile != null && !string.IsNullOrWhiteSpace(profile.Name) && profile.Values != null && profile.Values.Count > 0;
        }

        private static Profile Clone(Profile profile)
        {
            return new Profile { Name = profile.Name, Values = new Dictionary<string, string>(profile.Values, StringComparer.Ordinal) };
        }

        private static FileModel ReadFile(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(FileModel));
            using (var stream = File.OpenRead(path)) return (FileModel)serializer.ReadObject(stream);
        }

        private static bool SaveFile()
        {
            if (string.IsNullOrEmpty(_path)) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var serializer = new DataContractJsonSerializer(typeof(FileModel));
                using (var stream = File.Create(_path)) serializer.WriteObject(stream, new FileModel { Version = FormatVersion, Profiles = _profiles });
                return true;
            }
            catch (Exception error) { Mod.LogError("保存配置档案失败", error); return false; }
        }

        [DataContract]
        private sealed class FileModel
        {
            [DataMember(Order = 0)] public int Version;
            [DataMember(Order = 1)] public List<Profile> Profiles;
        }

        [DataContract]
        private sealed class Profile
        {
            [DataMember(Order = 0)] public string Name;
            [DataMember(Order = 1)] public Dictionary<string, string> Values;
        }
    }
}
