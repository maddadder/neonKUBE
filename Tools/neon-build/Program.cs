﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Linq;
using System.Text;

using Neon.Common;

namespace NeonBuild
{
    /// <summary>
    /// Hosts the program entrypoint.
    /// </summary>
    public static class Program
    {
        private const string usage =
@"
Internal neonKUBE project build related utilities.

neon-build clean [-all]
-----------------------
Deletes all of the [bin] and [obj] folders within the repo and
also clears the [Build] folder.

OPTIONS:

    --all           - clears the [Build-cache] folder too.

neon-build build-installer PLATFORM [--kube-version=VERSION]
------------------------------------------------------------
Builds a neonKUBE Installer

Removes cached components:
--------------------------
neon-build clear PLATFORM

neon-build download PLATFORM [--kube-version=VERSION]
-----------------------------------------------------
Downloads KUBE PLATFORM components (if not already present):

ARGUMENTS:

    PLATFORM        - specifies the target platform, one of:

                        windows, osx

OPTIONS:

    --kube-version  - optionally specifies the Kubernetes version
                      to be installed.  This defaults to the version
                      read from [$/kube-version.txt].

neon-build gzip SOURCE TARGET
-----------------------------
Compresses a file using GZIP if the target doesn't exist or is
older than the source.

ARGUMENTS:

    SOURCE          - path to the (uncompressed) source file.
    TARGET          - path to the (compressed) target file.

neon-build copy SOURCE TARGET
-----------------------------
Copies a file if the target doesn't exist or is older than the source.

ARGUMENTS:

    SOURCE          - path to the (uncompressed) source file.
    TARGET          - path to the (compressed) target file.

neon-build build-version
----------------------------------------------
Used to insert the first line of the [$/product-version] text file
into the `[$/Lib/Neon.Common/Build.cs`] file, replacing the value of the
[ProductVersion] constant.

neon-build pack-version VERSION-FILE CSPROJ-FILE
------------------------------------------------
Updates the specified library CSPROJ file version to a combination of
the global VERSION-FILE (typically [$/product-version.txt] and an optional
project local [prerelease.txt] file as specified here:

    https://github.com/nforgeio/neonKUBE/issues/715

neon-build help gtag TAG-FILE HELP-FOLDER
-----------------------------------------
Reads the Google Analytics script TAG-FILE and then inserts the script
into the Sandcastle Help File Builder generated documentation page files
named *.htm and *.html within the specified folder (recursively).
";
        private static CommandLine commandLine;

        /// <summary>
        /// This is the program entrypoint.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        public static void Main(string[] args)
        {
            string platform;
            KubeSetupHelper helper;

            commandLine = new CommandLine(args);

            var command = commandLine.Arguments.FirstOrDefault();

            if (command != null)
            {
                command = command.ToLowerInvariant();
            }

            if (commandLine.Arguments.Length == 0 || commandLine.HasHelpOption || command == "help")
            {
                Console.WriteLine(usage);
                Program.Exit(0);
            }

            try
            {
                Program.RepoRootFolder = Environment.GetEnvironmentVariable("NF_ROOT");

                if (string.IsNullOrEmpty(Program.RepoRootFolder) || !Directory.Exists(Program.RepoRootFolder))
                {
                    Console.Error.WriteLine("*** ERROR: NF_ROOT environment variable does not reference the local neonKUBE repostory.");
                    Program.Exit(1);
                }

                Program.DefaultKubernetesVersion = File.ReadAllText(Path.Combine(Program.RepoRootFolder, "kube-version.txt")).Trim();

                // Handle the commands.

                switch (command)
                {
                    case "clean":

                        var buildFolder = Path.Combine(Program.RepoRootFolder, "Build");

                        if (Directory.Exists(buildFolder))
                        {
                            NeonHelper.DeleteFolderContents(buildFolder);
                        }

                        if (commandLine.HasOption("--all"))
                        {
                            var buildCacheFolder = Path.Combine(Program.RepoRootFolder, "Build-cache");

                            if (Directory.Exists(buildCacheFolder))
                            {
                                NeonHelper.DeleteFolderContents(buildCacheFolder);
                            }
                        }

#if TODO
                        // $todo(jefflill): https://github.com/nforgeio/neonKUBE/issues/689

                        var packagesPath = Path.Combine(Program.RepoRootFolder, "packages");

                        if (Directory.Exists(packagesPath))
                        {
                            NeonHelper.DeleteFolder(packagesPath);
                            Directory.Delete(packagesPath);
                        }
#endif

                        var cadenceResourcesPath = Path.Combine(Program.RepoRootFolder, "Lib", "Neon.Cadence", "Resources");

                        if (Directory.Exists(cadenceResourcesPath))
                        {
                            NeonHelper.DeleteFolder(cadenceResourcesPath);
                        }

                        foreach (var folder in Directory.EnumerateDirectories(Program.RepoRootFolder, "bin", SearchOption.AllDirectories))
                        {
                            if (Directory.Exists(folder))
                            {
                                NeonHelper.DeleteFolder(folder);
                            }
                        }

                        foreach (var folder in Directory.EnumerateDirectories(Program.RepoRootFolder, "obj", SearchOption.AllDirectories))
                        {
                            if (Directory.Exists(folder))
                            {
                                NeonHelper.DeleteFolder(folder);
                            }
                        }

                        break;

                    case "installer":

                        platform = commandLine.Arguments.ElementAtOrDefault(1);

                        if (string.IsNullOrEmpty(platform))
                        {
                            Console.Error.WriteLine("*** ERROR: PLATFORM argument is required.");
                            Program.Exit(1);
                        }

                        helper = new KubeSetupHelper(platform, commandLine,
                            outputAction: text => Console.Write(text),
                            errorAction: text => Console.Write(text));

                        EnsureOption("--kube-version", Program.DefaultKubernetesVersion);

                        switch (helper.Platform)
                        {
                            case KubeClientPlatform.Windows:

                                new WinInstallBuilder(helper).Run();
                                break;

                            case KubeClientPlatform.Osx:

                                throw new NotImplementedException();
                        }
                        break;

                    case "clear":

                        platform = commandLine.Arguments.ElementAtOrDefault(1);

                        if (string.IsNullOrEmpty(platform))
                        {
                            Console.Error.WriteLine("*** ERROR: PLATFORM argument is required.");
                            Program.Exit(1);
                        }

                        helper = new KubeSetupHelper(platform, commandLine,
                            outputAction: text => Console.Write(text),
                            errorAction: text => Console.Write(text));

                        helper.Clear();
                        break;

                    case "download":

                        platform = commandLine.Arguments.ElementAtOrDefault(1);

                        if (string.IsNullOrEmpty(platform))
                        {
                            Console.Error.WriteLine("*** ERROR: PLATFORM argument is required.");
                            Program.Exit(1);
                        }

                        helper = new KubeSetupHelper(platform, commandLine,
                            outputAction: text => Console.Write(text),
                            errorAction: text => Console.Write(text));

                        EnsureOption("--kube-version", Program.DefaultKubernetesVersion);
                        helper.Download();
                        break;

                    case "copy":

                        {
                            var sourcePath = commandLine.Arguments.ElementAtOrDefault(1);
                            var targetPath = commandLine.Arguments.ElementAtOrDefault(2);

                            if (sourcePath == null)
                            {
                                Console.Error.WriteLine("*** ERROR: SOURCE argument is required.");
                                Program.Exit(1);
                            }

                            if (targetPath == null)
                            {
                                Console.Error.WriteLine("*** ERROR: TARGET argument is required.");
                                Program.Exit(1);
                            }

                            if (!File.Exists(sourcePath))
                            {
                                Console.Error.WriteLine($"*** ERROR: SOURCE file [{sourcePath}] does not exist.");
                                Program.Exit(1);
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                            if (File.Exists(targetPath) && File.GetLastWriteTimeUtc(targetPath) > File.GetLastWriteTimeUtc(sourcePath))
                            {
                                Console.WriteLine($"File [{targetPath}] is up to date.");
                                Program.Exit(0);
                            }

                            Console.WriteLine($"COPY: [{sourcePath}] --> [{targetPath}].");
                            File.Copy(sourcePath, targetPath);
                        }
                        break;

                    case "gzip":

                        {
                            var sourcePath = commandLine.Arguments.ElementAtOrDefault(1);
                            var targetPath = commandLine.Arguments.ElementAtOrDefault(2);

                            if (sourcePath == null)
                            {
                                Console.Error.WriteLine("*** ERROR: SOURCE argument is required.");
                                Program.Exit(1);
                            }

                            if (targetPath == null)
                            {
                                Console.Error.WriteLine("*** ERROR: TARGET argument is required.");
                                Program.Exit(1);
                            }

                            if (!File.Exists(sourcePath))
                            {
                                Console.Error.WriteLine($"*** ERROR: SOURCE file [{sourcePath}] does not exist.");
                                Program.Exit(1);
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                            if (File.Exists(targetPath) && File.GetLastWriteTimeUtc(targetPath) > File.GetLastWriteTimeUtc(sourcePath))
                            {
                                Console.WriteLine($"File [{targetPath}] is up to date.");
                                Program.Exit(0);
                            }

                            Console.WriteLine($"GZIP: [{sourcePath}] --> [{targetPath}].");

                            var uncompressed = File.ReadAllBytes(sourcePath);
                            var compressed = NeonHelper.GzipBytes(uncompressed);

                            File.WriteAllBytes(targetPath, compressed);
                        }
                        break;

                    case "build-version":

                        {
                            var productVersionPath = Path.Combine(Environment.GetEnvironmentVariable("NF_ROOT"), "product-version.txt");
                            var buildCsPath = Path.Combine(Environment.GetEnvironmentVariable("NF_ROOT"), "Lib", "Neon.Common", "Build.cs");
                            var version = File.ReadLines(productVersionPath, Encoding.UTF8).First();

                            if (string.IsNullOrEmpty(version))
                            {
                                Console.Error.WriteLine($"[{productVersionPath}] specifies an empty version.");
                                Program.Exit(1);
                            }

                            version = version.Trim();

                            if (!SemanticVersion.TryParse(version, out var v))
                            {
                                Console.Error.WriteLine($"[{productVersionPath}] specifies an invalid semantic version: [{version}].");
                                Program.Exit(1);
                            }

                            // Process the lines from the [$/Lib/Neon/Common/Build.cs] file, looking for the one
                            // with the [ProductVersion] constant definition.  We're going to replace the string
                            // with the product version we retrieved above and the rewrite the source file.
                            //
                            // Note that this is somewhat fragile because we're depending on the constant definition
                            // being on a single line (which is has been for at least 14 years).

                            var buildCsLines = File.ReadAllLines(buildCsPath);
                            var sbOutput = new StringBuilder();

                            foreach (var line in buildCsLines)
                            {
                                if (!line.Contains("public const string ProductVersion"))
                                {
                                    sbOutput.AppendLine(line);
                                    continue;
                                }

                                int pStartQuote;
                                int pEndQuote;

                                pStartQuote = line.IndexOf('"');

                                if (pStartQuote == -1)
                                {
                                    Console.Error.WriteLine($"[{buildCsPath}] unexpected [ProductVersion] definition format.");
                                    Program.Exit(1);
                                }

                                pEndQuote = line.IndexOf('"', pStartQuote + 1);

                                if (pStartQuote == -1)
                                {
                                    Console.Error.WriteLine($"[{buildCsPath}] unexpected [ProductVersion] definition format.");
                                    Program.Exit(1);
                                }

                                var oldLiteral = line.Substring(pStartQuote, pEndQuote - pStartQuote + 1);
                                var newLiteral = $"\"{version}\"";

                                var newLine = line.Replace(oldLiteral, newLiteral);

                                sbOutput.AppendLine(newLine);
                            }

                            File.WriteAllText(buildCsPath, sbOutput.ToString());
                        }
                        break;

                    case "pack-version":

                        PackVersion(commandLine);
                        break;

                    default:

                        Console.Error.WriteLine($"*** ERROR: Unexpected command [{command}].");
                        Program.Exit(1);
                        break;
                }

                Program.Exit(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"*** ERROR: {e.Message}");
                Program.Exit(1);
            }
        }

        /// <summary>
        /// Returns the path to the neonKUBE local repository root folder.
        /// </summary>
        public static string RepoRootFolder { get; private set; }

        /// <summary>
        /// Returns the default version of Kubernetes to be installed.
        /// </summary>
        public static string DefaultKubernetesVersion { get; private set; }

        /// <summary>
        /// Ensures that a command line option is present.
        /// </summary>
        /// <param name="option">The option name.</param>
        /// <param name="defValue">Optionally specifies the default value.</param>
        private static void EnsureOption(string option, string defValue = null)
        {
            if (string.IsNullOrEmpty(commandLine.GetOption(option, defValue)))
            {
                Console.Error.WriteLine($"*** ERROR: Command line option [{option}] is invalid.");
                Program.Exit(1);
            }
        }

        /// <summary>
        /// Terminates the program with a specified exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void Exit(int exitCode)
        {
            Environment.Exit(exitCode);
        }


        /// <summary>
        /// Reads a Nuget package version string from the first line of a text file and
        /// then updates the version section in a CSPROJ file or NUSPEC with the version.  
        /// This is useful for batch publishing multiple libraries.
        /// </summary>
        /// <param name="commandLine">The command line.</param>
        private static void PackVersion(CommandLine commandLine)
        {
            commandLine = commandLine.Shift(1);

            if (commandLine.Arguments.Length != 2)
            {
                Console.WriteLine(usage);
                Program.Exit(1);
            }

            var solutionVersionPath = Environment.ExpandEnvironmentVariables(commandLine.Arguments[0]);
            var csprojPath = Environment.ExpandEnvironmentVariables(commandLine.Arguments[1]);
            var localVersionPath = Path.Combine(Path.GetDirectoryName(csprojPath), "prerelease.txt");

            var rawSolutionVersion = File.ReadAllLines(solutionVersionPath).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(rawSolutionVersion))
            {
                Console.Error.WriteLine($"*** ERROR: [{solutionVersionPath}] does not specify a version.");
                Program.Exit(1);
            }

            var solutionVersion = SemanticVersion.Parse(rawSolutionVersion.Trim());
            var localPrerelease = (string)null;

            if (File.Exists(localVersionPath))
            {
                localPrerelease = File.ReadAllLines(localVersionPath).FirstOrDefault();

                if (!string.IsNullOrEmpty(localPrerelease))
                {
                    localPrerelease.Trim();
                }

                if (localPrerelease.StartsWith("-"))
                {
                    localPrerelease = localPrerelease.Substring(1);
                }

                if (string.IsNullOrEmpty(localPrerelease))
                {
                    localPrerelease = null;
                }

                localPrerelease = localPrerelease.ToLowerInvariant();
            }

            string version = null;

            if (solutionVersion.Prerelease != null && (string.IsNullOrEmpty(localPrerelease) || solutionVersion.Prerelease.ToLowerInvariant().CompareTo(localPrerelease) < 0))
            {
                // The solution version specifies a pre-release identifier which is less than
                // the local version or there is no local version.

                version = solutionVersion.ToString();
            }
            else if (!string.IsNullOrEmpty(localPrerelease))
            {
                // This project has a local [prerelease.txt] file so we'll append the
                // contents as the release identifier to the solution version for this
                // project.

                version = $"{solutionVersion}-{localPrerelease}";
            }
            else
            {
                // There is no local pre-release version, so we'll use the
                // solution version.

                version = solutionVersion.ToString();
            }

            // Ensure that the local version is valid.

            SemanticVersion.Parse(version);

            var csproj = File.ReadAllText(csprojPath);
            var pos    = csproj.IndexOf("<Version>", StringComparison.OrdinalIgnoreCase);

            pos += "<Version>".Length;

            if (pos == -1)
            {
                Console.Error.WriteLine($"*** ERROR: [{csprojPath}] does not have: <version>...</version>");
                Program.Exit(1);
            }

            var posEnd = csproj.IndexOf("</Version>", pos, StringComparison.OrdinalIgnoreCase);

            if (posEnd == -1)
            {
                Console.Error.WriteLine($"*** ERROR: [{csprojPath}] does not have: <version>...</version>");
                Program.Exit(1);
            }

            csproj = csproj.Substring(0, pos) + version + csproj.Substring(posEnd);

            File.WriteAllText(csprojPath, csproj);
        }
    }
}
