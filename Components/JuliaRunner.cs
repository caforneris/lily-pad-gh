// ========================================
// FILE: JuliaRunner.cs
// DESC: A static helper class to manage finding and executing the standalone
//       Julia process bundled with Lily Pad plugin.
// ========================================

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace LilyPadGH.Components
{
    public static class JuliaRunner
    {
        // NOTE: Cached path to avoid repeated file system lookups.
        private static string _juliaExecutablePath = null;
        private static string _customJuliaPath = null;

        public static void SetCustomJuliaPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                string juliaExe = Path.Combine(path, "bin", "julia.exe");
                if (File.Exists(juliaExe))
                {
                    _customJuliaPath = path;
                    _juliaExecutablePath = null; // Clear cache to force re-evaluation
                }
            }
        }

        private static string GetJuliaExecutablePath()
        {
            // Return the cached path if we've already found it.
            if (!string.IsNullOrEmpty(_juliaExecutablePath) && File.Exists(_juliaExecutablePath))
            {
                return _juliaExecutablePath;
            }

            string juliaPath = null;

            // First, check if a custom path has been set
            if (!string.IsNullOrEmpty(_customJuliaPath))
            {
                juliaPath = Path.Combine(_customJuliaPath, "bin", "julia.exe");
                if (File.Exists(juliaPath))
                {
                    _juliaExecutablePath = juliaPath;
                    return _juliaExecutablePath;
                }
            }

            // Second, check the user's AppData\Local\Programs for Julia installation
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] possibleJuliaVersions = { "Julia-1.11.7", "Julia-1.11.6", "Julia-1.11.5", "Julia-1.11", "Julia-1.10" };

            foreach (var version in possibleJuliaVersions)
            {
                // Check the exact structure: AppData\Local\Programs\Julia-1.11.7\bin\julia.exe
                juliaPath = Path.Combine(localAppData, "Programs", version, "bin", "julia.exe");
                if (File.Exists(juliaPath))
                {
                    _juliaExecutablePath = juliaPath;
                    return _juliaExecutablePath;
                }
            }

            // Third, check the bundled Julia in the plugin directory
            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            juliaPath = Path.Combine(ghaDirectory, "JuliaPackage", "julia-1.11.7-win64", "bin", "julia.exe");

            if (File.Exists(juliaPath))
            {
                _juliaExecutablePath = juliaPath;
                return _juliaExecutablePath;
            }

            // If we get here, no Julia installation was found
            string expectedPath = Path.Combine(localAppData, "Programs", "Julia-1.11.7", "bin", "julia.exe");
            throw new FileNotFoundException(
                $"julia.exe not found. Please ensure Julia is installed at:\n" +
                $"- {expectedPath}\n" +
                $"Or provide a custom path via the Julia Path input.",
                expectedPath);
        }

        public static string GetServerScriptPath()
        {
            // Check in the deployed package folder first
            string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
            string scriptPath = Path.Combine(packageDirectory, "Julia", "RunServer.jl");

            if (File.Exists(scriptPath))
            {
                return scriptPath;
            }

            // Fallback to the gha directory (for development/debugging)
            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            scriptPath = Path.Combine(ghaDirectory, "Julia", "RunServer.jl");

            if (!File.Exists(scriptPath))
            {
                string packagePath = Path.Combine(packageDirectory, "Julia", "RunServer.jl");
                throw new FileNotFoundException(
                    $"RunServer.jl not found. Expected at:\n" +
                    $"- Package folder: {packagePath}\n" +
                    $"- Fallback folder: {scriptPath}\n" +
                    $"Ensure the Julia scripts are deployed to the package folder.",
                    scriptPath);
            }

            return scriptPath;
        }

        public static string RunScript(string scriptPath, string arguments)
        {
            string juliaExePath = GetJuliaExecutablePath();

            var processStartInfo = new ProcessStartInfo
            {
                FileName = juliaExePath,
                Arguments = $"\"{scriptPath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true, // Run headlessly
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, args) => outputBuilder.AppendLine(args.Data);
                process.ErrorDataReceived += (sender, args) => errorBuilder.AppendLine(args.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(); // Wait for the process to complete.

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Julia script failed. Error: {errorBuilder.ToString()}");
                }

                return outputBuilder.ToString();
            }
        }
    }
}