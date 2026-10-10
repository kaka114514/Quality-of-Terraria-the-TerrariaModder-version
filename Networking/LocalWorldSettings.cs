using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerrariaModder.Core.Config;

namespace QualityOfTerraria.Networking
{
    // Core 的客户端文件只保存 Client 项，单机世界项另行保存，不能用联机服务器值覆盖它。
    internal static class LocalWorldSettings
    {
        private static string _path;
        private static bool _canSave;
        private static Dictionary<string, object> _values;
        private static IEnumerable<ConfigPropertyMeta> Properties => Mod.Config.GetPropertyMetadata().Where(p => p.Scope == ConfigScope.Server);

        internal static void Initialize()
        {
            _path = null; _canSave = false; _values = null;
            if (Mod.Config == null || ConfigManager.GetCurrentScope() != ConfigScope.Client || string.IsNullOrEmpty(Mod.Config.FilePath)) return;
            _path = Mod.Config.FilePath + ".world-settings.dat";
            _canSave = !File.Exists(_path) && !File.Exists(_path + ".bak");
            Dictionary<string, object> loaded;
            if (StoragePersistence.Read(_path, Read, out loaded)) { _values = loaded; _canSave = true; }
            else if (_canSave)
            {
                _values = Properties.ToDictionary(p => p.Key, p => p.GetValue(Mod.Config));
                // 升级前部分世界项标记为 Client，首次导入保留原选择。
                if (File.Exists(Mod.Config.FilePath))
                {
                    try
                    {
                        var legacy = ConfigManager.ParseJsonRaw(File.ReadAllText(Mod.Config.FilePath));
                        foreach (var property in Properties)
                        {
                            object value;
                            if (legacy.TryGetValue(property.Key, out value) && value != null)
                                _values[property.Key] = Convert.ChangeType(value, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    catch (Exception error) { Mod.LogError("读取单机旧世界设置失败", error); }
                }
            }
            Restore();
        }

        private static Dictionary<string, object> Read(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1) throw new InvalidDataException("单机世界配置格式不支持");
            int count = reader.ReadInt32();
            if (count < 0 || count > 256) throw new InvalidDataException("单机世界配置数量不合法");
            var result = new Dictionary<string, object>();
            var properties = Properties.ToDictionary(p => p.Key);
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                object value;
                switch (reader.ReadByte())
                {
                    case 1: value = reader.ReadBoolean(); break;
                    case 2: value = reader.ReadInt32(); break;
                    case 3: value = reader.ReadSingle(); break;
                    case 4: value = reader.ReadDouble(); break;
                    default: throw new InvalidDataException("单机世界配置类型不合法");
                }
                if (result.ContainsKey(key)) throw new InvalidDataException("单机世界配置包含重复键");
                ConfigPropertyMeta property;
                if (properties.TryGetValue(key, out property))
                {
                    if (value.GetType() != property.PropertyType) throw new InvalidDataException("单机世界配置类型不匹配");
                    result.Add(key, value);
                }
            }
            return result;
        }

        internal static void Restore()
        {
            if (_values == null || Mod.Config == null) return;
            foreach (var property in Properties)
            {
                object value;
                if (_values.TryGetValue(property.Key, out value)) property.SetValue(Mod.Config, value);
            }
        }

        internal static bool Save(out string error)
        {
            error = "";
            if (_path == null || !_canSave) { error = "单机世界配置不可保存"; return false; }
            var values = Properties.ToDictionary(p => p.Key, p => p.GetValue(Mod.Config));
            try
            {
                StoragePersistence.Write(_path, writer =>
                {
                    writer.Write(1); writer.Write(values.Count);
                    foreach (var pair in values)
                    {
                        writer.Write(pair.Key);
                        if (pair.Value is bool) { writer.Write((byte)1); writer.Write((bool)pair.Value); }
                        else if (pair.Value is int) { writer.Write((byte)2); writer.Write((int)pair.Value); }
                        else if (pair.Value is float) { writer.Write((byte)3); writer.Write((float)pair.Value); }
                        else if (pair.Value is double) { writer.Write((byte)4); writer.Write((double)pair.Value); }
                        else throw new InvalidDataException("单机世界配置类型不支持");
                    }
                });
                _values = values;
                return true;
            }
            catch (Exception exception) { error = "单机世界配置保存失败"; Mod.LogError(error, exception); return false; }
        }
    }
}
