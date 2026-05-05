// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace BAP.Localisation.Editor
{
    public static class CrowdinLocalisationImporter
    {
        private const string API_ROOT = "https://api.crowdin.com/api/v2";
        private const int BUILD_POLL_INTERVAL_MILLISECONDS = 2000;
        private const int BUILD_TIMEOUT_SECONDS = 120;

        public static void Import(CrowdinImportConfig config)
        {
            if (!ValidateConfig(config))
            {
                return;
            }

            try
            {
                // Start clean so each import runs against fresh temp artifacts.
                CleanupTempArtifacts();

                EditorUtility.DisplayProgressBar("Crowdin Import", "Resolving Crowdin project by name...", 0.1f);
                var projectId = GetProjectIdByName(config);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Resolving Crowdin bundle by name...", 0.25f);
                var bundleId = GetBundleIdByName(config, projectId);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Starting Crowdin bundle export...", 0.4f);
                var exportId = StartBundleExport(config, projectId, bundleId);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Waiting for export to finish...", 0.55f);
                WaitForBundleExport(config, projectId, bundleId, exportId);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Requesting bundle export download URL...", 0.7f);
                var downloadUrl = GetBundleExportDownloadUrl(config, projectId, bundleId, exportId);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Downloading bundle export archive...", 0.85f);
                var zipBytes = DownloadBytes(downloadUrl, config.ApiKey);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Saving and extracting archive...", 0.95f);
                var paths = SaveAndExtractToTemp(zipBytes);
                Debug.Log($"[CrowdinImporter] Bundle export complete. Zip: {paths.zipPath}. Extracted: {paths.extractDirectory}");

                EditorUtility.DisplayProgressBar("Crowdin Import", "Generating localisation resource files...", 1.0f);
                ImportExtractedFilesToResources(config, paths.extractDirectory);
            }
            catch (Exception e)
            {
                Debug.LogError("[CrowdinImporter] Import failed.");
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static int GetProjectIdByName(CrowdinImportConfig config)
        {
            // Resolve project ID dynamically so config can stay name-based.
            var url = $"{API_ROOT}/projects";
            var responseText = SendJsonRequest(url, "GET", null, config.ApiKey);
            var response = JsonUtility.FromJson<ProjectsListResponse>(responseText);

            if (response?.data == null)
            {
                throw new Exception("[CrowdinImporter] Invalid projects list response.");
            }

            foreach (var item in response.data)
            {
                var project = item?.data;
                if (project != null && string.Equals(project.name, config.ProjectName, StringComparison.OrdinalIgnoreCase))
                {
                    return project.id;
                }
            }

            throw new Exception($"[CrowdinImporter] Project '{config.ProjectName}' was not found.");
        }

        private static int GetBundleIdByName(CrowdinImportConfig config, int projectId)
        {
            // Resolve bundle ID from the selected project using the configured bundle name.
            var url = $"{API_ROOT}/projects/{projectId}/bundles";
            var responseText = SendJsonRequest(url, "GET", null, config.ApiKey);
            var response = JsonUtility.FromJson<BundlesListResponse>(responseText);

            if (response?.data == null)
            {
                throw new Exception("[CrowdinImporter] Invalid bundles list response.");
            }

            foreach (var item in response.data)
            {
                var bundle = item?.data;
                if (bundle != null && string.Equals(bundle.name, config.BundleName, StringComparison.OrdinalIgnoreCase))
                {
                    return bundle.id;
                }
            }

            throw new Exception($"[CrowdinImporter] Bundle '{config.BundleName}' was not found in project '{config.ProjectName}'.");
        }

        private static bool ValidateConfig(CrowdinImportConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[CrowdinImporter] Configuration is null.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Debug.LogError("[CrowdinImporter] PersonalAccessTokenApiKey is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.ProjectName))
            {
                Debug.LogError("[CrowdinImporter] ProjectName is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.BundleName))
            {
                Debug.LogError("[CrowdinImporter] BundleName is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.ResourcesPath))
            {
                Debug.LogError("[CrowdinImporter] ResourcesPath is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.SourceLanguageFileName))
            {
                Debug.LogError("[CrowdinImporter] SourceLanguageFileName is required.");
                return false;
            }

            return true;
        }

        private static void ImportExtractedFilesToResources(CrowdinImportConfig config, string extractDirectory)
        {
            if (!Directory.Exists(extractDirectory))
            {
                throw new DirectoryNotFoundException($"[CrowdinImporter] Extracted directory was not found: {extractDirectory}");
            }

            // Source file defines canonical key order used by all generated txt files.
            var sourceFileName = EnsureJsonExtension(config.SourceLanguageFileName);
            var jsonFiles = Directory.GetFiles(extractDirectory, "*.json", SearchOption.AllDirectories);
            var sourceFilePath = jsonFiles.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), sourceFileName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new Exception($"[CrowdinImporter] Source language file '{sourceFileName}' was not found in '{extractDirectory}'.");
            }

            var sourceEntries = LoadPhraseEntries(sourceFilePath);
            var keys = new List<string>(sourceEntries.Count);
            var sourcePhrases = new List<string>(sourceEntries.Count);

            foreach (var entry in sourceEntries)
            {
                var key = entry?.identifier ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                keys.Add(key);
                sourcePhrases.Add(entry.translation ?? entry.source_string ?? string.Empty);
            }

            if (keys.Count == 0)
            {
                throw new Exception($"[CrowdinImporter] No valid keys found in source file '{sourceFileName}'.");
            }

            // Replace previous import outputs to avoid stale language files.
            var resourcesAbsolutePath = GetAbsoluteResourcesPath(config.ResourcesPath);
            Directory.CreateDirectory(resourcesAbsolutePath);
            ClearDirectoryFiles(resourcesAbsolutePath);

            WriteLines(Path.Combine(resourcesAbsolutePath, "keys.txt"), keys);
            WriteLines(Path.Combine(resourcesAbsolutePath, ToTextFileName(sourceFileName)), sourcePhrases);

            foreach (var targetFilePath in jsonFiles)
            {
                if (string.Equals(targetFilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetEntries = LoadPhraseEntries(targetFilePath);
                var targetLookup = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var entry in targetEntries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.identifier) || targetLookup.ContainsKey(entry.identifier))
                    {
                        continue;
                    }

                    targetLookup[entry.identifier] = entry.translation ?? string.Empty;
                }

                var alignedPhrases = new List<string>(keys.Count);
                foreach (var key in keys)
                {
                    targetLookup.TryGetValue(key, out var value);
                    alignedPhrases.Add(value ?? string.Empty);
                }

                WriteLines(Path.Combine(resourcesAbsolutePath, ToTextFileName(Path.GetFileName(targetFilePath))), alignedPhrases);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[CrowdinImporter] Localisation files written to {resourcesAbsolutePath}");
        }

        private static List<CrowdinPhraseEntry> LoadPhraseEntries(string filePath)
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<CrowdinPhraseEntry>();
            }

            var wrappedJson = "{\"items\":" + json + "}";
            var response = JsonUtility.FromJson<CrowdinPhraseEntryArray>(wrappedJson);
            return response?.items != null ? new List<CrowdinPhraseEntry>(response.items) : new List<CrowdinPhraseEntry>();
        }

        // Delete only files in the target folder; keep directories intact.
        private static void ClearDirectoryFiles(string directory)
        {
            foreach (var filePath in Directory.GetFiles(directory))
            {
                File.Delete(filePath);
            }
        }

        private static void WriteLines(string path, List<string> lines)
        {
            var sanitizedLines = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                sanitizedLines.Add(EscapeNewlines(line));
            }

            File.WriteAllText(path, string.Join("\n", sanitizedLines), new UTF8Encoding(false));
        }

        private static string EscapeNewlines(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\r\n", "\\r\\n").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string EnsureJsonExtension(string fileName)
        {
            return fileName.EndsWith(".json", true, CultureInfo.InvariantCulture)
                ? fileName
                : fileName + ".json";
        }

        private static string ToTextFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + ".txt";
        }

        private static string GetAbsoluteResourcesPath(string resourcesPath)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var normalizedPath = NormalizeAssetPath(resourcesPath);
            var relativePath = normalizedPath.Substring("Assets/".Length);
            return Path.Combine(projectRoot, "Assets", relativePath);
        }

        private static string StartBundleExport(CrowdinImportConfig config, int projectId, int bundleId)
        {
            var url = $"{API_ROOT}/projects/{projectId}/bundles/{bundleId}/exports";
            var responseText = SendJsonRequest(url, "POST", null, config.ApiKey);
            var response = JsonUtility.FromJson<BundleExportStartResponse>(responseText);

            if (response?.data == null || string.IsNullOrWhiteSpace(response.data.identifier))
            {
                throw new Exception("[CrowdinImporter] Invalid bundle export start response.");
            }

            return response.data.identifier;
        }

        private static void WaitForBundleExport(CrowdinImportConfig config, int projectId, int bundleId, string exportId)
        {
            // Poll until Crowdin marks the bundle export as finished.
            var timeoutAt = DateTime.UtcNow.AddSeconds(BUILD_TIMEOUT_SECONDS);
            var pollMs = BUILD_POLL_INTERVAL_MILLISECONDS;

            while (DateTime.UtcNow <= timeoutAt)
            {
                var url = $"{API_ROOT}/projects/{projectId}/bundles/{bundleId}/exports/{exportId}";
                var responseText = SendJsonRequest(url, "GET", null, config.ApiKey);
                var response = JsonUtility.FromJson<BundleExportStatusResponse>(responseText);
                var status = response?.data?.status;

                if (status == "finished")
                {
                    return;
                }

                if (status == "failed")
                {
                    throw new Exception($"[CrowdinImporter] Crowdin bundle export {exportId} failed.");
                }

                System.Threading.Thread.Sleep(pollMs);
            }

            throw new TimeoutException($"[CrowdinImporter] Timed out waiting for bundle export {exportId}.");
        }

        private static string GetBundleExportDownloadUrl(CrowdinImportConfig config, int projectId, int bundleId, string exportId)
        {
            var url = $"{API_ROOT}/projects/{projectId}/bundles/{bundleId}/exports/{exportId}/download";
            var responseText = SendJsonRequest(url, "GET", null, config.ApiKey);
            var response = JsonUtility.FromJson<DownloadResponse>(responseText);

            if (string.IsNullOrWhiteSpace(response?.data?.url))
            {
                throw new Exception("[CrowdinImporter] Invalid download URL response.");
            }

            return response.data.url;
        }

        private static (string zipPath, string extractDirectory) SaveAndExtractToTemp(byte[] zipBytes)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(projectRoot, "Temp");
            var extractDir = Path.Combine(tempDir, "crowdin");
            var zipPath = Path.Combine(tempDir, "crowdin.zip");

            // Keep export artifacts predictable for easier debugging and cleanup.
            Directory.CreateDirectory(tempDir);
            File.WriteAllBytes(zipPath, zipBytes);

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir);
            return (zipPath, extractDir);
        }

        private static void CleanupTempArtifacts()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var tempDir = Path.Combine(projectRoot, "Temp");
            var extractDir = Path.Combine(tempDir, "crowdin");
            var zipPath = Path.Combine(tempDir, "crowdin.zip");

            // Remove previous import artifacts before starting a new run.
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }

        private static string SendJsonRequest(string url, string method, string bodyJson, string token)
        {
            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(bodyJson))
            {
                var payload = Encoding.UTF8.GetBytes(bodyJson);
                request.uploadHandler = new UploadHandlerRaw(payload);
            }

            RunRequest(request);

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorBody = request.downloadHandler?.text;
                throw new Exception($"[CrowdinImporter] Request failed ({method} {url}). Error: {request.error}. Body: {errorBody}");
            }

            return request.downloadHandler.text;
        }

        private static byte[] DownloadBytes(string url, string token)
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();

            // Crowdin API calls need auth; temporary pre-signed URLs usually do not.
            if (ShouldAttachAuthHeader(url) && !string.IsNullOrWhiteSpace(token))
            {
                request.SetRequestHeader("Authorization", $"Bearer {token}");
            }

            RunRequest(request);

            if (request.result != UnityWebRequest.Result.Success)
            {
                var errorBody = request.downloadHandler?.text;
                var statusCode = request.responseCode;
                throw new Exception($"[CrowdinImporter] Download failed (GET {url}). Status: {statusCode}. Error: {request.error}. Body: {errorBody}");
            }

            return request.downloadHandler.data;
        }

        private static bool ShouldAttachAuthHeader(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return true;
            }

            return string.Equals(uri.Host, "api.crowdin.com", StringComparison.OrdinalIgnoreCase);
        }

        private static void RunRequest(UnityWebRequest request)
        {
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/"))
            {
                normalized = $"Assets/{normalized.TrimStart('/')}";
            }

            return normalized;
        }

        [Serializable]
        private class ProjectsListResponse
        {
            public List<ProjectItem> data;
        }

        [Serializable]
        private class BundlesListResponse
        {
            public List<BundleItem> data;
        }

        [Serializable]
        private class ProjectItem
        {
            public ProjectData data;
        }

        [Serializable]
        private class ProjectData
        {
            public int id;
            public string name;
        }

        [Serializable]
        private class BundleItem
        {
            public BundleData data;
        }

        [Serializable]
        private class BundleData
        {
            public int id;
            public string name;
        }

        [Serializable]
        private class BundleExportStartResponse
        {
            public BundleExportData data;
        }

        [Serializable]
        private class BundleExportStatusResponse
        {
            public BundleExportData data;
        }

        [Serializable]
        private class BundleExportData
        {
            public string identifier;
            public string status;
        }

        [Serializable]
        private class DownloadResponse
        {
            public DownloadData data;
        }

        [Serializable]
        private class DownloadData
        {
            public string url;
        }

        [Serializable]
        private class CrowdinPhraseEntry
        {
            public string identifier;
            public string source_string;
            public string translation;
        }

        [Serializable]
        private class CrowdinPhraseEntryArray
        {
            public CrowdinPhraseEntry[] items;
        }
    }
}