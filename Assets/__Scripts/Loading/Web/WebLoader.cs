using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public static class ApiConfig
{
    private const string DefaultBeatLeaderBaseURL = "https://beatleader.com/";
    private const string DefaultScoreSaberBaseURL = "https://scoresaber.com/";

    public static readonly string BeatLeaderBaseURL = GetURL(DefaultBeatLeaderBaseURL);
    public static readonly string ScoreSaberBaseURL = GetURL(DefaultScoreSaberBaseURL);
    public static readonly string BeatLeaderApiURL = GetSubdomainURL(BeatLeaderBaseURL, "api");
    public static readonly string ScoreSaberApiURL = GetPathURL(ScoreSaberBaseURL, "api/v2/");

    public static readonly string[] CorsURLs =
    {
        BeatLeaderBaseURL,
        BeatLeaderApiURL,
        ScoreSaberBaseURL,
        ScoreSaberApiURL
    };

    private static string GetURL(string defaultURL)
    {
        return defaultURL.EndsWith("/") ? defaultURL : $"{defaultURL}/";
    }

    private static string GetSubdomainURL(string baseURL, string subdomain)
    {
        Uri uri = new Uri(baseURL);
        UriBuilder builder = new UriBuilder(uri)
        {
            Host = $"{subdomain}.{uri.Host}",
            Path = ""
        };

        return builder.Uri.ToString();
    }

    private static string GetPathURL(string baseURL, string path)
    {
        return new Uri(new Uri(baseURL), path).ToString();
    }
}

#pragma warning disable CS4014 //Suppress warnings about lack of await for uwr.SendWebRequest()
public class WebLoader
{
    public const string CorsProxy = "https://cors.bsmg.dev/";

    //Domains listed in this array will bypass the CORS proxy
    //Map sources that include CORS headers should be added here for faster downloads
    private static readonly string[] DefaultWhitelistURLs = new string[]
    {
        "https://r2cdn.beatsaver.com",
        "https://cdn.beatsaver.com",
        "https://api.beatleader.xyz",
        "https://cdn.replays.beatleader.xyz/",
        "https://api.beatleader.com",
        "https://cdn.replays.beatleader.com/",
        "https://cdn.songs.beatleader.xyz/",
        "https://cdn.songs.beatleader.com/",
        "https://scoresaber.com",
        "https://cdn.scoresaber.com"
    };

    public static string[] WhitelistURLs => DefaultWhitelistURLs
        .Concat(ApiConfig.CorsURLs)
        .Where(x => !string.IsNullOrEmpty(x))
        .Distinct()
        .ToArray();

    public static ulong DownloadSize;
    public static UnityWebRequest uwr;
    private static readonly List<UnityWebRequest> ActiveRequests = new List<UnityWebRequest>();


    public static string GetCorsURL(string url)
    {
        if(WhitelistURLs.Any(x => url.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
        {
            return url;
        }

        Debug.Log($"Downloading via CORS proxy.");
        return CorsProxy + url;
    }


    public static async Task<Stream> LoadFileURL(string url, bool noProxy, bool sendError = true)
    {
        await Task.Yield();
        return await StreamFromURL(url, noProxy, sendError);
    }


    public static async Task<MemoryStream> StreamFromURL(string url, bool noProxy, bool sendError = true)
    {
        MapLoader.Progress = 0;
        DownloadSize = 0;

#if UNITY_WEBGL && !UNITY_EDITOR
        if(!noProxy)
        {
            url = GetCorsURL(url);
        }
        else
        {
            Debug.Log("CORS proxy is disabled.");
        }
#endif

        UnityWebRequest request = null;
        try
        {
            request = UnityWebRequest.Get(url);
            ActiveRequests.Add(request);
            uwr = request;

            Debug.Log("Starting download.");
            request.SendWebRequest();

            while(!request.isDone)
            {
                if(DownloadSize == 0)
                {
                    //GetRequestHeader returns the file size in a string,
                    //or null if the headers haven't been receieved yet
                    string sizeHeader = request.GetResponseHeader("Content-Length");

                    ulong outValue;
                    DownloadSize = ulong.TryParse(sizeHeader, out outValue) ? outValue : 0;
                }

                MapLoader.Progress = request.downloadProgress;

                await Task.Yield();
            }

            if(request.result != UnityWebRequest.Result.Success)
            {
                if(request.error == "Request aborted")
                {
                    Debug.Log("Download cancelled.");
                    if(sendError)
                    {
                        ErrorHandler.Instance.QueuePopup(ErrorType.Notification, "Download cancelled!");
                    }
                }
                else
                {
                    Debug.LogWarning($"{request.error}");
                    if(sendError)
                    {
                        ErrorHandler.Instance.QueuePopup(ErrorType.Error, $"Download failed! {request.error}");
                    }
                }

                return null;
            }
            else
            {
                return new MemoryStream(request.downloadHandler.data);
            }
        }
        catch(Exception e)
        {
            Debug.LogWarning($"Download failed with exception: {e.Message}, {e.StackTrace}");
        }
        finally
        {
            if(request != null)
            {
                ActiveRequests.Remove(request);
                request.Dispose();
                uwr = ActiveRequests.Count > 0 ? ActiveRequests[^1] : null;
            }
            MapLoader.Progress = 0;
        }
        
        return null;
    }


    public static void CancelDownload()
    {
        foreach(UnityWebRequest request in ActiveRequests)
        {
            if(request != null && !request.isDone)
            {
                request.Abort();
            }
        }
    }
}