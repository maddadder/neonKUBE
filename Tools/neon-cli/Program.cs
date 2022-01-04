﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Neon;
using Neon.Common;
using Neon.Deployment;
using Neon.Diagnostics;
using Neon.Kube;
using Neon.Kube.Xunit;
using Neon.IO;
using Neon.SSH;
using Neon.Windows;
using Neon.WinTTY;

namespace NeonCli
{
    /// <summary>
    /// This tool is used to configure and manage the nodes of a neonKUBE cluster.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The program version.
        /// </summary>
        public const string Version =
#if ENTERPRISE
            Neon.Cloud.Build.NeonDesktopVersion;
#else
            Neon.Kube.KubeVersions.Kubernetes;
#endif

        /// <summary>
        /// Returns <c>true</c> if this is the enterprise <b>neon-cli</b> build.
        /// </summary>
        /// <remarks>
        /// We use this to help with managing the source code duplicated for this in the
        /// neonKUBE and neonCLOUD (enterprise) GitHub repositories.
        /// </remarks>
        public const bool IsEnterprise =
#if ENTERPRISE
            true;
#else
            false;
#endif

        /// <summary>
        /// Returns the program name for printing help.  This will be <b>"neon"</b> for the community
        /// version and <b>"neon enterprise"</b> for the enterprise version.
        /// </summary>
        public const string Name =
#if ENTERPRISE
            "neon enterprise";
#else
            "neon";
#endif

        /// <summary>
        /// Returns the folder path where the program binary is located.
        /// </summary>
        private static readonly string BinaryFolder = NeonHelper.GetAssemblyFolder(Assembly.GetExecutingAssembly());

        /// <summary>
        /// Returns the path to the standard tool folder when <b>neon-cli</b> has been fully installed.
        /// </summary>
        private static readonly string InstalledToolFolder = Path.Combine(BinaryFolder, "tools");

        /// <summary>
        /// Returns the orignal program <see cref="CommandLine"/>.
        /// </summary>
        public static CommandLine CommandLine { get; private set; }

        /// <summary>
        /// Returns the fully qualified path to the <b>kubectl</b> binary.
        /// </summary>
        public static string KubectlPath { get; private set; }

        /// <summary>
        /// Returns the fully qualified path to the <b>helm</b> binary.
        /// </summary>
        public static string HelmPath { get; private set; }

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string usage = $@"
{Program.Name} [v{Program.Version}]
{Build.Copyright}

USAGE:

    neon [OPTIONS] COMMAND [ARG...]

NEON KUBECTL COMMANDS:

    [neon-cli] supports all standard kubectl commands like (more help below):

    neon apply -f my-manifest.yaml

NEON CLUSTER MANAGEMENT COMMANDS:

    neon cluster prepare    [CLUSTER-DEF]
    neon cluster remove
    neon cluster setup      [CLUSTER-DEF]
    neon cluster start      USER@CLUSTER[/NAMESPACE]
    neon cluster stop       USER@CLUSTER[/NAMESPACE] [--turnoff]
    neon cluster verify     [CLUSTER-DEF]
    neon login              COMMAND
    neon logout

NEON HELM COMMANDS:

    The neon-cli supports all standard Helm commands by prefixing
    them with [helm], like:

    neon helm install -f my-values.yaml my-redis ./redis

NEON UTILITY COMMANDS:

    neon tool generate iso  SOURCE-FOLDER ISO-PATH
    neon tool password      COMMAND
    neon tool vault         COMMAND
    neon tool version       [-n] [--git] [--minimum=VERSION]

CLUSTER MANAGEMENT ARGUMENTS:

    CLUSTER-DEF         - Path to a cluster definition file.  This is
                          optional for some commands when logged in

    COMMAND             - Subcommand and arguments

===============================================================================
";
            //===============================
            // $debug(jefflill): DELETE THIS!

            var manifestText =
@"
apiVersion: v1
kind: Pod
metadata:
  annotations:
    kubeadm.kubernetes.io/kube-apiserver.advertise-address.endpoint: 100.64.0.2:6443
  creationTimestamp: null
  labels:
    component: kube-apiserver
    tier: control-plane
  name: kube-apiserver
  namespace: kube-system
spec:
  containers:
  - command:
    - kube-apiserver
    - --advertise-address=0.0.0.0
    - --allow-privileged=true
    - --api-audiences=api
    - --authorization-mode=Node,RBAC
    - --bind-address=0.0.0.0
    - --client-ca-file=/etc/kubernetes/pki/ca.crt
    - --default-not-ready-toleration-seconds=30
    - --default-unreachable-toleration-seconds=30
    - --enable-admission-plugins=NamespaceLifecycle,LimitRanger,ServiceAccount,DefaultStorageClass,DefaultTolerationSeconds,MutatingAdmissionWebhook,ValidatingAdmissionWebhook,Priority,ResourceQuota
    - --enable-bootstrap-token-auth=true
    - --etcd-cafile=/etc/kubernetes/pki/etcd/ca.crt
    - --etcd-certfile=/etc/kubernetes/pki/apiserver-etcd-client.crt
    - --etcd-keyfile=/etc/kubernetes/pki/apiserver-etcd-client.key
    - --etcd-servers=https://127.0.0.1:2379
    - --insecure-port=0
    - --kubelet-client-certificate=/etc/kubernetes/pki/apiserver-kubelet-client.crt
    - --kubelet-client-key=/etc/kubernetes/pki/apiserver-kubelet-client.key
    - --kubelet-preferred-address-types=InternalIP,ExternalIP,Hostname
    - --logging-format=json
    - --oidc-client-id=kubernetes
    - --oidc-groups-claim=groups
    - --oidc-groups-prefix=
    - --oidc-issuer-url=https://sso.f4ef74204ee34bbb888e823b3f0c8e3b.neoncluster.io
    - --oidc-username-claim=email
    - --oidc-username-prefix=-
    - --proxy-client-cert-file=/etc/kubernetes/pki/front-proxy-client.crt
    - --proxy-client-key-file=/etc/kubernetes/pki/front-proxy-client.key
    - --requestheader-allowed-names=front-proxy-client
    - --requestheader-client-ca-file=/etc/kubernetes/pki/front-proxy-ca.crt
    - --requestheader-extra-headers-prefix=X-Remote-Extra-
    - --requestheader-group-headers=X-Remote-Group
    - --requestheader-username-headers=X-Remote-User
    - --secure-port=6443
    - --service-account-issuer=kubernetes.default.svc
    - --service-account-key-file=/etc/kubernetes/pki/sa.key
    - --service-account-signing-key-file=/etc/kubernetes/pki/sa.key
    - --service-cluster-ip-range=10.253.0.0/16
    - --tls-cert-file=/etc/kubernetes/pki/apiserver.crt
    - --tls-private-key-file=/etc/kubernetes/pki/apiserver.key
    image: neon-registry.node.local/kube-apiserver:v1.21.4
    imagePullPolicy: IfNotPresent
    livenessProbe:
      failureThreshold: 8
      httpGet:
        host: 100.64.0.2
        path: /livez
        port: 6443
        scheme: HTTPS
      initialDelaySeconds: 10
      periodSeconds: 10
      timeoutSeconds: 15
    name: kube-apiserver
";
            var manifest   = NeonHelper.YamlDeserialize<dynamic>(manifestText);
            var spec       = manifest["spec"];
            var containers = spec["containers"];
            var container  = containers[0];
            var cmd        = (List<object>)container["command"];

            cmd.Add("--feature-gate=TEST=true");

            manifestText = NeonHelper.YamlSerialize(manifest);

            //===============================

            // Disable any logging that might be performed by library classes.

            LogManager.Default.LogLevel = LogLevel.None;

            // Use the version of Powershell Core installed with the application,
            // if present.

            PowerShell.PwshPath = KubeHelper.PwshPath;

            // Ensure that all of the non-enterprise cluster hosting manager 
            // implementations are loaded.

            new HostingManagerFactory(() => HostingLoader.Initialize());

#if ENTERPRISE
            // Configure the enterprise service depedencies.

            NeonHelper.ServiceContainer.AddSingleton<IEnterpriseHostingLoader>(new EnterpriseHostingLoader());
            NeonHelper.ServiceContainer.AddSingleton<IEnterpriseHelper>(new EnterpriseHelper());
#endif

            // Register a [ProfileClient] so commands will be able to pick
            // up secrets and profile information from [neon-assistant].

            NeonHelper.ServiceContainer.AddSingleton<IProfileClient>(new ProfileClient());

            // Fetch the paths to the [kubectl] and [helm] binaries.  Note that this
            // will download them when necessary.

            KubectlPath = GetKubectlPath();
            HelmPath    = GetHelmPath();

            // Process the command line.

            try
            {
                ICommand command;

                CommandLine = new CommandLine(args);

                if (CommandLine.Items.Length == 0)
                {
                    // Output our standard usage help and then launch [kubectl] to display
                    // its help as well.

                    Console.WriteLine(usage);
                    NeonHelper.Execute(KubectlPath, Array.Empty<object>());
                    Program.Exit(0);
                }

                // Scan for enabled commands in the current assembly.

                var commands = new List<ICommand>();

                foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (!type.Implements<ICommand>())
                    {
                        continue;
                    }

                    var commandAttribute = type.GetCustomAttribute<CommandAttribute>();

                    if (commandAttribute == null || commandAttribute.Disabled)
                    {
                        continue;
                    }

                    commands.Add((ICommand)Activator.CreateInstance(type));
                }

                // Short-circuit the help command.

                if (CommandLine.Arguments[0] == "help")
                {
                    CommandLine = CommandLine.Shift(1);
                    command     = GetCommand(CommandLine, commands);

                    if (command != null)
                    {
                        command.Help();
                    }
                    else
                    {
                        // Output our standard usage help and then launch [kubectl] to
                        // display its help as well.

                        Console.WriteLine(usage);
                        NeonHelper.Execute(KubectlPath, Array.Empty<object>());
                    }

                    Program.Exit(0);
                }

                // Lookup the command.

                command = GetCommand(CommandLine, commands);

                if (CommandLine.Arguments[0] == "tool" && command == null)
                {
                    // Special case invalid command detection for [tool] commands.

                    Console.WriteLine(usage);
                    Program.Exit(1);
                }

                if (command == null)
                {
                    // This must be a [kubectl] command, so spawn [kubectl] to handle it.
                    // Note that we'll create a TTY for commands with a [-t] or [--tty]
                    // option so that editors and other interactive commands will work.

                    // $todo(jefflill):
                    //
                    // I believe this treats this as if the user specified the [-i] or
                    // [--stdin] option as well.  Most users probably specify [-it]
                    // together, but we may need to revisit this at some point.

                    var tty = CommandLine.HasOption("--tty");

                    if (!tty)
                    {
                        // Look for a [-t] option.

                        foreach (var item in CommandLine.Items.Where(item => item.StartsWith("-") && item.Length > 1 && item[1] != '-'))
                        {
                            if (item.Contains('t'))
                            {
                                tty = true;
                                break;
                            }
                        }
                    }

                    if (tty)
                    {
                        new ConsoleTTY().Run($"\"{KubectlPath}\" {CommandLine}");
                        Program.Exit(0);
                    }
                    else
                    {
                        Program.Exit(NeonHelper.Execute(KubectlPath, CommandLine.Items));
                    }
                }

                // This is one of our commands, so ensure that there are no unexpected
                // command line options when the command enables option checks.

                if (command.CheckOptions)
                {
                    var validOptions = new HashSet<string>();

                    foreach (var optionName in command.ExtendedOptions)
                    {
                        validOptions.Add(optionName);
                    }

                    foreach (var option in CommandLine.Options)
                    {
                        if (!validOptions.Contains(option.Key))
                        {
                            var commandWords = string.Empty;

                            foreach (var word in command.Words)
                            {
                                if (commandWords.Length > 0)
                                {
                                    commandWords += " ";
                                }

                                commandWords += word;
                            }

                            Console.Error.WriteLine($"*** ERROR: [{commandWords}] command does not support [{option.Key}].");
                            Program.Exit(1);
                        }
                    }
                }

                // Run the command.

                await command.RunAsync(CommandLine.Shift(command.Words.Length));
            }
            catch (ProgramExitException e)
            {
                return e.ExitCode;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"*** ERROR: {NeonHelper.ExceptionError(e)}");
                Console.Error.WriteLine(e.StackTrace);
                Console.Error.WriteLine(string.Empty);
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Attempts to match the command line to the <see cref="ICommand"/> to be used
        /// to implement the command.
        /// </summary>
        /// <param name="commandLine">The command line.</param>
        /// <param name="commands">The commands.</param>
        /// <returns>The command instance or <c>null</c>.</returns>
        private static ICommand GetCommand(CommandLine commandLine, List<ICommand> commands)
        {
            // Sort the commands in decending order by number of words in the
            // command (we want to match the longest sequence).

            foreach (var command in commands.OrderByDescending(c => c.Words.Length))
            {
                if (command.Words.Length > commandLine.Arguments.Length)
                {
                    // Not enough arguments to match the command.

                    continue;
                }

                var matches = true;

                for (int i = 0; i < command.Words.Length; i++)
                {
                    if (!string.Equals(command.Words[i], commandLine.Arguments[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches && command.AltWords != null)
                {
                    matches = true;

                    for (int i = 0; i < command.AltWords.Length; i++)
                    {
                        if (!string.Equals(command.AltWords[i], commandLine.Arguments[i]))
                        {
                            matches = false;
                            break;
                        }
                    }
                }

                if (matches)
                {
                    return command;
                }
            }

            // No match.

            return null;
        }

        /// <summary>
        /// Returns the program version as the Git branch and commit and an optional
        /// indication of whether the program was build from a dirty branch.
        /// </summary>
        public static string GitVersion
        {
            get
            {
#pragma warning disable 0436
                var version = $"{ThisAssembly.Git.Branch}-{ThisAssembly.Git.Commit}";
#pragma warning restore 0436

#pragma warning disable 162 // Unreachable code

                //if (ThisAssembly.Git.IsDirty)
                //{
                //    version += "-DIRTY";
                //}

#pragma warning restore 162 // Unreachable code

                return version;
            }
        }

        /// <summary>
        /// Exits the program returning the specified process exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void Exit(int exitCode)
        {
            throw new ProgramExitException(exitCode);
        }

        /// <summary>
        /// Presents the user with a yes/no question and waits for a response.
        /// </summary>
        /// <param name="prompt">The question prompt.</param>
        /// <returns><c>true</c> if the answer is yes, <b>false</b> for no.</returns>
        public static bool PromptYesNo(string prompt)
        {
            try
            {
                while (true)
                {
                    Console.WriteLine();
                    Console.Write($"{prompt} [y/n]: ");

                    var key = Console.ReadKey().KeyChar;

                    Console.WriteLine();

                    if (key == 'y' || key == 'Y')
                    {
                        return true;
                    }
                    else if (key == 'n' || key == 'N')
                    {
                        return false;
                    }
                }
            }
            finally
            {
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Searches the directory holding a file as well as any ancestor directories
        /// for the first <b>.password-name</b> file specifying a default password name.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <returns>The default password name if one was found or <c>null</c>.</returns>
        public static string GetDefaultPasswordName(string filePath)
        {
            var folderPath = Path.GetDirectoryName(Path.GetFullPath(filePath));

            try
            {
                while (true)
                {
                    var passwordNamePath = Path.Combine(folderPath, ".password-name");

                    if (File.Exists(passwordNamePath))
                    {
                        var passwordName = File.ReadLines(passwordNamePath).First().Trim();

                        if (passwordName == string.Empty)
                        {
                            // An empty [.password-name] file will block further searching.

                            return null;
                        }

                        return passwordName;
                    }

                    if (Path.GetPathRoot(folderPath) == folderPath)
                    {
                        // We're at the file system root.

                        return null;
                    }

                    // Advance to the parent folder.

                    folderPath = Path.GetFullPath(Path.Combine(folderPath, ".."));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // We will see this if the current user doesn't have permissions to
                // walk the file directories all the way up to the root of the
                // file system.  We'll just return NULL in this case.

                return null;
            }
        }

        /// <summary>
        /// Returns a password based on its name.
        /// </summary>
        /// <param name="passwordName">The password name.</param>
        /// <returns>The password or <c>null</c> if the named password doesn't exist.</returns>
        public static string LookupPassword(string passwordName)
        {
            var passwordPath = Path.Combine(KubeHelper.PasswordsFolder, passwordName);

            if (File.Exists(passwordPath))
            {
                return File.ReadLines(passwordPath).First().Trim();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the path to the a tool binary to be used by <b>neon-cli</b>.
        /// </summary>
        /// <returns>The fully qualified tool path.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the tool cannot be located.</exception>
        /// <remarks>
        /// <para>
        /// Installed versions of <b>neon-cli</b> expect the <b>kubectl</b> and <b>helm</b> tools to
        /// be located in the <b>tools</b> subfolder where <b>neon-cli</b> itself is installed, like:
        /// </para>
        /// <code>
        /// C:\Program Files\neonFORGE\neonDESKTOP\
        ///     neon-cli.exe
        ///     tools\
        ///         helm.exe
        ///         kubectl.exe
        /// </code>
        /// <para>
        /// If this folder exists and the tool binary exists within that folder, then we'll simply
        /// return the path to the binary.
        /// </para>
        /// <para>
        /// If the tool folder or binary does not exist, then the user is probably a developer running
        /// an uninstalled version of the tool, perhaps in the debugger.  In this case, we're going to
        /// cache these binaries in the special tools folder: <see cref="KubeHelper.ToolsFolder"/>.
        /// </para>
        /// <para>
        /// If the tool folder and/or the requested tool binary doesn't exist or the tool version doesn't
        /// match what's specified in <see cref="KubeVersions"/>, then this method will attempt to download
        /// the binary to <b>%TEMP%\neon-tool-cache</b>, indicating that this is happening on the
        /// console.
        /// </para>
        /// </remarks>
        public static string GetKubectlPath()
        {
            return KubeHelper.GetKubectlPath(InstalledToolFolder, userToolsFolder: true);
        }

        /// <summary>
        /// Returns the path to the a tool binary to be used by <b>neon-cli</b>.
        /// </summary>
        /// <returns>The fully qualified tool path.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the tool cannot be located.</exception>
        /// <remarks>
        /// <para>
        /// Installed versions of <b>neon-cli</b> expect the <b>kubectl</b> and <b>helm</b> tools to
        /// be located in the <b>tools</b> subfolder where <b>neon-cli</b> itself is installed, like:
        /// </para>
        /// <code>
        /// C:\Program Files\neonFORGE\neonDESKTOP\
        ///     neon-cli.exe
        ///     tools\
        ///         helm.exe
        ///         kubectl.exe
        /// </code>
        /// <para>
        /// If this folder exists and the tool binary exists within that folder, then we'll simply
        /// return the path to the binary.
        /// </para>
        /// <para>
        /// If the tool folder or binary does not exist, then the user is probably a developer running
        /// an uninstalled version of the tool, perhaps in the debugger.  In this case, we're going to
        /// cache these binaries in the special tools folder: <see cref="KubeHelper.ToolsFolder"/>.
        /// </para>
        /// <para>
        /// If the tool folder and/or ther equested tool binary doesn't exist or the tool version doesn't
        /// match what's specified in <see cref="KubeVersions"/>, then this method will attempt to download
        /// the binary to <b>%TEMP%\neon-tool-cache</b>, indicating that this is happening on the
        /// console.
        /// </para>
        /// </remarks>
        public static string GetHelmPath()
        {
            return KubeHelper.GetHelmPath(InstalledToolFolder, userToolsFolder: true);
        }
    }
}
