using System;
using SqlAssist.Assistant;
using SqlAssist.Core.Assistant;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class AssistantCredentialTests
    {
        [Fact]
        public void WindowsCredentialStoreRoundTripIsIsolatedAndDeletable()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;
            // Integration test: isolated temporary target, never the user's real SqlAssist keys.
            var store = new CredentialKeyStore("SqlAssist/Tests/" + Guid.NewGuid() + "/");
            try
            {
                Assert.Null(store.Read(AiProviderKind.OpenRouter));
                store.Save(AiProviderKind.OpenRouter, "test-only-ไทย-secret");
                Assert.Equal("test-only-ไทย-secret", store.Read(AiProviderKind.OpenRouter));
                Assert.Null(store.Read(AiProviderKind.Ollama));
                store.Delete(AiProviderKind.OpenRouter);
                Assert.Null(store.Read(AiProviderKind.OpenRouter));
                store.Delete(AiProviderKind.OpenRouter);
            }
            finally { store.Delete(AiProviderKind.OpenRouter); }
        }
        [Fact]
        public void EmptyKeysDoNotOverwriteExistingCredential()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;
            var store = new CredentialKeyStore("SqlAssist/Tests/" + Guid.NewGuid() + "/");
            try
            {
                store.Save(AiProviderKind.OpenRouter, "test-only-value");
                Assert.Throws<ArgumentException>(() => store.Save(AiProviderKind.OpenRouter, " "));
                Assert.Equal("test-only-value", store.Read(AiProviderKind.OpenRouter));
            }
            finally { store.Delete(AiProviderKind.OpenRouter); }
        }
    }
}
