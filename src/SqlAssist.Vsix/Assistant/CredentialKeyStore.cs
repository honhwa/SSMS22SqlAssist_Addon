using System;
using System.Runtime.InteropServices;
using System.Text;
using SqlAssist.Core.Assistant;

namespace SqlAssist.Assistant
{
    /// <summary>Generic credentials encrypted and owned by the signed-in Windows user. No plaintext settings file.</summary>
    internal sealed class CredentialKeyStore : IApiKeyStore
    {
        private readonly string _prefix;
        public CredentialKeyStore(string prefix = "SqlAssist/Assistant/") { _prefix = prefix; }
        public string Read(AiProviderKind provider)
        {
            if (!CredRead(_prefix + provider, 1, 0, out IntPtr pointer))
            {
                if (Marshal.GetLastWin32Error() == 1168) return null;
                throw Failure();
            }
            try
            {
                var credential = (Credential)Marshal.PtrToStructure(pointer, typeof(Credential));
                return credential.BlobSize == 0 ? null : Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2);
            }
            finally { CredFree(pointer); }
        }
        public void Save(AiProviderKind provider, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("กรุณากรอก API Key");
            byte[] bytes = Encoding.Unicode.GetBytes(value.Trim());
            if (bytes.Length > 2560) { Array.Clear(bytes, 0, bytes.Length); throw new ArgumentException("API Key ยาวเกินขีดจำกัด"); }
            IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential { Type = 1, TargetName = _prefix + provider, BlobSize = (uint)bytes.Length, Blob = blob, Persist = 2, UserName = "SqlAssist" };
                if (!CredWrite(ref credential, 0)) throw Failure();
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }
        }
        public void Delete(AiProviderKind provider)
        {
            if (!CredDelete(_prefix + provider, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw Failure();
        }
        private static InvalidOperationException Failure() => new InvalidOperationException("เข้าถึง Windows Credential Manager ไม่สำเร็จ");
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags, Type;
            public string TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint BlobSize;
            public IntPtr Blob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
    }
}
