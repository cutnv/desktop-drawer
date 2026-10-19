using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DesktopDrawer
{
    internal sealed class DesktopPin
    {
        private const string Header = "DesktopDrawer.Pin.v1";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(Header);
        private readonly string path;
        internal DesktopPin(string file) { path = file; }
        internal bool IsConfigured { get { return File.Exists(path); } }
        internal static bool IsValid(string pin)
        {
            if (pin == null || pin.Length != 6) return false;
            foreach (char digit in pin) if (digit < '0' || digit > '9') return false;
            return true;
        }
        private static byte[] Derive(string pin, byte[] salt)
        {
            using (var derivation = new Rfc2898DeriveBytes(pin, salt, 210000, HashAlgorithmName.SHA256)) return derivation.GetBytes(32);
        }
        internal void Save(string pin)
        {
            if (!IsValid(pin)) throw new ArgumentException("PIN 必须是 6 位数字。");
            byte[] salt = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(salt);
            byte[] hash = Derive(pin, salt), payload = new byte[48];
            Buffer.BlockCopy(salt, 0, payload, 0, 16); Buffer.BlockCopy(hash, 0, payload, 16, 32);
            try
            {
                byte[] encrypted = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);
                Files.WriteRecord(path, new[] { Header, Convert.ToBase64String(encrypted) });
            }
            finally { Array.Clear(hash, 0, hash.Length); Array.Clear(payload, 0, payload.Length); }
        }
        private byte[] Read()
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length != 2 || lines[0] != Header) throw new IOException("桌面锁 PIN 记录损坏。");
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(lines[1]), Entropy, DataProtectionScope.CurrentUser);
            if (data.Length != 48) throw new IOException("桌面锁 PIN 记录无效。");
            return data;
        }
        internal void CheckReadable() { byte[] data = Read(); Array.Clear(data, 0, data.Length); }
        internal bool Verify(string pin)
        {
            if (!IsValid(pin)) return false;
            byte[] data = Read(), salt = new byte[16];
            Buffer.BlockCopy(data, 0, salt, 0, 16);
            byte[] actual = Derive(pin, salt);
            try
            {
                int difference = 0;
                for (int i = 0; i < actual.Length; i++) difference |= actual[i] ^ data[i + 16];
                return difference == 0;
            }
            finally { Array.Clear(data, 0, data.Length); Array.Clear(actual, 0, actual.Length); }
        }
    }
}
