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
                    // 只刷新到操作系统缓存：日志用于抵御游戏/模组崩溃，进程崩溃不会丢页缓存，
                    // 而 Flush(true) 会强制物理落盘，在主线程上每次物品操作都造成可感知的卡顿。
                    stream.Flush();
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // 高频短记录（物品操作日志）用就地覆盖写：本机实测平均 0.22ms，
        // 而"临时文件 + File.Replace"平均 2.6ms、尖峰可达 11ms，主线程上每次操作都会卡一下。
        // 记录在请求发出前写入，写入被中断说明请求还没发出去，因此这里不需要备份文件。
        internal static void WriteInPlace(string path, Action<BinaryWriter> write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    write(writer);
                    writer.Flush();
                }
                stream.Flush();
            }
        }

        // 就地写入的读取端：只认主文件；记录损坏按"没有待确认操作"处理，不回退到可能过期的备份。
        internal static bool ReadInPlace<T>(string path, Func<BinaryReader, T> read, out T data)
        {
            data = default(T);
            if (!File.Exists(path)) return true;
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    data = read(reader);
                    if (stream.Position != stream.Length) throw new InvalidDataException("记录包含多余数据");
                }
                return true;
            }
            catch (Exception)
            {
                data = default(T);
                return false;
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
