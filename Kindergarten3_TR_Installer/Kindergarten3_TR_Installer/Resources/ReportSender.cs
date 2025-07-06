using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Kindergarten3_TR_Installer
{
    public static class ReportSender
    {
        private static readonly string base64Webhook = "";

        private static string DecodeWebhook()
        {
            try
            {
                string padded = base64Webhook + new string('=', (4 - base64Webhook.Length % 4) % 4);
                byte[] data = Convert.FromBase64String(padded);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string> ComputeFileHashAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(filePath))
                    return null;

                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            });
        }

        public static async Task<bool> SendReportAsync(string gameName, string hash, string filePath)
        {
            string webhookUrl = DecodeWebhook();
            if (string.IsNullOrWhiteSpace(webhookUrl)) return false;

            var payload = new
            {
                content = $"**Version Report for {gameName}**\n" +
                  $":file_folder: File: `{Path.GetFileName(filePath)}`\n" +
                  $":lock: SHA256: `{hash}`\n" +
                  $":clock3: Time: {DateTime.UtcNow:u}"
            };


            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("Content-Type", "application/json");
                    string jsonPayload = JsonConvert.SerializeObject(payload);
                    await client.UploadStringTaskAsync(new Uri(webhookUrl), "POST", jsonPayload);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
