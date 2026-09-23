using System;
using System.Collections.Generic;
using System.IO;

namespace IfredrixDownloadManager;

/// <summary>One installable browser target.</summary>
public sealed class BrowserTarget
{
    public BrowserTarget(string id, string display, string extensionDir, string extensionsPage)
    {
        Id = id;
        Display = display;
        ExtensionDir = extensionDir;
        ExtensionsPage = extensionsPage;
    }

    public string Id { get; }
    public string Display { get; }
    public string ExtensionDir { get; }
    public string ExtensionsPage { get; }
    public string? ExePath { get; set; }
    public bool IsInstalled => !string.IsNullOrEmpty(ExePath);
}

/// <summary>Detects installed browsers from their default install paths.</summary>
public static class BrowserDetector
{
    public static List<BrowserTarget> Detect(string extensionRoot)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var targets = new List<BrowserTarget>
        {
            new("chrome", "Google Chrome", Path.Combine(extensionRoot, "chrome"), "chrome://extensions"),
            new("edge", "Microsoft Edge", Path.Combine(extensionRoot, "edge"), "edge://extensions"),
            new("brave", "Brave", Path.Combine(extensionRoot, "chrome"), "brave://extensions"),
            new("vivaldi", "Vivaldi", Path.Combine(extensionRoot, "chrome"), "vivaldi://extensions"),
            new("opera", "Opera", Path.Combine(extensionRoot, "opera"), "opera://extensions"),
            new("firefox", "Mozilla Firefox", Path.Combine(extensionRoot, "firefox"), "about:debugging#/runtime/this-firefox"),
        };

        string? FirstExisting(params string[] paths)
        {
            foreach (var p in paths)
            {
                try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p; } catch { }
            }
            return null;
        }

        targets[0].ExePath = FirstExisting(
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"));
        targets[1].ExePath = FirstExisting(
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"));
        targets[2].ExePath = FirstExisting(
            Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
        targets[3].ExePath = FirstExisting(
            Path.Combine(localAppData, "Vivaldi", "Application", "vivaldi.exe"),
            Path.Combine(programFiles, "Vivaldi", "Application", "vivaldi.exe"));
        targets[4].ExePath = FirstExisting(
            Path.Combine(localAppData, "Programs", "Opera", "opera.exe"),
            Path.Combine(programFiles, "Opera", "opera.exe"),
            Path.Combine(programFilesX86, "Opera", "opera.exe"));
        targets[5].ExePath = FirstExisting(
            Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
            Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe"));

        return targets;
    }
}
