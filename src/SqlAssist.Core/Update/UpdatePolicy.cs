using System;
using System.Collections.Generic;
using System.Globalization;

namespace SqlAssist.Core.Update
{
    /// <summary>When to check for an update and whether a release is worth telling the user about.</summary>
    public static class UpdatePolicy
    {
        public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

        /// <summary>"v1.3.0", "1.3" or "1.3.0.0" -> 1.3.0. Always three parts, so 1.3 and 1.3.0 compare equal.</summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            string s = (text ?? "").Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            if (!Version.TryParse(s, out var v)) return false;
            version = Normalize(v);
            return true;
        }

        public static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

        public static bool IsNewer(Version current, Version candidate) =>
            candidate != null && (current == null || Normalize(candidate) > Normalize(current));

        /// <summary>The automatic check runs at most once per <see cref="CheckInterval"/>. A last check in the future
        /// (the clock was changed) counts as stale, so a bad timestamp can't block checking forever.</summary>
        public static bool ShouldAutoCheck(UpdateState state, DateTime nowUtc) =>
            state.AutoCheck && (state.LastCheckUtc == null || state.LastCheckUtc > nowUtc || nowUtc - state.LastCheckUtc >= CheckInterval);

        /// <summary>The automatic check stays quiet about a version the user chose to skip, or one already downloaded and
        /// waiting to install when SSMS closes; a manual check always tells.</summary>
        public static bool ShouldNotify(Version current, ReleaseInfo release, UpdateState state, bool manual) =>
            release != null && IsNewer(current, release.Version) &&
            (manual || ((state.SkippedVersion == null || IsNewer(state.SkippedVersion, release.Version)) &&
                        (state.PendingVersion == null || IsNewer(state.PendingVersion, release.Version))));

        /// <summary>What to do at startup about an update scheduled to install when SSMS closes.</summary>
        public static PendingAction ResumePending(Version current, UpdateState state, bool setupExists)
        {
            if (state.PendingVersion == null) return PendingAction.None;
            // Installed (by the waiter, or by hand), or the downloaded setup is gone: nothing left to do.
            if (!IsNewer(current, state.PendingVersion) || !setupExists) return PendingAction.Clear;
            // Still newer than what runs: the waiter did not get to install it (e.g. Windows restarted), so start it again.
            return PendingAction.Restart;
        }
    }

    public enum PendingAction { None, Clear, Restart }

    /// <summary>What SqlAssistSetup /installonexit leaves in update-result.txt for the next SSMS start to report:
    /// "Version=1.3.3", "Ok=true", "Error=...".</summary>
    public sealed class InstallResult
    {
        public Version Version { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }

        public static InstallResult Parse(string text)
        {
            var result = new InstallResult();
            foreach (string line in (text ?? "").Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "Version" && UpdatePolicy.TryParseVersion(value, out var v)) result.Version = v;
                else if (key == "Ok" && bool.TryParse(value, out bool ok)) result.Ok = ok;
                else if (key == "Error") result.Error = value;
            }
            return result.Version == null ? null : result;
        }
    }

    /// <summary>What the update check remembers between SSMS sessions, stored as key=value lines.</summary>
    public sealed class UpdateState
    {
        public bool AutoCheck { get; set; } = true;
        public DateTime? LastCheckUtc { get; set; }
        public Version SkippedVersion { get; set; }
        /// <summary>A downloaded update waiting to be installed when SSMS closes, and its setup exe.</summary>
        public Version PendingVersion { get; set; }
        public string PendingSetup { get; set; }

        public static UpdateState Parse(string text)
        {
            var state = new UpdateState();
            foreach (string line in (text ?? "").Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "AutoCheck":
                        if (bool.TryParse(value, out bool auto)) state.AutoCheck = auto;
                        break;
                    case "LastCheckUtc":
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var last))
                            state.LastCheckUtc = last;
                        break;
                    case "SkippedVersion":
                        if (UpdatePolicy.TryParseVersion(value, out var skipped)) state.SkippedVersion = skipped;
                        break;
                    case "PendingVersion":
                        if (UpdatePolicy.TryParseVersion(value, out var pending)) state.PendingVersion = pending;
                        break;
                    case "PendingSetup":
                        if (value.Length > 0) state.PendingSetup = value;
                        break;
                }
            }
            return state;
        }

        public override string ToString()
        {
            var lines = new List<string> { "AutoCheck=" + (AutoCheck ? "true" : "false") };
            if (LastCheckUtc != null) lines.Add("LastCheckUtc=" + LastCheckUtc.Value.ToString("o", CultureInfo.InvariantCulture));
            if (SkippedVersion != null) lines.Add("SkippedVersion=" + SkippedVersion);
            if (PendingVersion != null) lines.Add("PendingVersion=" + PendingVersion);
            if (PendingSetup != null) lines.Add("PendingSetup=" + PendingSetup);
            return string.Join("\n", lines) + "\n";
        }
    }
}
