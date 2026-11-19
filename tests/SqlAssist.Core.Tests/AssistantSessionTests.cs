using System;
using SqlAssist.Core.Assistant;
using SqlAssist.Core.Settings;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class AssistantSessionTests
    {
        [Theory]
        [InlineData("Gemini")]
        [InlineData("0")]
        public void RemovedProviderFallsBackToOpenRouterAndPreservesRemainingSettings(string provider)
        {
            var settings = SqlAssistSettings.Parse("<SqlAssistSettings><Assistant Provider='" + provider + "'><Gemini Model='old'/><OpenRouter Model='vendor/kept'/><Ollama Model='local:kept' BaseUrl='http://host:11434'/></Assistant></SqlAssistSettings>");
            Assert.Equal(AiProviderKind.OpenRouter, settings.Assistant.ActiveProvider);
            Assert.Equal("vendor/kept", settings.Assistant.For(AiProviderKind.OpenRouter).ModelId);
            Assert.Equal("local:kept", settings.Assistant.For(AiProviderKind.Ollama).ModelId);
            Assert.DoesNotContain("Gemini", settings.ToXml());
        }

        [Fact]
        public void RemovedProviderCannotCreateAClient()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AiProviderFactory.Create((AiProviderKind)0, new AiProviderSettings(), null));
        }

        [Theory]
        [InlineData("1", AiProviderKind.OpenRouter)]
        [InlineData("2", AiProviderKind.Ollama)]
        public void ExistingNumericProviderIdsStillSelectTheSameProvider(string value, AiProviderKind expected)
        {
            var settings = SqlAssistSettings.Parse("<SqlAssistSettings><Assistant Provider='" + value + "'/></SqlAssistSettings>");
            Assert.Equal(expected, settings.Assistant.ActiveProvider);
        }

        [Fact]
        public void NewChatRejectsLateChunksAndCompletion()
        {
            var session = new ChatSession();
            var first = session.Begin("Explain this");
            session.TryAppend(first.Id, "old");
            session.NewChat();
            var next = session.Begin("new");
            Assert.False(session.TryAppend(first.Id, "late"));
            Assert.False(session.Complete(first.Id));
            Assert.True(session.IsBusy);
            Assert.Equal("new", next.Context[0].Text);
            Assert.Equal(2, session.Messages.Count);
        }

        [Fact]
        public void StopKeepsPartialAnswerAndRejectsFurtherChanges()
        {
            var session = new ChatSession();
            var turn = session.Begin("question");
            session.TryAppend(turn.Id, "partial");
            session.Stop();
            Assert.False(session.IsBusy);
            Assert.False(session.TryAppend(turn.Id, "late"));
            Assert.Equal("partial", session.Messages[1].Text);
            Assert.Equal(AiMessageState.Stopped, session.Messages[1].State);
        }

        [Fact]
        public void NextRequestMarksStoppedReplyWithoutChangingDisplayedText()
        {
            var session = new ChatSession();
            var turn = session.Begin("question");
            session.TryAppend(turn.Id, "partial"); session.Stop();
            var next = session.Begin("continue");
            Assert.Contains("หยุดกลางทาง", next.Context[1].Text);
            Assert.Equal("partial", session.Messages[1].Text);
        }

        [Fact]
        public void CannotSendTwiceWhileBusy()
        {
            var session = new ChatSession();
            session.Begin("first");
            Assert.Throws<InvalidOperationException>(() => session.Begin("second"));
            Assert.Equal(2, session.Messages.Count);
        }

        [Fact]
        public void ContextDropsOldestWholeTurnsButKeepsVisibleHistory()
        {
            var session = new ChatSession(20);
            var first = session.Begin("12345678");
            session.TryAppend(first.Id, "abcdefgh");
            session.Complete(first.Id);
            Assert.Equal(1, session.PreviewOmittedTurns("new question"));
            var next = session.Begin("new question");
            Assert.Equal(1, next.OmittedTurns);
            Assert.Single(next.Context);
            Assert.Equal(4, session.Messages.Count);
        }

        [Fact]
        public void FullTurnsAreSentAndRequestSnapshotCannotChange()
        {
            var session = new ChatSession();
            var first = session.Begin("question");
            session.TryAppend(first.Id, "answer");
            session.Complete(first.Id);
            var second = session.Begin("follow up");
            Assert.Equal(new[] { "user", "assistant", "user" }, System.Linq.Enumerable.Select(second.Context, x => x.Role));
            session.NewChat();
            Assert.Equal("answer", second.Context[1].Text);
        }

        [Fact]
        public void FailedEmptyTurnsDoNotEnterNextContext()
        {
            var session = new ChatSession();
            var first = session.Begin("failed question");
            session.Fail(first.Id);
            var second = session.Begin("try again");
            Assert.Single(second.Context);
        }

        [Fact]
        public void OversizedLatestInputIsRejectedWithoutChangingChat()
        {
            var session = new ChatSession(10);
            Assert.Throws<ArgumentException>(() => session.Begin("12345678901"));
            Assert.Empty(session.Messages);
        }

        [Fact]
        public void AssistantSettingsRoundTripIndependentlyAndContainNoKeys()
        {
            var settings = new SqlAssistSettings();
            settings.Assistant.ActiveProvider = AiProviderKind.OpenRouter;
            settings.Assistant.For(AiProviderKind.OpenRouter).ModelId = "vendor/model";
            settings.Assistant.For(AiProviderKind.Ollama).BaseUrl = "https://host/prefix/";
            settings.Assistant.For(AiProviderKind.OpenRouter).UseStreaming = false;
            var xml = settings.ToXml();
            var loaded = SqlAssistSettings.Parse(xml);
            Assert.Equal(AiProviderKind.OpenRouter, loaded.Assistant.ActiveProvider);
            Assert.Equal("vendor/model", loaded.Assistant.For(AiProviderKind.OpenRouter).ModelId);
            Assert.False(loaded.Assistant.For(AiProviderKind.OpenRouter).UseStreaming);
            Assert.Equal("https://host/prefix/", loaded.Assistant.For(AiProviderKind.Ollama).BaseUrl);
            Assert.DoesNotContain("ApiKey", xml);
        }
    }
}
