// ========================================
// FILE: JuliaRunner.cs
// PART 5: JULIA PROCESS MANAGEMENT
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

        private static string GetJuliaExecutablePath()
        {
            // Return the cached path if we've already found it.
            if (!string.IsNullOrEmpty(_juliaExecutablePath) && File.Exists(_juliaExecutablePath))
            {
                return _juliaExecutablePath;
            }

            // Get the directory where your .gha file is located.
            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Build the relative path to julia.exe.
            string juliaPath = Path.Combine(ghaDirectory, "JuliaPackage", "julia-1.11.7-win64", "bin", "julia.exe");

            if (!File.Exists(juliaPath))
            {
                throw new FileNotFoundException(
                    "julia.exe not found. Check the .csproj file to ensure the 'Content Include' path is correct.",
                    juliaPath);
            }

            _juliaExecutablePath = juliaPath;
            return _juliaExecutablePath;
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