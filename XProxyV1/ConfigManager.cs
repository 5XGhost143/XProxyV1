using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace XProxyV1
{
    public static class ConfigManager
    {
        public static HashSet<string> LoadBlacklist(string filePath = "json/blacklist.json")
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Blacklist file not found: {filePath}");
                    CreateDefaultBlacklist(filePath);
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var json = File.ReadAllText(filePath);
                var domains = JsonSerializer.Deserialize<List<string>>(json);
                
                Console.WriteLine($"Loaded {domains?.Count ?? 0} blocked domains");
                return new HashSet<string>(domains ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Loading Blacklist: {ex.Message}");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static Dictionary<string, string> LoadRedirects(string filePath = "json/redirects.json")
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Redirects file not found: {filePath}");
                    CreateDefaultRedirects(filePath);
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                var json = File.ReadAllText(filePath);
                var redirects = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                
                Console.WriteLine($"Loaded {redirects?.Count ?? 0} redirects");
                return redirects ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error Loading Redirects: {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void CreateDefaultBlacklist(string filePath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                
                var defaultBlacklist = new List<string>
                {
                    "example-blocked.com",
                    "malicious-site.net",
                    "ad-tracker.com",
                    "facebook.com",
                    "twitter.com"
                };

                var json = JsonSerializer.Serialize(defaultBlacklist, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(filePath, json);
                Console.WriteLine($"Created default blacklist at: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating default blacklist: {ex.Message}");
            }
        }

        private static void CreateDefaultRedirects(string filePath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                
                var defaultRedirects = new Dictionary<string, string>
                {
                    { "google.de", "google.com" },
                    { "www.google.de", "www.google.com" },
                    { "youtube.de", "youtube.com" },
                    { "www.youtube.de", "www.youtube.com" },
                    { "amazon.de", "amazon.com" },
                    { "www.amazon.de", "www.amazon.com" }
                };

                var json = JsonSerializer.Serialize(defaultRedirects, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(filePath, json);
                Console.WriteLine($"Created default redirects at: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating default redirects: {ex.Message}");
            }
        }
    }
}