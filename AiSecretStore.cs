using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace YeShunguangPet;

public sealed class AiSecretStore
{
    public string FilePath { get; }
    public bool HasKey => File.Exists(FilePath);

    public AiSecretStore(string? filePath = null)
        => FilePath = filePath ?? Path.Combine(PetSettings.SettingsDirectory, "ai-secret.bin");

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { Delete(); return; }
        var protectedBytes = Protect(Encoding.UTF8.GetBytes(apiKey.Trim()));
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, protectedBytes); File.Move(temporary, FilePath, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(FilePath);
            return Encoding.UTF8.GetString(Unprotect(protectedBytes));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("AI API Key 无法解密，请重新输入。", ex);
        }
    }

    public void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }
    private const uint CryptProtectUiForbidden = 0x1;

    private static byte[] Protect(byte[] value)
    {
        var input = Alloc(value); var output = new DataBlob();
        try
        {
            if (!CryptProtectData(ref input, "YeShunguangPet AI Key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Free(input); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }

    private static byte[] Unprotect(byte[] value)
    {
        var input = Alloc(value); var output = new DataBlob(); var description = IntPtr.Zero;
        try
        {
            if (!CryptUnprotectData(ref input, out description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Free(input); if (description != IntPtr.Zero) LocalFree(description); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }

    private static DataBlob Alloc(byte[] value)
    {
        var data = Marshal.AllocHGlobal(value.Length); Marshal.Copy(value, 0, data, value.Length); return new DataBlob { Length = value.Length, Data = data };
    }
    private static void Free(DataBlob blob) { if (blob.Data != IntPtr.Zero) Marshal.FreeHGlobal(blob.Data); }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, out IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, ref DataBlob dataOut);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
