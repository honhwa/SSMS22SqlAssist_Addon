using System;

namespace SqlAssist.Core.Settings
{
    /// <summary>Explain SQL (AI). Off until the user turns it on: nothing leaves the machine before that. The API key is not here
    /// (settings.xml may be copied to other machines); it lives in a DPAPI-protected file of its own.</summary>
    public sealed class AiOptions
    {
        public const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";
        public const string DefaultModel = "claude-opus-5-5";

        public bool Enabled { get; set; }
        public string Endpoint { get; set; } = DefaultEndpoint;
        public string Model { get; set; } = DefaultModel;
        /// <summary>Replace string and number literals with '?' and 0 before sending.</summary>
        public bool MaskLiterals { get; set; } = true;
        /// <summary>Send the MS_Description of the columns too.</summary>
        public bool SendDescriptions { get; set; } = true;
        /// <summary>The user ticked "don't ask again" on the window that shows what is sent.</summary>
        public bool ConsentGiven { get; set; }

        /// <summary>Only an absolute https:// URI is accepted; anything else is the default.</summary>
        public static string CleanEndpoint(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return DefaultEndpoint;
            value = value.Trim();
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? value : DefaultEndpoint;
        }

        public static string CleanModel(string value) => string.IsNullOrWhiteSpace(value) ? DefaultModel : value.Trim();
    }
}
