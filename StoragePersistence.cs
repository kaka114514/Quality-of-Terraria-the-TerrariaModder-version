using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QualityOfTerraria
{
    internal static class StoragePersistence
    {
        internal static string Identity(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }

        internal static void Write(string path, Action<BinaryWriter> write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    {
                        write(writer);
                        writer.Flush();
                    }
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        internal static bool Read<T>(string path, Func<BinaryReader, T> read, out T data)
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    using (var stream = File.OpenRead(candidate))
                    using (var reader = new BinaryReader(stream))
                    {
                        data = read(reader);
                        if (stream.Position != stream.Length) throw new InvalidDataException("存档包含多余数据");
                    }
                    if (candidate != path)
                    {
                        // 不让损坏的主文件覆盖有效备份。
                        string temp = path + ".recovery";
                        File.Copy(candidate, temp, true);
                        try
                        {
                            if (File.Exists(path)) File.Replace(temp, path, null);
                            else File.Move(temp, path);
                        }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                    return true;
                }
                catch (Exception error) { Mod.LogError("读取存储文件失败：" + candidate, error); }
            }
            data = default(T);
            return false;
        }
    }
}
