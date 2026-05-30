using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

public class MapLoader : MonoBehaviour
{
    public class PreparedMapLoad
    {
        public Stream Stream;
        public string CachedPath;
        public string URL;
        public string MapID;
        public string MapHash;
        public bool IgnoreMapForSharing;
    }

    private static bool _loading = false;
    public static bool Loading
    {
        get => _loading;

        private set
        {
            _loading = value;
            OnLoadingChanged?.Invoke(value);
        }
    }

    public static string LoadingMessage = "";
    public static float Progress;

    public static event Action<bool> OnLoadingChanged;
    public static event Action OnMapLoaded;
    public static event Action OnLoadingFailed;
    public static event Action OnReplayMapPrompt;


    private IEnumerator LoadMapCoroutine(IMapDataLoader loader)
    {
        Loading = true;

        using Task<LoadedMap> loadingTask = loader.GetMap();
        yield return new WaitUntil(() => loadingTask.IsCompleted);
        LoadedMap mapData = loadingTask.Result;

        Debug.Log("Loading complete.");
        LoadingMessage = "Done";

        loader.Dispose();

        LoadingMessage = "Initializing";
        //Wait 2 frames to ensure the text updates
        yield return null;
        yield return null;

        SetMap(mapData);
    }


    private void LoadMapZip(string directory)
    {
        Loading = true;

        ZipReader zipReader = new ZipReader();
        try
        {
            Debug.Log("Loading map zip.");
            LoadingMessage = "Loading map zip";

            zipReader.Archive = ZipFile.OpenRead(directory);
            StartCoroutine(LoadMapCoroutine(zipReader));
        }
        catch(Exception err)
        {
            zipReader.Dispose();

            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to load zip file!");
            Debug.LogWarning($"Unhandled exception loading zip: {err.Message}, {err.StackTrace}.");

            SetMap(LoadedMap.Empty);
        }
    }


#if UNITY_WEBGL && !UNITY_EDITOR
    private IEnumerator LoadMapZipWebGLCoroutine(string directory)
    {
        Loading = true;
        LoadingMessage = "Loading zip";

        Debug.Log("Starting web request.");
        using UnityWebRequest uwr = UnityWebRequest.Get(directory);
        yield return uwr.SendWebRequest();

        if(uwr.result == UnityWebRequest.Result.Success)
        {
            ZipReader zipReader = new ZipReader();
            try
            {
                zipReader.ArchiveStream = new MemoryStream(uwr.downloadHandler.data);
                zipReader.Archive = new ZipArchive(zipReader.ArchiveStream, ZipArchiveMode.Read);

                StartCoroutine(LoadMapCoroutine(zipReader));
            }
            catch(Exception e)
            {
                Debug.LogWarning($"Failed to read map data with error: {e.Message}, {e.StackTrace}");
                ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to read map data!");

                zipReader.Dispose();
                SetMap(LoadedMap.Empty);
                yield break;
            }
        }
        else
        {
            Debug.LogWarning(uwr.error);
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to load map! {uwr.error}");

            SetMap(LoadedMap.Empty);
            yield break;
        }
    }


    public void LoadMapZipWebGL(string directory)
    {
        if(DialogueHandler.DialogueActive)
        {
            return;
        }

        if(Loading)
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "You're already loading something!");
            Debug.LogWarning("Trying to load a map while already loading!");
            return;
        }

        StartCoroutine(LoadMapZipWebGLCoroutine(directory));
        UrlArgHandler.LoadedMapURL = null;
    }
#endif


    public IEnumerator LoadMapZipURLCoroutine(string url, string mapID = null, string mapHash = null, bool noProxy = false)
    {
        Loading = true;

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedMap(url, mapID, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            Debug.Log("Found map in cache.");
            LoadMapZip(cachedFile.FilePath);
            yield break;
        }
#endif

        Debug.Log($"Downloading map data from: {url}");
        LoadingMessage = "Downloading map";

        using Task<Stream> downloadTask = WebLoader.LoadFileURL(url, noProxy);
        yield return new WaitUntil(() => downloadTask.IsCompleted);

        Stream zipStream = downloadTask.Result;

        if(zipStream == null)
        {
            Debug.LogWarning("Downloaded data is null!");

            SetMap(LoadedMap.Empty);
            yield break;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        string extraData = mapID == null ? null : "latest";
        CacheManager.SaveMapToCache(zipStream, url, mapID, mapHash, extraData);
#endif

        ZipReader zipReader = new ZipReader(null, zipStream);
        try
        {
            zipReader.Archive = new ZipArchive(zipReader.ArchiveStream, ZipArchiveMode.Read);
            StartCoroutine(LoadMapCoroutine(zipReader));
        }
        catch(Exception err)
        {
            zipReader.Dispose();

            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to read map zip!");
            Debug.LogWarning($"Unhandled exception loading zip URL: {err.Message}, {err.StackTrace}");

            SetMap(LoadedMap.Empty);
        }
    }


    public IEnumerator LoadMapZipURLsCoroutine(string[] urls, string mapID = null, string mapHash = null, bool noProxy = false)
    {
        Loading = true;

        for(int i = 0; i < urls.Length; i++)
        {
            string url = urls[i];

#if !UNITY_WEBGL || UNITY_EDITOR
            CachedFile cachedFile = CacheManager.GetCachedMap(url, mapID, mapHash);
            if(!string.IsNullOrEmpty(cachedFile?.FilePath))
            {
                Debug.Log("Found map in cache.");
                LoadMapZip(cachedFile.FilePath);
                yield break;
            }
#endif

            Debug.Log($"Downloading map data from: {url}");
            if(urls.Length > 1)
            {
                LoadingMessage = $"Downloading map (url {i + 1})";
            }
            else LoadingMessage = "Downloading map";

            using Task<Stream> downloadTask = WebLoader.LoadFileURL(url, noProxy, false);
            yield return new WaitUntil(() => downloadTask.IsCompleted);

            Stream zipStream = downloadTask.Result;

            if(zipStream == null)
            {
                Debug.LogWarning("Downloaded data is null!");
                continue;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            CacheManager.SaveMapToCache(zipStream, url, mapID, mapHash);
#endif

            ZipReader zipReader = new ZipReader(null, zipStream);
            try
            {
                zipReader.Archive = new ZipArchive(zipReader.ArchiveStream, ZipArchiveMode.Read);

                UrlArgHandler.ignoreMapForSharing = true;
                if(!string.IsNullOrEmpty(mapID))
                {
                    UrlArgHandler.LoadedMapID = mapID;
                }
                else UrlArgHandler.LoadedMapURL = url;

                StartCoroutine(LoadMapCoroutine(zipReader));
                yield break;
            }
            catch(Exception err)
            {
                zipReader.Dispose();

                ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to read map zip!");
                Debug.LogWarning($"Unhandled exception loading zip URL: {err.Message}, {err.StackTrace}");

                SetMap(LoadedMap.Empty);
                yield break;
            }
        }

        //We've tried every URL available and all of them failed
        Debug.Log("No urls succeeded! Showing manual map selection.");
        Loading = false;
        LoadingMessage = "";

        OnReplayMapPrompt?.Invoke();
    }


    private IEnumerator LoadPreparedMapCoroutine(Task<PreparedMapLoad> mapTask)
    {
        LoadingMessage = "Downloading map";
        yield return new WaitUntil(() => mapTask.IsCompleted);

        if(mapTask.IsFaulted)
        {
            Debug.LogWarning($"Failed to prepare map with error: {mapTask.Exception}");
            ShowReplayMapPrompt();
            yield break;
        }

        PreparedMapLoad preparedMap = mapTask.Result;
        if(preparedMap == null)
        {
            ShowReplayMapPrompt();
            yield break;
        }

        if(!string.IsNullOrEmpty(preparedMap.CachedPath))
        {
            LoadMapZip(preparedMap.CachedPath);
            yield break;
        }

        if(preparedMap.Stream == null)
        {
            ShowReplayMapPrompt();
            yield break;
        }

        if(preparedMap.Stream.CanSeek)
        {
            preparedMap.Stream.Position = 0;
        }

        ZipReader zipReader = new ZipReader(null, preparedMap.Stream);
        try
        {
            zipReader.Archive = new ZipArchive(zipReader.ArchiveStream, ZipArchiveMode.Read);

            UrlArgHandler.ignoreMapForSharing = preparedMap.IgnoreMapForSharing;
            if(!string.IsNullOrEmpty(preparedMap.MapID))
            {
                UrlArgHandler.LoadedMapID = preparedMap.MapID;
            }
            else UrlArgHandler.LoadedMapURL = preparedMap.URL;

            StartCoroutine(LoadMapCoroutine(zipReader));
        }
        catch(Exception err)
        {
            zipReader.Dispose();

            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to read map zip!");
            Debug.LogWarning($"Unhandled exception loading prepared map: {err.Message}, {err.StackTrace}");

            SetMap(LoadedMap.Empty);
        }
    }


    public IEnumerator LoadMapIDCoroutine(string mapID, string mapHash = null)
    {
        Loading = true;

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedMap(null, mapID, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            Debug.Log("Found map in cache.");
            LoadMapZip(cachedFile.FilePath);
            yield break;
        }
#endif

        Debug.Log($"Getting BeatSaver response for ID: {mapID}");
        LoadingMessage = "Fetching map from BeatSaver";

        using Task<string> apiTask = BeatSaverHandler.GetBeatSaverMapID(mapID);
        yield return new WaitUntil(() => apiTask.IsCompleted);
        
        string mapURL = apiTask.Result;
        if(string.IsNullOrEmpty(mapURL))
        {
            Debug.Log("Empty or nonexistant URL!");
            SetMap(LoadedMap.Empty);
            yield break;
        }

        mapURL = System.Web.HttpUtility.UrlDecode(mapURL);
        StartCoroutine(LoadMapZipURLCoroutine(mapURL, mapID, mapHash));
    }


    private IEnumerator LoadMapFromReplayCoroutine(Replay loadedReplay, bool noProxy = false)
    {
        string mapHash = null;
        if(!string.IsNullOrEmpty(loadedReplay.info?.hash) && loadedReplay.info.hash.Length >= 40)
        {
            //For some reason replay hash fields might have extra text past the hash
            mapHash = loadedReplay.info.hash[..40];
        }

        if(mapHash == null)
        {
            Debug.Log("Invalid hash! Showing manual map selection.");

            ShowReplayMapPrompt();
            yield break;
        }

        Debug.Log($"Searching for map matching replay hash: {mapHash}");

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedMap(null, null, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            //Only use the cache if we know the ID or URL, so the link buttons work
            if(!string.IsNullOrEmpty(cachedFile.ID))
            {
                UrlArgHandler.LoadedMapID = cachedFile.ID;
                Debug.Log($"Found map ID: {cachedFile.ID} in cache.");

                LoadMapZip(cachedFile.FilePath);
                yield break;
            }
            else if(!string.IsNullOrEmpty(cachedFile.URL))
            {
                UrlArgHandler.LoadedMapURL = cachedFile.URL;
                Debug.Log($"Found map URL: {cachedFile.URL} in cache.");

                LoadMapZip(cachedFile.FilePath);
                yield break;
            }
        }
#endif

        Debug.Log($"Getting BeatSaver response for hash: {mapHash}");
        LoadingMessage = "Fetching map from BeatSaver";

        using Task<(string[], string)> apiTask = BeatSaverHandler.GetBeatSaverMapHash(mapHash);
        yield return new WaitUntil(() => apiTask.IsCompleted);

        string[] mapURLs = apiTask.Result.Item1;
        string mapID = apiTask.Result.Item2;
        if(mapURLs == null || mapURLs.Length == 0)
        {
            Debug.Log("Empty or nonexistant URL! Showing manual map selection.");

            ShowReplayMapPrompt();
            yield break;
        }

        for(int i = 0; i < mapURLs.Length; i++)
        {
            mapURLs[i] = System.Web.HttpUtility.UrlDecode(mapURLs[i]);
        }

        StartCoroutine(LoadMapZipURLsCoroutine(mapURLs, mapID, mapHash, noProxy));
    }


    private static Task<PreparedMapLoad> PrepareMapLoadAsync(ResolvedScore resolved, bool noProxy)
    {
        if(resolved == null) return Task.FromResult<PreparedMapLoad>(null);

        string mapHash = GetSourceMapHash(resolved.SourceInfo);
        if(!string.IsNullOrEmpty(resolved.MapURL))
        {
            return PrepareMapURLAsync(resolved.MapURL, resolved.MapID, mapHash, noProxy, false);
        }

        if(!string.IsNullOrEmpty(resolved.MapID))
        {
            return PrepareMapIDAsync(resolved.MapID, mapHash, noProxy);
        }

        if(!string.IsNullOrEmpty(mapHash))
        {
            return PrepareMapHashAsync(mapHash, noProxy);
        }

        if(resolved.SourceInfo?.HasFallbackMap == true)
        {
            return PrepareMapURLAsync(resolved.SourceInfo.FallbackMapDownloadURL, resolved.SourceInfo.FallbackMapID, mapHash, noProxy, false);
        }

        return Task.FromResult<PreparedMapLoad>(null);
    }


    private static async Task<PreparedMapLoad> PrepareMapIDAsync(string mapID, string mapHash, bool noProxy)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedMap(null, mapID, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            return PreparedMapFromCache(cachedFile, mapID, mapHash, false);
        }
#endif

        Debug.Log($"Getting BeatSaver response for ID: {mapID}");
        string mapURL = await BeatSaverHandler.GetBeatSaverMapID(mapID);
        if(string.IsNullOrEmpty(mapURL)) return null;

        mapURL = System.Web.HttpUtility.UrlDecode(mapURL);
        return await PrepareMapURLAsync(mapURL, mapID, mapHash, noProxy, false);
    }


    private static async Task<PreparedMapLoad> PrepareMapHashAsync(string mapHash, bool noProxy)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedMap(null, null, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath) && (!string.IsNullOrEmpty(cachedFile.ID) || !string.IsNullOrEmpty(cachedFile.URL)))
        {
            return PreparedMapFromCache(cachedFile, null, mapHash, true);
        }
#endif

        Debug.Log($"Getting BeatSaver response for hash: {mapHash}");
        (string[] mapURLs, string mapID) = await BeatSaverHandler.GetBeatSaverMapHash(mapHash);
        if(mapURLs == null || mapURLs.Length == 0) return null;

        for(int i = 0; i < mapURLs.Length; i++)
        {
            mapURLs[i] = System.Web.HttpUtility.UrlDecode(mapURLs[i]);
        }

        return await PrepareMapURLsAsync(mapURLs, mapID, mapHash, noProxy, true);
    }


    private static async Task<PreparedMapLoad> PrepareMapURLAsync(string url, string mapID, string mapHash, bool noProxy, bool ignoreMapForSharing)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedMap(url, mapID, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            return PreparedMapFromCache(cachedFile, mapID, mapHash, ignoreMapForSharing);
        }
#endif

        Debug.Log($"Downloading map data from: {url}");
        Stream zipStream = await WebLoader.LoadFileURL(url, noProxy);
        if(zipStream == null) return null;

#if !UNITY_WEBGL || UNITY_EDITOR
        string extraData = mapID == null ? null : "latest";
        CacheManager.SaveMapToCache(zipStream, url, mapID, mapHash, extraData);
#endif

        return new PreparedMapLoad
        {
            Stream = zipStream,
            URL = url,
            MapID = mapID,
            MapHash = mapHash,
            IgnoreMapForSharing = ignoreMapForSharing
        };
    }


    private static async Task<PreparedMapLoad> PrepareMapURLsAsync(
        string[] urls, string mapID, string mapHash, bool noProxy, bool ignoreMapForSharing)
    {
        for(int i = 0; i < urls.Length; i++)
        {
            string url = urls[i];

#if !UNITY_WEBGL || UNITY_EDITOR
            CachedFile cachedFile = CacheManager.GetCachedMap(url, mapID, mapHash);
            if(!string.IsNullOrEmpty(cachedFile?.FilePath))
            {
                return PreparedMapFromCache(cachedFile, mapID, mapHash, ignoreMapForSharing);
            }
#endif

            Debug.Log($"Downloading map data from: {url}");
            Stream zipStream = await WebLoader.LoadFileURL(url, noProxy, false);
            if(zipStream == null)
            {
                Debug.LogWarning("Downloaded data is null!");
                continue;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            CacheManager.SaveMapToCache(zipStream, url, mapID, mapHash);
#endif

            return new PreparedMapLoad
            {
                Stream = zipStream,
                URL = url,
                MapID = mapID,
                MapHash = mapHash,
                IgnoreMapForSharing = ignoreMapForSharing
            };
        }

        return null;
    }


    private static PreparedMapLoad PreparedMapFromCache(CachedFile cachedFile, string mapID, string mapHash, bool ignoreMapForSharing)
    {
        return new PreparedMapLoad
        {
            CachedPath = cachedFile.FilePath,
            URL = cachedFile.URL,
            MapID = string.IsNullOrEmpty(cachedFile.ID) ? mapID : cachedFile.ID,
            MapHash = mapHash,
            IgnoreMapForSharing = ignoreMapForSharing
        };
    }


    private static string GetSourceMapHash(ReplaySourceInfo sourceInfo)
    {
        string mapHash = sourceInfo?.MapHash;
        if(!string.IsNullOrEmpty(mapHash) && mapHash.Length > 40)
        {
            mapHash = mapHash[..40];
        }

        return mapHash;
    }


    private void ShowReplayMapPrompt()
    {
        Debug.Log("No map download succeeded! Showing manual map selection.");
        Loading = false;
        LoadingMessage = "";

        OnReplayMapPrompt?.Invoke();
    }


    private IEnumerator SetReplayCoroutine(
        Replay replay, string mapURL = null, string mapID = null, bool noProxy = false, Task<PreparedMapLoad> mapTask = null)
    {
        ReplaySourceInfo sourceInfo = ReplayManager.SourceInfo;

        sourceInfo?.ApplyTo(replay);

        ReplayManager.SetReplay(replay);

        Task sourceDataTask = LoadSourceDataAsync(sourceInfo, replay);
        if(mapTask != null)
        {
            StartCoroutine(LoadPreparedMapCoroutine(mapTask));
            yield break;
        }

        string mapHash = replay.info.hash;
        if(!string.IsNullOrEmpty(mapHash) && mapHash.Length > 40)
        {
            mapHash = mapHash[..40];
        }

        if(!string.IsNullOrEmpty(mapID))
        {
            Debug.Log($"Loading map from preset ID: {mapID}");
            UrlArgHandler.LoadedMapID = mapID;
            StartCoroutine(LoadMapIDCoroutine(mapID, mapHash));
        }
        else if(!string.IsNullOrEmpty(mapURL))
        {
            Debug.Log($"Loading map from preset URL: {mapURL}");
            UrlArgHandler.LoadedMapURL = mapURL;
            StartCoroutine(LoadMapZipURLCoroutine(mapURL, mapID, mapHash, noProxy));
        }
        else if(string.IsNullOrEmpty(replay.info?.hash) || replay.info.hash.Length < 40)
        {
            if(sourceInfo != null && !sourceInfo.HasFallbackMap)
            {
                LoadingMessage = "Loading player profile";
                yield return new WaitUntil(() => sourceDataTask.IsCompleted);
            }

            if(sourceInfo != null && sourceInfo.HasFallbackMap)
            {
                if(!string.IsNullOrEmpty(sourceInfo.FallbackMapID))
                {
                    UrlArgHandler.LoadedMapID = sourceInfo.FallbackMapID;
                }
                else UrlArgHandler.LoadedMapURL = sourceInfo.FallbackMapDownloadURL;

                StartCoroutine(LoadMapZipURLCoroutine(sourceInfo.FallbackMapDownloadURL, sourceInfo.FallbackMapID, mapHash, noProxy));
            }
            else StartCoroutine(LoadMapFromReplayCoroutine(replay, noProxy));
        }
        else StartCoroutine(LoadMapFromReplayCoroutine(replay, noProxy));
    }


    private static Task LoadSourceDataAsync(ReplaySourceInfo sourceInfo, Replay replay)
    {
        return sourceInfo?.LoadSourceData?.Invoke(replay) ?? Task.CompletedTask;
    }


#if !UNITY_WEBGL || UNITY_EDITOR
    private IEnumerator LoadReplayDirectoryCoroutine(string directory, string mapURL = null)
    {
        Loading = true;

        Debug.Log($"Loading replay from directory: {directory}");
        LoadingMessage = "Loading replay";

        using Task<Replay> replayTask = Task.Run(() => ReplayLoader.ReplayFromDirectory(directory));
        yield return new WaitUntil(() => replayTask.IsCompleted);

        Replay replay = replayTask.Result;
        if(replay == null)
        {
            SetMap(LoadedMap.Empty);
            yield break;
        }

        StartCoroutine(SetReplayCoroutine(replay, mapURL));
    }
#else


    private IEnumerator LoadReplayDirectoryWebGLCoroutine(string directory)
    {
        Loading = true;
        LoadingMessage = "Loading replay";

        Debug.Log("Starting web request.");
        using UnityWebRequest uwr = UnityWebRequest.Get(directory);
        yield return uwr.SendWebRequest();

        if(uwr.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(uwr.error);
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to load replay! {uwr.error}");

            SetMap(LoadedMap.Empty);
            yield break;
        }

        using Task<Replay> replayTask = ReplayLoader.ReplayFromStream(new MemoryStream(uwr.downloadHandler.data));
        yield return new WaitUntil(() => replayTask.IsCompleted);

        Replay replay = replayTask.Result;
        if(replay == null)
        {
            Debug.LogWarning($"Failed to read replay data!");
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to read replay data!");

            SetMap(LoadedMap.Empty);
            yield break;
        }

        StartCoroutine(SetReplayCoroutine(replay));
    }


    public void LoadReplayDirectoryWebGL(string directory)
    {
        if(DialogueHandler.DialogueActive)
        {
            return;
        }

        if(Loading)
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "You're already loading something!");
            Debug.LogWarning("Trying to load a replay while already loading!");
            return;
        }

        StartCoroutine(LoadReplayDirectoryWebGLCoroutine(directory));
        UrlArgHandler.LoadedReplayURL = null;
    }
#endif


    public IEnumerator LoadReplayURLCoroutine(
        string url, string id = null, string mapURL = null, string mapID = null, bool noProxy = false, Task<PreparedMapLoad> mapTask = null)
    {
        Loading = true;
        Debug.Log($"Searching for replay from: {url}");

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = CacheManager.GetCachedReplay(url);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            Debug.Log("Found replay in cache.");
            StartCoroutine(LoadReplayDirectoryCoroutine(cachedFile.FilePath, cachedFile.ExtraData));
            yield break;
        }
#endif

        LoadingMessage = "Downloading replay";

        using Task<Stream> downloadTask = WebLoader.LoadFileURL(url, noProxy);
        yield return new WaitUntil(() => downloadTask.IsCompleted);

        using Stream replayStream = downloadTask.Result;
        if(replayStream == null)
        {
            Debug.LogWarning("Downloaded replay is null!");

            SetMap(LoadedMap.Empty);
            yield break;
        }

        using Task<Replay> decodeTask = ReplayLoader.ReplayFromStream(replayStream);
        yield return new WaitUntil(() => decodeTask.IsCompleted);

        Replay replay = decodeTask.Result;
        if(replay == null)
        {
            Debug.LogWarning("Failed to decode replay!");
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to decode the replay!");
            SetMap(LoadedMap.Empty);
            yield break;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        CacheManager.SaveReplayToCache(replayStream, url, id, mapURL);
#endif

        StartCoroutine(SetReplayCoroutine(replay, mapURL, mapID, noProxy, mapTask));
    }


    public IEnumerator LoadScoreSaberReplayIDCoroutine(string id, string mapURL = null, string mapID = null, bool noProxy = false)
    {
        yield return LoadReplayFromScoreCoroutine(
            ReplaySourceType.ScoreSaber, id, mapURL, mapID, noProxy);
    }


    public IEnumerator LoadReplayIDAutoCoroutine(string id, string mapURL = null, string mapID = null, bool noProxy = false)
    {
        Loading = true;
        Debug.Log($"Searching for replay from score ID: {id}");

        LoadingMessage = "Fetching replay";

        Task<ResolvedScore> scoreSaberTask = ResolveReplayScoreAsync(
            ReplaySourceType.ScoreSaber, id, mapURL, mapID, false);
        yield return new WaitUntil(() => scoreSaberTask.IsCompleted);

        ResolvedScore resolved = scoreSaberTask.Result;

        if(resolved == null || string.IsNullOrEmpty(resolved.ReplayURL))
        {
            Debug.Log($"Empty or nonexistent replay URL for score ID: {id}");
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Couldn't find a replay for score {id}!");
            SetMap(LoadedMap.Empty);
            yield break;
        }

        UrlArgHandler.LoadedSSScoreId = id;

        if(resolved.SourceInfo != null)
        {
            ReplayManager.SourceInfo = resolved.SourceInfo;
        }

        Task<PreparedMapLoad> mapTask = PrepareMapLoadAsync(resolved, noProxy);
        StartCoroutine(LoadReplayURLCoroutine(resolved.ReplayURL, null, resolved.MapURL, resolved.MapID, noProxy, mapTask));
    }


    private IEnumerator LoadReplayFromScoreCoroutine(ReplaySourceType sourceType, string id, string mapURL, string mapID, bool noProxy)
    {
        Loading = true;
        Debug.Log($"Searching for replay from {sourceType} score ID: {id}");

        LoadingMessage = $"Fetching replay from {sourceType}";

        Task<ResolvedScore> resolveTask = ResolveReplayScoreAsync(sourceType, id, mapURL, mapID);
        yield return new WaitUntil(() => resolveTask.IsCompleted);

        ResolvedScore resolved = resolveTask.Result;
        if(resolved == null || string.IsNullOrEmpty(resolved.ReplayURL))
        {
            Debug.Log($"Empty or nonexistent {sourceType} replay URL!");
            SetMap(LoadedMap.Empty);
            yield break;
        }

        if(resolved.SourceInfo != null)
        {
            ReplayManager.SourceInfo = resolved.SourceInfo;
        }

        Task<PreparedMapLoad> mapTask = PrepareMapLoadAsync(resolved, noProxy);
        StartCoroutine(LoadReplayURLCoroutine(resolved.ReplayURL, null, resolved.MapURL, resolved.MapID, noProxy, mapTask));
    }


    private static Task<ResolvedScore> ResolveReplayScoreAsync(
        ReplaySourceType sourceType, string id, string mapURL, string mapID, bool showErrors = true)
    {
        return sourceType switch
        {
            ReplaySourceType.ScoreSaber => ScoreSaberSource.ResolveScoreAsync(id, mapURL, mapID, showErrors),
            _ => Task.FromResult<ResolvedScore>(null)
        };
    }


    private void SetMap(LoadedMap newMap)
    {
        StopAllCoroutines();
        LoadingMessage = "";
        Loading = false;
        
        if(newMap.Info == null || newMap.Difficulties.Count == 0 || newMap.Song == null)
        {
            Debug.LogWarning("Failed to load map file.");

            if(newMap.Song != null)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                newMap.Song.UnloadAudioData();
                Destroy(newMap.Song);
#else
                newMap.Song.Dispose();
#endif
            }
            UIStateManager.CurrentState = UIState.MapSelection;
            OnLoadingFailed?.Invoke();

            return;
        }

        UIStateManager.CurrentState = UIState.Previewer;
        
        BeatmapManager.Info = newMap.Info;
        SongManager.Instance.MusicClip = newMap.Song;

        if(newMap.CoverImageData != null && newMap.CoverImageData.Length > 0)
        {
            CoverImageHandler.Instance.SetImageFromData(newMap.CoverImageData);
        }
        else CoverImageHandler.Instance.ClearImage();

        BeatmapManager.SetDifficulties(newMap.Difficulties);
        BeatmapManager.CurrentDifficulty = BeatmapManager.GetDefaultDifficulty();

        OnMapLoaded?.Invoke();
    }


    public void LoadMapDirectory(string directory)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        throw new InvalidOperationException("Loading from directory doesn't work on WebGL!");
#else

        if(File.Exists(directory))
        {
            if(directory.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
            {
                LoadMapZip(directory);
                HotReloader.loadedMapPath = directory;
                return;
            }

            if(directory.EndsWith(".bsor", StringComparison.InvariantCultureIgnoreCase))
            {
                StartCoroutine(LoadReplayDirectoryCoroutine(directory));
                return;
            }

            if(directory.EndsWith(".dat", StringComparison.InvariantCultureIgnoreCase))
            {
                //Check if this is a ScoreSaber replay file
                if(ScoreSaberDecoder.IsScoreSaberFile(directory))
                {
                    StartCoroutine(LoadReplayDirectoryCoroutine(directory));
                    return;
                }

                //User is trying to load an unzipped map, get the parent directory
                DirectoryInfo parentDir = Directory.GetParent(directory);
                FileReader fileReader = new FileReader(parentDir.FullName);
                StartCoroutine(LoadMapCoroutine(fileReader));
                HotReloader.loadedMapPath = parentDir.FullName;
            }
        }
        else if(Directory.Exists(directory))
        {
            FileReader fileReader = new FileReader(directory);
            StartCoroutine(LoadMapCoroutine(fileReader));
            HotReloader.loadedMapPath = directory;
        }
        else
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "That file or directory doesn't exist!");
            Debug.LogWarning($"Trying to load a map from a file that doesn't exist!");
        }
#endif
    }


    public void LoadMapInput(string input)
    {
        if(DialogueHandler.DialogueActive)
        {
            Debug.LogWarning("Trying to load a map while in a dialogue!");
            return;
        }

        if(Loading)
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "You're already loading something!");
            Debug.LogWarning("Trying to load a map while already loading!");
            return;
        }

        if(UIStateManager.CurrentState != UIState.MapSelection)
        {
            UIStateManager.CurrentState = UIState.MapSelection;
        }

        if(!ReplayManager.IsReplayMode)
        {
            HotReloader.loadedMapPath = null;
        }
        UrlArgHandler.ignoreMapForSharing = false;

        string decodedURL = System.Web.HttpUtility.UrlDecode(input);
        if(decodedURL.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase) || input.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase))
        {
            Uri uri = new Uri(decodedURL);
            string noQuery = uri.GetLeftPart(UriPartial.Path);

            if(noQuery.Contains("beatsaver.com/maps"))
            {
                //Direct beatsaver link, should load based on ID instead
                string ID = noQuery.Split("/").Last();
                StartCoroutine(LoadMapIDCoroutine(ID));

                UrlArgHandler.LoadedMapID = ID;
                return;
            }

            if(noQuery.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
            {
                StartCoroutine(LoadMapZipURLCoroutine(decodedURL));
                UrlArgHandler.LoadedMapURL = decodedURL;
                return;
            }

            if(!ReplayManager.IsReplayMode && noQuery.EndsWith(".bsor", StringComparison.InvariantCultureIgnoreCase))
            {
                StartCoroutine(LoadReplayURLCoroutine(decodedURL));
                UrlArgHandler.LoadedReplayURL = decodedURL;
                return;
            }

            Debug.LogWarning($"{decodedURL} doesn't link to a valid map!");
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Invalid URL!");
            return;
        }

        if(!ReplayManager.IsReplayMode && SettingsManager.GetBool("replaymode"))
        {
            string replayInput = input.Trim();
            const string scoreSaberPrefix = "ss:";
            const string scoreSaberLongPrefix = "scoresaber:";

            if(replayInput.StartsWith(scoreSaberPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                string scoreID = replayInput[scoreSaberPrefix.Length..].Trim();
                if(!string.IsNullOrEmpty(scoreID) && scoreID.All(char.IsDigit))
                {
                    StartCoroutine(LoadScoreSaberReplayIDCoroutine(scoreID));
                    UrlArgHandler.LoadedSSScoreId = scoreID;
                    return;
                }
            }

            if(replayInput.StartsWith(scoreSaberLongPrefix, StringComparison.InvariantCultureIgnoreCase))
            {
                string scoreID = replayInput[scoreSaberLongPrefix.Length..].Trim();
                if(!string.IsNullOrEmpty(scoreID) && scoreID.All(char.IsDigit))
                {
                    StartCoroutine(LoadScoreSaberReplayIDCoroutine(scoreID));
                    UrlArgHandler.LoadedSSScoreId = scoreID;
                    return;
                }
            }

            if(!input.Any(x => !char.IsDigit(x)))
            {
                StartCoroutine(LoadReplayIDAutoCoroutine(input));
                return;
            }
        }
        else
        {
            const string IDchars = "0123456789abcdef";
            //If the directory doesn't contain any characters that aren't hexadecimal, that means it's probably an ID
            if(!input.ToLower().Any(x => !IDchars.Contains(x)))
            {
                StartCoroutine(LoadMapIDCoroutine(input));
                UrlArgHandler.LoadedMapID = input;
                return;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        //Loading files from string directories doesn't work in WebGL
        ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Invalid URL!");
#else
        UrlArgHandler.LoadedMapURL = null;
        LoadMapDirectory(input);
#endif
    }


    public void CancelMapLoading()
    {
        SetMap(LoadedMap.Empty);
    }


    private struct ScheduledDifficulty
    {
        //Just a container for concurrent difficulty loading
        public DifficultyBeatmap Beatmap;
        public DifficultyCharacteristic Characteristic;
        public byte[] diffData;
    }
}


public class LoadedMap
{
#if !UNITY_WEBGL || UNITY_EDITOR
    public LoadedMap(LoadedMapData mapData, byte[] coverImageData, AudioClip song)
#else
    public LoadedMap(LoadedMapData mapData, byte[] coverImageData, WebSongClip song)
#endif
    {
        MapData = mapData;
        CoverImageData = coverImageData;
        Song = song;
    }

    public LoadedMapData MapData { get; private set; }
    public BeatmapInfo Info => MapData.Info;
    public List<Difficulty> Difficulties => MapData.Difficulties;
    public byte[] CoverImageData { get; private set; }
#if !UNITY_WEBGL || UNITY_EDITOR
    public AudioClip Song { get; private set; }
#else
    public WebSongClip Song { get; private set; }
#endif

    public static LoadedMap Empty => new LoadedMap(LoadedMapData.Empty, null, null);
}


public class LoadedMapData
{
    public LoadedMapData(BeatmapInfo info)
    {
        Info = info;
        Difficulties = new List<Difficulty>();

        BpmEvents = null;
        Lightshows = null;
    }

    public BeatmapInfo Info;
    public List<Difficulty> Difficulties;

    public BeatmapBpmEvent[] BpmEvents;
    public Dictionary<string, BeatmapLightshowV4> Lightshows;

    public static LoadedMapData Empty => new LoadedMapData(null);


    public BeatmapLightshowV4 GetLightshow(string lightshowFilename)
    {
        if(Lightshows == null)
        {
            return new BeatmapLightshowV4();
        }

        if(Lightshows.TryGetValue(lightshowFilename, out BeatmapLightshowV4 lightshow))
        {
            return lightshow;
        }
        else return new BeatmapLightshowV4();
    }
}


public interface IMapDataLoader : IDisposable
{
    public Task<LoadedMap> GetMap();
    public Task<LoadedMapData> GetMapData();
}


public interface IReplayLoader : IMapDataLoader
{
    public Task<Replay> GetReplay();
}