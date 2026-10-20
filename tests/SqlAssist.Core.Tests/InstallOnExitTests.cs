using System;
using SqlAssist.Core.Update;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class InstallOnExitTests
    {
        private static ReleaseInfo Release(string version) =>
            ReleaseInfo.Parse("{\"tag_name\":\"v" + version + "\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://x\",\"assets\":[]}");

        [Fact]
        public void State_RoundTripsPendingUpdate()
        {
            var state = new UpdateState { PendingVersion = new Version(1, 3, 3), PendingSetup = @"C:\Users\u\AppData\Local\SqlAssist\updates\SqlAssistSetup-1.3.3.exe" };
            var again = UpdateState.Parse(state.ToString());
            Assert.Equal(state.PendingVersion, again.PendingVersion);
            Assert.Equal(state.PendingSetup, again.PendingSetup);
            Assert.Null(UpdateState.Parse("AutoCheck=true\n").PendingVersion);
        }

        [Fact]
        public void AutomaticCheck_StaysQuietAboutTheVersionAlreadyWaiting()
        {
            var state = new UpdateState { PendingVersion = new Version(1, 3, 3) };
            var current = new Version(1, 3, 2);
            Assert.False(UpdatePolicy.ShouldNotify(current, Release("1.3.3"), state, manual: false));
            Assert.True(UpdatePolicy.ShouldNotify(current, Release("1.3.4"), state, manual: false)); // a newer one still shows
            Assert.True(UpdatePolicy.ShouldNotify(current, Release("1.3.3"), state, manual: true));
        }

        [Theory]
        [InlineData(null, "1.3.2", true, PendingAction.None)]
        [InlineData("1.3.3", "1.3.2", true, PendingAction.Restart)]   // waiter lost (restart): start it again
        [InlineData("1.3.3", "1.3.3", true, PendingAction.Clear)]     // installed
        [InlineData("1.3.3", "1.4.0", true, PendingAction.Clear)]     // something newer got installed meanwhile
        [InlineData("1.3.3", "1.3.2", false, PendingAction.Clear)]    // the downloaded setup is gone
        public void ResumePending(string pending, string current, bool setupExists, PendingAction expected)
        {
            var state = new UpdateState { PendingVersion = pending == null ? null : new Version(pending) };
            Assert.Equal(expected, UpdatePolicy.ResumePending(new Version(current), state, setupExists));
        }

        [Fact]
        public void InstallResult_Parses()
        {
            var ok = InstallResult.Parse("Version=1.3.3\r\nOk=true\r\n");
            Assert.Equal((new Version(1, 3, 3), true, (string)null), (ok.Version, ok.Ok, ok.Error));

            var failed = InstallResult.Parse("Version=1.3.3\nOk=false\nError=VSIXInstaller exit code 1001\n");
            Assert.False(failed.Ok);
            Assert.Equal("VSIXInstaller exit code 1001", failed.Error);

            Assert.Null(InstallResult.Parse("garbage"));
        }
    }
}
