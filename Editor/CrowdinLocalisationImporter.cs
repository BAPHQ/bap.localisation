using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace BAP.Localisation.Editor
{
    public static class CrowdinLocalisationImporter
    {
        private const string ApiRoot = "https://api.crowdin.com/api/v2";

        [Serializable]
        private class BuildResponse
        {
            public BuildData data;
        }

        [Serializable]
        private class BuildData
        {
            public int id;
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

        [MenuItem("Tools/BAP/Localisation/Import From Crowdin")]
        private static void ImportFromSelectedConfig()
        {
            var config = Selection.activeObject as CrowdinImporterConfig;

            if (config == null)
            {
                EditorUtility.DisplayDialog(
                    "Crowdin Import",
                    "Select a CrowdinImporterConfig asset in the Project window first.",
                    "OK");
                return;
            }

            Import(config);
        }

        public static void Import(CrowdinImporterConfig config)
        {
            if (!ValidateConfig(config))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Crowdin Import", "Starting Crowdin build...", 0.1f);
                var buildId = StartBuild(config);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Waiting for build to finish...", 0.3f);
                WaitForBuild(config, buildId);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Requesting build download URL...", 0.5f);
                var downloadUrl = GetDownloadUrl(config, buildId);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Downloading build archive...", 0.7f);
                var zipBytes = DownloadBytes(downloadUrl, config.PersonalAccessToken);

                EditorUtility.DisplayProgressBar("Crowdin Import", "Writing localisation files...", 0.9f);
                ApplyArchive(config, zipBytes);

                AssetDatabase.Refresh();
                Debug.Log("[CrowdinImporter] Import complete.");
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

        private static bool ValidateConfig(CrowdinImporterConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[CrowdinImporter] Configuration is null.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.ProjectId))
            {
                Debug.LogError("[CrowdinImporter] ProjectId is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.PersonalAccessToken))
            {
                Debug.LogError("[CrowdinImporter] PersonalAccessToken is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.KeyZipEntryPath) || string.IsNullOrWhiteSpace(config.KeyOutputAssetPath))
            {
                Debug.LogError("[CrowdinImporter] KeyZipEntryPath and KeyOutputAssetPath are required.");
                return false;
            }

            return true;
        }

        private static int StartBuild(CrowdinImporterConfig config)
        {
            var url = $"{ApiRoot}/projects/{config.ProjectId}/translations/builds";
            var responseText = SendJsonRequest(url, "POST", "{}", config.PersonalAccessToken);
            var response = JsonUtility.FromJson<BuildResponse>(responseText);

            if (response?.data == null)
            {
                throw new Exception("[CrowdinImporter] Invalid build start response.");
            }

            return response.data.id;
        }

        private static void WaitForBuild(CrowdinImporterConfig config, int buildId)
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(config.BuildTimeoutSeconds <= 0 ? 120 : config.BuildTimeoutSeconds);
            var pollMs = config.BuildPollIntervalMilliseconds <= 0 ? 2000 : config.BuildPollIntervalMilliseconds;

            while (DateTime.UtcNow <= timeoutAt)
            {
                var url = $"{ApiRoot}/projects/{config.ProjectId}/translations/builds/{buildId}";
                var responseText = SendJsonRequest(url, "GET", null, config.PersonalAccessToken);
                var response = JsonUtility.FromJson<BuildResponse>(responseText);
                var status = response?.data?.status;

                if (status == "finished")
                {
                    return;
                }

                if (status == "failed")
                {
                    throw new Exception($"[CrowdinImporter] Crowdin build {buildId} failed.");
                }

                System.Threading.Thread.Sleep(pollMs);
            }

            throw new TimeoutException($"[CrowdinImporter] Timed out waiting for build {buildId}.");
        }

        private static string GetDownloadUrl(CrowdinImporterConfig config, int buildId)
        {
            var url = $"{ApiRoot}/projects/{config.ProjectId}/translations/builds/{buildId}/download";
            var responseText = SendJsonRequest(url, "GET", null, config.PersonalAccessToken);
            var response = JsonUtility.FromJson<DownloadResponse>(responseText);

            if (string.IsNullOrWhiteSpace(response?.data?.url))
            {
                throw new Exception("[CrowdinImporter] Invalid download URL response.");
            }

            return response.data.url;
        }

        private static void ApplyArchive(CrowdinImporterConfig config, byte[] zipBytes)
        {
            using var stream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var keyContent = ReadEntryText(archive, config.KeyZipEntryPath);
            WriteAssetText(config.KeyOutputAssetPath, keyContent);

            foreach (var target in config.Targets)
            {
                if (target == null || string.IsNullOrWhiteSpace(target.ZipEntryPath) || string.IsNullOrWhiteSpace(target.OutputAssetPath))
                {
                    continue;
                }

                var phraseContent = ReadEntryText(archive, target.ZipEntryPath);
                WriteAssetText(target.OutputAssetPath, phraseContent);
            }
        }

        private static string ReadEntryText(ZipArchive archive, string entryPath)
        {
            var normalized = NormalizeZipPath(entryPath);
            var entry = archive.GetEntry(normalized) ?? archive.GetEntry(normalized.TrimStart('/'));

            if (entry == null)
            {
                throw new FileNotFoundException($"[CrowdinImporter] Could not find entry in zip: {entryPath}");
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void WriteAssetText(string assetPath, string content)
        {
            var normalizedAssetPath = NormalizeAssetPath(assetPath);
            var absolutePath = Path.Combine(Directory.GetCurrentDirectory(), normalizedAssetPath);
            var directory = Path.GetDirectoryName(absolutePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, content, Encoding.UTF8);
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

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.SetRequestHeader("Authorization", $"Bearer {token}");
            }

            RunRequest(request);

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"[CrowdinImporter] Download failed: {request.error}");
            }

            return request.downloadHandler.data;
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

        private static string NormalizeZipPath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}