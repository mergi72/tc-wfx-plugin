using System.Runtime.InteropServices;
using System.Text;

namespace TcWfxPlugin.Wfx;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public bool TryRead(string target, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (!CredReadW(target, CredTypeGeneric, 0, out var credPtr) || credPtr == nint.Zero)
        {
            return false;
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIALW>(credPtr);
            username = cred.UserName ?? string.Empty;

            if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != nint.Zero)
            {
                var blob = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
                password = Encoding.Unicode.GetString(blob).TrimEnd('\0');
            }

            return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public void Save(string target, string username, string password)
    {
        var secretBytes = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, blobPtr, secretBytes.Length);

            var credential = new CREDENTIALW
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = username,
            };

            _ = CredWriteW(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint reservedFlag, out nint credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW([In] ref CREDENTIALW userCredential, [In] uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] nint cred);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIALW
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}