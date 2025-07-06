using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class GameVersionChecker
{
    private Dictionary<string, string> knownHashes = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> KnownHashes => knownHashes;

    /// <summary>
    /// Loads known hashes from a JSON URL on GitHub asynchronously.
    /// JSON format example: { "1.0.0": "abc123hash...", "1.1.0": "def456hash..." }
    /// </summary>
    /// <param name="jsonUrl">URL to raw JSON file on GitHub</param>
    public async Task<bool> UpdateKnownHashesFromGitHubAsync(string jsonUrl)
    {
        try
        {
            using (var client = new HttpClient())
            {
                var json = await client.GetStringAsync(jsonUrl);
                var newHashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (newHashes != null)
                {
                    knownHashes = newHashes;
                    return true;
                }
                return false;
            }
        }
        catch
        {
            // Optionally log error here
            return false;
        }
    }

    /// <summary>
    /// Checks if the given hash is a known version hash.
    /// </summary>
    public bool IsKnownHash(string hash)
    {
        return knownHashes.ContainsValue(hash.ToUpper());
    }

    /// <summary>
    /// Returns the version string matching the given hash, or null if not found.
    /// </summary>
    public string GetVersionByHash(string hash)
    {
        foreach (var kvp in knownHashes)
        {
            if (kvp.Value == hash.ToUpper())
                return kvp.Key;
        }
        return null;
    }
}
