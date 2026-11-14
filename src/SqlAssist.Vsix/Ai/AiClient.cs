using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlAssist.Ai
{
    /// <summary>Posts an Explain SQL request to the Claude Messages API. Plain HttpClient rather than the Anthropic SDK: SqlAssist ships
    /// as one DLL loaded into SSMS's process, and the SDK's dependencies could clash with assemblies SSMS has already loaded.
    /// Logs only the status, the size sent and the time taken: never the SQL, the answer or the key.</summary>
    internal static class AiClient
    {
        public const string ApiVersion = "2026-10-21";
        /// <summary>Lets the request carry <c>fallbacks: "default"</c>, so a declined request is retried on another model by the API.</summary>
        public const string FallbackBeta = "server-side-fallback-2026-07-01";

        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(CreateClient);

        /// <summary>(0, null) when the request could not be made or timed out; cancelling <paramref name="token"/> throws.</summary>
        public static async Task<(int status, string body)> SendAsync(string endpoint, string apiKey, string body, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", ApiVersion);
                    request.Headers.Add("anthropic-beta", FallbackBeta);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (var response = await Http.Value.SendAsync(request, token).ConfigureAwait(false))
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        int status = (int)response.StatusCode;
                        Log.Info("AI request: HTTP " + status + ", " + body.Length + " chars sent, " + clock.ElapsedMilliseconds + " ms");
                        return (status, text);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Log.Info("AI request: cancelled after " + clock.ElapsedMilliseconds + " ms");
                throw;
            }
            catch (Exception ex)
            {
                // Includes HttpClient's own timeout, which surfaces as a TaskCanceledException without our token being cancelled.
                Log.Error("AI request failed after " + clock.ElapsedMilliseconds + " ms", ex);
                return (0, null);
            }
        }

        private static HttpClient CreateClient()
        {
            // SSMS runs on .NET Framework; make sure TLS 1.2 is on whatever the machine's defaults are.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SqlAssist/" + SqlAssistPackage.Version);
            return client;
        }
    }
}
