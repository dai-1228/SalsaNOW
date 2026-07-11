using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Win32;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SalsaNOW
{
    internal static class AppInstaller
    {
        // Parallel installation of user-defined apps from remote and local JSON sources
        public static async Task AppsInstallAsync(string globalDirectory, string customAppsJsonPath)
        {
            const string jsonUrl = "https://salsanowfiles.work/jsons/apps.json";
            try
            {
                List<Apps> apps;
                using (var wc = new WebClient())
                {
                    string json = await wc.DownloadStringTaskAsync(jsonUrl);
                    apps = JsonConvert.DeserializeObject<List<Apps>>(json);
                }

                // Load custom apps from local JSON if provided via arguments
                if (!string.IsNullOrEmpty(customAppsJsonPath) && System.IO.File.Exists(customAppsJsonPath))
                {
                    try
                    {
                        var customApps = JsonConvert.DeserializeObject<List<Apps>>(System.IO.File.ReadAllText(customAppsJsonPath));
                        if (customApps != null) apps.AddRange(customApps);
                    }
                    catch (Exception ex) { SalsaLogger.Error($"Custom JSON Error: {ex.Message}"); }
                }

                var finalNames = new[] { "discord", "roblox", "helium", "waterfox" };
                apps.RemoveAll(a => finalNames.Any(f => a.name.ToLower().Contains(f)));
                try {
                    string wf = Path.Combine(globalDirectory, "Waterfox");
                    if (Directory.Exists(wf)) Directory.Delete(wf, true);
                } catch { }

                var tasks = apps.Select(app => Task.Run(async () =>
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.Headers.Add("Cache-Control", "no-cache");
                        webClient.Headers.Add("Pragma", "no-cache");

                        string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{app.name}.lnk");
                        string appDir = Path.Combine(globalDirectory, app.name);
                        string appExePath = Path.Combine(globalDirectory, app.exeName);
                        string appZipExe = Path.Combine(appDir, app.exeName);

                        bool isZip = app.fileExtension == "zip";
                        bool isExe = app.fileExtension == "exe";
                        
                        bool alreadyExists = (isZip && Directory.Exists(appDir)) || (isExe && System.IO.File.Exists(appExePath));

                        // Initial installation for missing applications
                        if (!alreadyExists)
                        {
                            SalsaLogger.Info("Installing " + app.name);
                            if (isZip)
                            {
                                string zipPath = $"{appDir}.zip";
                                await webClient.DownloadFileTaskAsync(new Uri(app.url), zipPath);
                                ZipFile.ExtractToDirectory(zipPath, appDir);
                                System.IO.File.Delete(zipPath);

                                CreateShortcut(app.name, desktopPath, appZipExe, Path.GetDirectoryName(appZipExe));
                                if (app.run == "true") Process.Start(appZipExe);
                            }
                            else if (isExe)
                            {
                                await webClient.DownloadFileTaskAsync(new Uri(app.url), appExePath);
                                CreateShortcut(app.name, desktopPath, appExePath, globalDirectory);
                                if (app.run == "true") Process.Start(appExePath);
                            }
                        }
                        else
                        {
                            SalsaLogger.Info($"{app.name} already exists. Skipping download and respecting user desktop layout.");
                            
                          
                            if (isZip)
                            {
                                if (app.run == "true") Process.Start(appZipExe);
                            }
                            else if (isExe) // We install exe anyway to ensure everything is up to date
                            {
                                await webClient.DownloadFileTaskAsync(new Uri(app.url), appExePath);
                                if (app.run == "true") Process.Start(appExePath);
                            }
                        }
                    }
                })).ToList();

                await Task.WhenAll(tasks);
            }
            catch (Exception ex) { SalsaLogger.Error(ex.Message); }
        }

        // Silent background app deployment with automated cleanup of obsolete files/folders
        public static async Task AppsInstallSilentAsync(string globalDirectory)
        {
            const string jsonUrl = "https://salsanowfiles.work/jsons/silentapps.json";
            string silentAppsPath = Path.Combine(globalDirectory, "SilentApps");

            try
            {
                Directory.CreateDirectory(silentAppsPath);
                List<SilentApps> apps;
                using (var wc = new WebClient())
                {
                    string json = await wc.DownloadStringTaskAsync(jsonUrl);
                    apps = JsonConvert.DeserializeObject<List<SilentApps>>(json);
                }

                // Clean up folders and files that are no longer present in the JSON definition
                var allowedFolders = new HashSet<string>(apps.Where(a => a.archive == "true").Select(a => a.name), StringComparer.OrdinalIgnoreCase);
                var allowedFiles = new HashSet<string>(apps.Where(a => a.fileExtension == "exe" || a.fileExtension == "bat").Select(a => $"{a.fileName}.{a.fileExtension}"), StringComparer.OrdinalIgnoreCase);

                foreach (var dir in Directory.GetDirectories(silentAppsPath))
                {
                    if (!allowedFolders.Contains(Path.GetFileName(dir))) try { Directory.Delete(dir, true); } catch { }
                }
                foreach (var file in Directory.GetFiles(silentAppsPath))
                {
                    if (!allowedFiles.Contains(Path.GetFileName(file))) try { System.IO.File.Delete(file); } catch { }
                }

                var finalNames = new[] { "discord", "roblox", "helium", "waterfox" };
                apps.RemoveAll(a => finalNames.Any(f => a.name.ToLower().Contains(f)));

                var tasks = apps.Select(app => Task.Run(async () =>
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.Headers.Add("Cache-Control", "no-cache");
                        webClient.Headers.Add("Pragma", "no-cache");

                        string appFolder = Path.Combine(silentAppsPath, app.name);
                        string appPath = Path.Combine(silentAppsPath, $"{app.fileName}.{app.fileExtension}");
                        string appZipPath = Path.Combine(appFolder, $"{app.fileName}.{app.fileExtension}");

                        if (app.archive == "true")
                        {
                            if (System.IO.File.Exists(appZipPath)) return;
                            string zip = $"{appFolder}.zip";
                            await webClient.DownloadFileTaskAsync(new Uri(app.url), zip);
                            ZipFile.ExtractToDirectory(zip, appFolder);
                            System.IO.File.Delete(zip);
                            if (app.run == "true") Process.Start(appZipPath);
                        }
                        else
                        {
                            if (!System.IO.File.Exists(appPath))
                            {
                                await webClient.DownloadFileTaskAsync(new Uri(app.url), appPath);
                            }

                            if (app.run == "true")
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = appPath,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WindowStyle = ProcessWindowStyle.Hidden
                                });
                            }
                        }
                    }
                })).ToList();

                await Task.WhenAll(tasks);
            }
            catch (Exception ex) { SalsaLogger.Error(ex.ToString()); }
        }

        private class FinalAppInstallResult
        {
            public string AppName;
            public string DesktopShortcutPath;
            public string FinalTargetExe;
            public string FinalWorkingDir;
            public string FinalArgs;
            public bool ShouldRun;
        }

        // Install Discord, Roblox, Helium at the end with visible screens
        public static async Task InstallFinalAppsAsync(string globalDirectory)
        {
            var explicitApps = new List<Apps>
            {
                new Apps { name = "Discord", exeName = "DiscordSetup.exe",          run = "true", url = "https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x86", fileExtension = "exe" },
                new Apps { name = "Roblox", exeName = "RobloxPlayerLauncher.exe",   run = "true", url = "https://www.roblox.com/download/client?os=win&renderingPlatform=nextjs",                          fileExtension = "exe" },
                new Apps { name = "Helium", exeName = "helium_0.14.4.1_x64-windows.zip", run = "true", url = "https://github.com/imputnet/helium-windows/releases/download/0.14.4.1/helium_0.14.4.1_x64-windows.zip", fileExtension = "zip" }
            };

            var installTasks = explicitApps.Select(app => Task.Run(async () =>
            {
                string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{app.name}.lnk");

                var result = new FinalAppInstallResult
                {
                    AppName = app.name,
                    DesktopShortcutPath = desktopPath,
                    ShouldRun = app.run == "true"
                };

                using (var webClient = new WebClient())
                {
                    webClient.Headers.Add("Cache-Control", "no-cache");
                    webClient.Headers.Add("Pragma", "no-cache");

                    string stagingDir = Path.Combine(Path.GetTempPath(), "SalsaNOWInstallers");
                    try { Directory.CreateDirectory(stagingDir); } catch { }
                    string downloadedPath = Path.Combine(stagingDir, app.exeName);

                    try
                    {
                        await webClient.DownloadFileTaskAsync(new Uri(app.url), downloadedPath);

                        if (app.name.Equals("Helium", StringComparison.OrdinalIgnoreCase))
                        {
                            string heliumDir = Path.Combine(globalDirectory, "Helium");
                            try { if (Directory.Exists(heliumDir)) Directory.Delete(heliumDir, true); } catch { }

                            SalsaLogger.Info($"Extracting Helium portable zip to {heliumDir} (no admin, no default browser registration)");
                            ZipFile.ExtractToDirectory(downloadedPath, heliumDir);

                            string[] candidates = new[]
                            {
                                Path.Combine(heliumDir, "helium.exe"),
                                Path.Combine(heliumDir, "Helium.exe")
                            };
                            result.FinalTargetExe = candidates.FirstOrDefault(p => File.Exists(p));

                            if (result.FinalTargetExe == null)
                            {
                                var dir = new DirectoryInfo(heliumDir);
                                var sub = dir.GetDirectories("*", SearchOption.TopDirectoryOnly)
                                             .SelectMany(d => d.GetFiles("helium.exe", SearchOption.TopDirectoryOnly))
                                             .FirstOrDefault();
                                if (sub != null) result.FinalTargetExe = sub.FullName;
                            }

                            result.FinalWorkingDir = string.IsNullOrEmpty(result.FinalTargetExe) ? heliumDir : Path.GetDirectoryName(result.FinalTargetExe);
                        }
                        else if (app.name.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = downloadedPath,
                                Arguments = "--silent",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };

                            SalsaLogger.Info("Installing Discord silently (per-user, no admin)");
                            var proc = Process.Start(psi);
                            if (proc != null) proc.WaitForExit();

                            string localDiscord = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");

                            SalsaLogger.Info("Waiting for Discord payload to finish downloading...");
                            WaitForFile(Path.Combine(localDiscord, "Update.exe"), 120000);
                            WaitForDirectory(localDiscord, "app-*", 120000);

                            KillProcesses("Discord", "Update");

                            string appsDiscord = Path.Combine(globalDirectory, "Discord");

                            SalsaLogger.Info($"Physically moving Discord install: {localDiscord} -> {appsDiscord}");
                            MoveDirectoryAcrossVolumes(localDiscord, appsDiscord);

                            UpdateDiscordRegistry(appsDiscord);

                            string updateExe = Path.Combine(appsDiscord, "Update.exe");
                            if (File.Exists(updateExe))
                            {
                                result.FinalTargetExe  = updateExe;
                                result.FinalWorkingDir = appsDiscord;
                                result.FinalArgs       = "--processStart Discord.exe";
                            }
                        }
                        else if (app.name.Equals("Roblox", StringComparison.OrdinalIgnoreCase))
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = downloadedPath,
                                UseShellExecute = true,
                                CreateNoWindow = false,
                                WindowStyle = ProcessWindowStyle.Normal
                            };

                            SalsaLogger.Info("Running Roblox launcher (per-user, no admin)");
                            var proc = Process.Start(psi);
                            if (proc != null) proc.WaitForExit();

                            string localRoblox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
                            string versionsDir = Path.Combine(localRoblox, "Versions");

                            SalsaLogger.Info("Waiting for Roblox player to finish downloading...");
                            string rbxExe = WaitForRobloxPlayer(versionsDir, 180000);

                            KillProcesses("RobloxPlayerBeta", "RobloxPlayerLauncher");

                            string appsRoblox = Path.Combine(globalDirectory, "Roblox");

                            SalsaLogger.Info($"Physically moving Roblox install: {localRoblox} -> {appsRoblox}");
                            MoveDirectoryAcrossVolumes(localRoblox, appsRoblox);

                            UpdateRobloxRegistry(appsRoblox);

                            if (!string.IsNullOrEmpty(rbxExe) && File.Exists(rbxExe))
                            {
                                result.FinalTargetExe  = rbxExe.Replace(localRoblox, appsRoblox);
                                result.FinalWorkingDir = Path.GetDirectoryName(result.FinalTargetExe);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SalsaLogger.Error($"Failed to install {app.name}: {ex.Message}");
                    }
                    finally
                    {
                        try { if (File.Exists(downloadedPath)) File.Delete(downloadedPath); } catch { }
                    }
                }

                return result;
            })).ToList();

            var results = await Task.WhenAll(installTasks);

            foreach (var result in results)
            {
                if (!string.IsNullOrEmpty(result.FinalTargetExe) && File.Exists(result.FinalTargetExe))
                {
                    CreateShortcutWithArgs(
                        result.AppName,
                        result.DesktopShortcutPath,
                        result.FinalTargetExe,
                        result.FinalWorkingDir ?? Path.GetDirectoryName(result.FinalTargetExe),
                        result.FinalArgs);
                    SalsaLogger.Info($"Created desktop shortcut for {result.AppName} -> {result.FinalTargetExe} {result.FinalArgs}");
                }
                else
                {
                    SalsaLogger.Warn($"Could not locate installed executable for {result.AppName}; desktop shortcut was not created.");
                }

                if (result.ShouldRun && !string.IsNullOrEmpty(result.FinalTargetExe) && File.Exists(result.FinalTargetExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = result.FinalTargetExe,
                            Arguments = result.FinalArgs,
                            UseShellExecute = true,
                            CreateNoWindow = false,
                            WindowStyle = ProcessWindowStyle.Normal
                        });
                    }
                    catch (Exception ex) { SalsaLogger.Error($"Failed to launch {result.AppName}: {ex.Message}"); }
                }
            }
        }

        private static bool WaitForFile(string path, int timeoutMs)
        {
            int waited = 0;
            const int interval = 1000;
            while (!File.Exists(path) && waited < timeoutMs)
            {
                Thread.Sleep(interval);
                waited += interval;
            }
            bool exists = File.Exists(path);
            SalsaLogger.Info($"WaitForFile({path}) -> {(exists ? "found" : "timeout")} after {waited}ms");
            return exists;
        }

        private static bool WaitForDirectory(string parentDir, string dirGlob, int timeoutMs)
        {
            int waited = 0;
            const int interval = 1000;
            while (waited < timeoutMs)
            {
                try
                {
                    if (Directory.Exists(parentDir))
                    {
                        var match = new DirectoryInfo(parentDir)
                            .GetDirectories(dirGlob, SearchOption.TopDirectoryOnly)
                            .FirstOrDefault(d => Directory.GetFiles(d.FullName, "*.dll", SearchOption.TopDirectoryOnly).Length > 0);
                        if (match != null)
                        {
                            SalsaLogger.Info($"WaitForDirectory({parentDir}\\{dirGlob}) -> found after {waited}ms");
                            return true;
                        }
                    }
                }
                catch { }
                Thread.Sleep(interval);
                waited += interval;
            }
            SalsaLogger.Info($"WaitForDirectory({parentDir}\\{dirGlob}) -> timeout after {waited}ms");
            return false;
        }

        private static string WaitForRobloxPlayer(string versionsDir, int timeoutMs)
        {
            int waited = 0;
            const int interval = 1000;
            while (waited < timeoutMs)
            {
                try
                {
                    if (Directory.Exists(versionsDir))
                    {
                        var versionDir = new DirectoryInfo(versionsDir)
                            .GetDirectories()
                            .OrderByDescending(d => d.LastWriteTime)
                            .FirstOrDefault();
                        if (versionDir != null)
                        {
                            string rbxExe = Path.Combine(versionDir.FullName, "RobloxPlayerBeta.exe");
                            if (File.Exists(rbxExe))
                            {
                                SalsaLogger.Info($"WaitForRobloxPlayer -> found {rbxExe} after {waited}ms");
                                return rbxExe;
                            }
                        }
                    }
                }
                catch { }
                Thread.Sleep(interval);
                waited += interval;
            }
            SalsaLogger.Info($"WaitForRobloxPlayer -> timeout after {waited}ms");
            return null;
        }

        private static void MoveDirectoryAcrossVolumes(string source, string dest)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest)) return;
            if (!Directory.Exists(source))
            {
                SalsaLogger.Warn($"MoveDirectory: source does not exist: {source}");
                return;
            }

            try
            {
                if (Directory.Exists(dest))
                {
                    try { Directory.Delete(dest, true); }
                    catch (Exception ex) { SalsaLogger.Warn($"Could not delete existing dest {dest}: {ex.Message}"); }
                }
            }
            catch { }

            try
            {
                Directory.Move(source, dest);
                SalsaLogger.Info($"Moved directory: {source} -> {dest}");
                return;
            }
            catch (Exception ex)
            {
                SalsaLogger.Warn($"Directory.Move failed (likely cross-volume): {ex.Message}. Falling back to recursive copy+delete.");
            }

            try
            {
                CopyDirectoryRecursive(source, dest);
                try { Directory.Delete(source, true); }
                catch (Exception ex) { SalsaLogger.Warn($"Could not delete source after copy: {ex.Message}"); }
                SalsaLogger.Info($"Copied+deleted directory: {source} -> {dest}");
            }
            catch (Exception ex)
            {
                SalsaLogger.Error($"Recursive copy failed: {ex.Message}");
            }
        }

        private static void CopyDirectoryRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                string target = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, target, true);
            }
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
            {
                string target = Path.Combine(dest, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, target);
            }
        }

        private static void KillProcesses(params string[] processNames)
        {
            foreach (var name in processNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (!p.HasExited) p.Kill();
                        SalsaLogger.Info($"Killed process: {p.ProcessName}");
                    }
                    catch (Exception ex) { SalsaLogger.Warn($"Could not kill {name}: {ex.Message}"); }
                    finally { p.Dispose(); }
                }
            }
        }

        private static void UpdateDiscordRegistry(string newInstallDir)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Discord", writable: true))
                {
                    if (key != null)
                    {
                        string exe       = Path.Combine(newInstallDir, "Discord.exe");
                        string updateExe = Path.Combine(newInstallDir, "Update.exe");
                        try { key.SetValue("InstallLocation", newInstallDir); } catch { }
                        try { key.SetValue("DisplayIcon",     exe); } catch { }
                        try { key.SetValue("UninstallString", $"\"{updateExe}\" --uninstall"); } catch { }
                        try { key.SetValue("ModifyPath",      $"\"{updateExe}\" --update"); } catch { }
                    }
                }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Discord"))
                {
                    try { key.SetValue("InstallLocation", newInstallDir); } catch { }
                }
            }
            catch (Exception ex) { SalsaLogger.Warn($"UpdateDiscordRegistry: {ex.Message}"); }
        }

        private static void UpdateRobloxRegistry(string newInstallDir)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Roblox"))
                {
                    try { key.SetValue("InstallLocation", newInstallDir); } catch { }
                }

                string[] uninstallNames = { "Roblox", "Roblox Player" };
                foreach (var name in uninstallNames)
                {
                    using (var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{name}", writable: true))
                    {
                        if (key == null) continue;
                        try { key.SetValue("InstallLocation", newInstallDir); } catch { }
                        string launcher = Path.Combine(newInstallDir, "RobloxPlayerLauncher.exe");
                        if (File.Exists(launcher))
                        {
                            try { key.SetValue("UninstallString", $"\"{launcher}\" -uninstall"); } catch { }
                            try { key.SetValue("DisplayIcon",     launcher); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { SalsaLogger.Warn($"UpdateRobloxRegistry: {ex.Message}"); }
        }

        private static void CreateShortcutWithArgs(string name, string path, string target, string workDir, string arguments)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                    break;
                }
                catch { Thread.Sleep(200); }
            }

            try
            {
                Type tWsh = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(tWsh);
                var lnk = shell.CreateShortcut(path);
                lnk.TargetPath = target;
                lnk.WorkingDirectory = workDir;
                if (!string.IsNullOrEmpty(arguments)) lnk.Arguments = arguments;
                lnk.Save();
            }
            catch (Exception ex) { SalsaLogger.Error($"Shortcut creation failed for {name}: {ex.Message}"); }
        }

        // Setup for Desktop shells and visual personalization
        public static async Task DesktopInstallAsync(string globalDirectory)
        {
            const string jsonUrl = "https://salsanowfiles.work/jsons/desktop.json";
            
            // Enforce Dark Mode for Windows Apps
            Process.Start(new ProcessStartInfo("cmd.exe", "/c reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize\" /v AppsUseLightTheme /t REG_DWORD /d 0 /f") { UseShellExecute = true });

            try
            {
                List<DesktopInfo> desktopInfo;
                using (var wc = new WebClient())
                {
                    string json = await wc.DownloadStringTaskAsync(jsonUrl);
                    desktopInfo = JsonConvert.DeserializeObject<List<DesktopInfo>>(json);
                }

                bool skipSeelen = SalsaSettings.SkipSeelenUiExecution;
                bool bingWall = SalsaSettings.BingWallpaperEnabled;

                // Terminate original explorer shells to prepare for custom shell injection
                IntPtr hWndSeelen = NativeMethods.FindWindow(null, "CustomExplorer");
                if (hWndSeelen != IntPtr.Zero) NativeMethods.PostMessage(hWndSeelen, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                foreach (var desktop in desktopInfo)
                {
                    string appDir = Path.Combine(globalDirectory, desktop.name);
                    string zipFile = Path.Combine(globalDirectory, $"{desktop.name}.zip");
                    string exePath = Path.Combine(appDir, desktop.exeName);

                    bool needsInstall = !Directory.Exists(appDir) || !File.Exists(exePath);

                    if (needsInstall)
                    {
                        // Clean up broken install if folder exists but exe is missing
                        if (Directory.Exists(appDir))
                        {
                            try
                            {
                                Directory.Delete(appDir, true);
                            }
                            catch
                            {
                                // Optional: retry or log
                            }
                        }

                        using (var wc = new WebClient())
                        {
                            wc.Headers.Add("Cache-Control", "no-cache");
                            wc.Headers.Add("Pragma", "no-cache");

                            await wc.DownloadFileTaskAsync(new Uri(desktop.url), zipFile);
                            ZipFile.ExtractToDirectory(zipFile, appDir);
                            File.Delete(zipFile);

                            // Run after fresh install
                            if (desktop.name.Contains("WinXShell"))
                            {
                                Process.Start(exePath);
                                Thread.Sleep(500);
                                CloseWindowLoop("WinXShell");
                            }

                            if (desktop.name.Contains("seelenui") && skipSeelen)
                            {
                                await ApplySeelenConfig(wc, desktop.zipConfig, zipFile);

                                Thread.Sleep(500); // wait for Seelen UI to initialize

                                Process.Start(exePath);
                            }
                        }
                    }
                    else
                    {
                        // Existing valid install
                        if (desktop.name.Contains("WinXShell"))
                        {
                            if (bingWall)
                                await DownloadBingWallpaper(appDir);

                            Process.Start(exePath);
                            CloseWindowLoop("WinXShell");
                        }

                        if (desktop.name.Contains("seelenui") && skipSeelen)
                        {
                            using (var wc = new WebClient())
                                await ApplySeelenConfig(wc, desktop.zipConfig, zipFile);

                            Thread.Sleep(500); // wait for Seelen UI to initialize

                            Process.Start(exePath);
                        }
                    }
                }

                if (skipSeelen) await SeelenSettingsLoop();
            }
            catch (Exception ex) { SalsaLogger.Error(ex.ToString()); }
        }

        // Extracts fresh Seelen UI config, cleaning the target directory beforehand to prevent corruption
        private static async Task<bool> ApplySeelenConfig(WebClient wc, string url, string zip)
        {
            await wc.DownloadFileTaskAsync(new Uri(url), zip);

            string target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "com.seelen.seelen-ui");

            const int maxRetries = 5;

            try
            {
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // Remove existing config
                        if (Directory.Exists(target))
                        {
                            Directory.Delete(target, true);

                            if (Directory.Exists(target))
                                throw new IOException("Failed to delete target directory.");
                        }

                        // Extract new config
                        ZipFile.ExtractToDirectory(zip, target);

                        // Verify extraction
                        if (!Directory.Exists(target) ||
                            !Directory.EnumerateFileSystemEntries(target).Any())
                        {
                            throw new IOException("Extraction verification failed.");
                        }

                        return true; // Success
                    }
                    catch
                    {
                        // Clean up partial extraction before retrying
                        try
                        {
                            if (Directory.Exists(target))
                                Directory.Delete(target, true);
                        }
                        catch { }

                        if (attempt == maxRetries)
                            return false;

                        await Task.Delay(500);
                    }
                }

                return false;
            }
            finally
            {
                if (File.Exists(zip))
                {
                    try
                    {
                        File.Delete(zip);
                    }
                    catch { }
                }
            }
        }

        // Fetches and applies the UHD Bing Photo of the Day
        private static async Task DownloadBingWallpaper(string dir)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    string json = await wc.DownloadStringTaskAsync("https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-AU");
                    var url = JObject.Parse(json)["images"][0]["urlbase"].ToString();
                    await wc.DownloadFileTaskAsync(new Uri($"https://www.bing.com{url}_UHD.jpg"), Path.Combine(dir, "wallpaper.jpg"));
                }
            }
            catch { }
        }

        // Synchronously waits for a specific window to initialize before closing it
        private static void CloseWindowLoop(string title)
        {
            for (int i = 0; i < 100; i++)
            {
                IntPtr ptr = NativeMethods.FindWindowByCaption(IntPtr.Zero, title);
                if (ptr != IntPtr.Zero)
                {
                    NativeMethods.SendMessage(ptr, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    break;
                }
                Thread.Sleep(100);
            }
        }

        // Monitors Seelen UI startup and automatically suppresses initial settings and wall popups
        private static async Task SeelenSettingsLoop()
        {
            while (true)
            {
                bool foundSettings = false;

                NativeMethods.EnumWindows((hWnd, lp) =>
                {
                    NativeMethods.EnumChildWindows(hWnd, (child, cLp) =>
                    {
                        var sb = new StringBuilder(512);
                        NativeMethods.GetWindowText(child, sb, sb.Capacity);
                        string title = sb.ToString();

                        if (title.Equals("tauri.localhost/settings/index.html", StringComparison.OrdinalIgnoreCase))
                        {
                            foundSettings = true;

                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(500); // wait 500ms before closing
                                NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            });

                            return false; // stop child enumeration
                        }

                        return true;
                    }, IntPtr.Zero);

                    return !foundSettings; // stop EnumWindows if found
                }, IntPtr.Zero);

                if (foundSettings)
                    return; // exit method immediately when found

                await Task.Delay(500);
            }
        }

        // Generates Windows shortcuts, deleting existing dead shortcuts first to ensure proper VM binding
        private static void CreateShortcut(string name, string path, string target, string workDir)
        {
            // Attempt to remove dead/corrupt shortcut to enforce generation of a new Volume GUID
            for (int i = 0; i < 5; i++)
            {
                try 
                { 
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path); 
                    break; 
                } 
                catch { Thread.Sleep(200); }
            }

            try 
            {
                // Instantiate WScript.Shell without Interop dependencies to prevent COM thread crashes
                Type tWsh = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(tWsh);
                var lnk = shell.CreateShortcut(path);
                lnk.TargetPath = target;
                lnk.WorkingDirectory = workDir;
                lnk.Save();
            }
            catch (Exception ex) { SalsaLogger.Error($"Shortcut creation failed for {name}: {ex.Message}"); }
        }
    }
}
