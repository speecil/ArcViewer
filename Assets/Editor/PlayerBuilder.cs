using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class PlayerBuilder
{
    [MenuItem("Build/WebGL Dev (fast)")]   public static void WebGLDev()      => BuildFor("webgl",   "dev");
    [MenuItem("Build/WebGL Release")]      public static void WebGLRelease()  => BuildFor("webgl",   "release");
    [MenuItem("Build/Win64 Dev")]          public static void Win64Dev()      => BuildFor("win64",   "dev");
    [MenuItem("Build/Win64 Release")]      public static void Win64Release()  => BuildFor("win64",   "release");
    [MenuItem("Build/Linux64 Dev")]        public static void LinuxDev()      => BuildFor("linux64", "dev");
    [MenuItem("Build/Linux64 Release")]    public static void LinuxRelease()  => BuildFor("linux64", "release");
    [MenuItem("Build/macOS Dev")]          public static void MacDev()        => BuildFor("macos",   "dev");
    [MenuItem("Build/macOS Release")]      public static void MacRelease()    => BuildFor("macos",   "release");

    public static void Build()
    {
        string platform = Environment.GetEnvironmentVariable("ARCVIEWER_PLATFORM");
        string mode = Environment.GetEnvironmentVariable("ARCVIEWER_MODE");
        if(string.IsNullOrEmpty(platform)) platform = "webgl";
        if(string.IsNullOrEmpty(mode)) mode = "dev";
        BuildFor(platform, mode);
    }

    static void BuildFor(string platform, string mode)
    {
        bool dev = mode == "dev";
        EditorUserBuildSettings.development = dev;

        BuildTarget target;
        NamedBuildTarget named;
        string output;
        switch(platform)
        {
            case "webgl":
                target = BuildTarget.WebGL;
                named = NamedBuildTarget.WebGL;
                output = "Build";
                ApplyWebGL(dev);
                break;
            case "win64":
                target = BuildTarget.StandaloneWindows64;
                named = NamedBuildTarget.Standalone;
                output = "Build/win64/ArcViewer.exe";
                break;
            case "linux64":
                target = BuildTarget.StandaloneLinux64;
                named = NamedBuildTarget.Standalone;
                output = "Build/linux64/ArcViewer";
                break;
            case "macos":
                target = BuildTarget.StandaloneOSX;
                named = NamedBuildTarget.Standalone;
                output = "Build/macos/ArcViewer.app";
                break;
            default:
                throw new ArgumentException($"unknown platform: {platform}");
        }

        PlayerSettings.SetIl2CppCodeGeneration(named, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetManagedStrippingLevel(named, dev ? ManagedStrippingLevel.Minimal : ManagedStrippingLevel.Low);

        EditorBuildSettingsScene[] built = EditorBuildSettings.scenes;
        string[] scenes = new string[built.Length];
        for(int i = 0; i < built.Length; i++) scenes[i] = built[i].path;

        BuildOptions options = BuildOptions.None;
        if(dev) options |= BuildOptions.Development | BuildOptions.CompressWithLz4;

        BuildReport report = BuildPipeline.BuildPlayer(scenes, output, target, options);
        if(target == BuildTarget.WebGL && report.summary.result == BuildResult.Succeeded) ApplyWebGLCacheBust(output);
    }

    static void ApplyWebGL(bool dev)
    {
        if(dev)
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.analyzeBuildSize = false;
        }
        else
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        }
    }

    static void ApplyWebGLCacheBust(string output)
    {
        string outputPath = Path.GetFullPath(output);
        string buildKey = GetWebGLBuildKey(outputPath);
        string indexFile = Path.Combine(outputPath, "index.html");
        string styleFile = Path.Combine(outputPath, "TemplateData", "style.css");

        UpdateFileUrls(indexFile, buildKey, @"(?<quote>[""'])(?<url>(?:Build|TemplateData)/[^""'#?]+)(?:\?v=[^""'#]*)?\k<quote>");
        UpdateFileUrls(styleFile, buildKey, @"url\((?<quote>[""']?)(?<url>[^)""'#?]+)(?:\?v=[^)""'#]*)?\k<quote>\)");
    }

    static string GetWebGLBuildKey(string outputPath)
    {
        string[] hashInputs = new[]
        {
            Path.Combine(outputPath, "Build"),
            Path.Combine(outputPath, "TemplateData")
        };

        using(SHA256 sha = SHA256.Create())
        {
            IOrderedEnumerable<string> files = hashInputs
                .Where(Directory.Exists)
                .SelectMany(path => Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                .OrderBy(path => path);

            foreach(string file in files)
            {
                string relativePath = file
                    .Substring(outputPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                byte[] nameBytes = Encoding.UTF8.GetBytes(relativePath);
                sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
                sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);

                byte[] fileBytes = File.ReadAllBytes(file);
                sha.TransformBlock(fileBytes, 0, fileBytes.Length, null, 0);
                sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").Substring(0, 12).ToLowerInvariant();
        }
    }

    static void UpdateFileUrls(string file, string buildKey, string pattern)
    {
        if(!File.Exists(file)) return;

        string text = File.ReadAllText(file);
        text = Regex.Replace(text, pattern, match =>
        {
            string quote = match.Groups["quote"].Value;
            string url = match.Groups["url"].Value;
            if(match.Value.StartsWith("url(")) return $"url({quote}{url}?v={buildKey}{quote})";
            return $"{quote}{url}?v={buildKey}{quote}";
        });
        File.WriteAllText(file, text);
    }
}
