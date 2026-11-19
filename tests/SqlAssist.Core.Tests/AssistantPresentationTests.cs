using System;
using System.Linq;
using SqlAssist.Core.Assistant;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class AssistantPresentationTests
    {
        [Fact]
        public void MarkdownSeparatesSqlAndLeavesUntrustedHtmlAsText()
        {
            var blocks = AssistantMarkdown.Parse("# Explain\n<script>alert(1)</script>\n![remote](https://host/image)\n```sql\nSELECT 1;\n```\nend");
            Assert.Equal(3, blocks.Count);
            Assert.Contains("<script>", blocks[0].Text);
            Assert.Contains("https://host/image", blocks[0].Text);
            Assert.True(blocks[1].IsSql);
            Assert.Equal("SELECT 1;", blocks[1].Text);
            Assert.Equal("end", blocks[2].Text);
        }
        [Fact]
        public void UnclosedSqlFenceKeepsReceivedCode()
        {
            var block = AssistantMarkdown.Parse("```tsql\nSELECT 'ไทย'").Single();
            Assert.True(block.IsSql);
            Assert.Equal("SELECT 'ไทย'", block.Text);
        }
        [Fact]
        public void EnvironmentKeyTakesPrecedenceAndDeleteDoesNotRemoveIt()
        {
            var store = new MemoryKeys();
            store.Save(AiProviderKind.OpenRouter, "saved");
            var keys = new ApiKeyResolver(store, name => name == "OPENROUTER_API_KEY" ? "environment-secret" : null);
            Assert.Equal("environment-secret", keys.Get(AiProviderKind.OpenRouter));
            Assert.Equal("Environment: OPENROUTER_API_KEY", keys.Source(AiProviderKind.OpenRouter));
            store.Delete(AiProviderKind.OpenRouter);
            Assert.Equal("environment-secret", keys.Get(AiProviderKind.OpenRouter));
        }
        [Fact]
        public void MissingKeyAndLocalOllamaNeedNoSecret()
        {
            var keys = new ApiKeyResolver(new MemoryKeys(), name => null);
            Assert.Null(keys.Get(AiProviderKind.OpenRouter));
            Assert.Null(keys.Get(AiProviderKind.Ollama));
        }
        private sealed class MemoryKeys : IApiKeyStore
        {
            private string _value;
            public string Read(AiProviderKind provider) => _value;
            public void Save(AiProviderKind provider, string value) => _value = value;
            public void Delete(AiProviderKind provider) => _value = null;
        }
    }
}
