﻿//-----------------------------------------------------------------------------
// FILE:	    KubeSetup.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
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
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32;

using Couchbase;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using k8s;
using k8s.Models;

using Neon.Collections;
using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.SSH;
using Neon.Windows;
using Neon.Cryptography;

namespace Neon.Kube
{
    /// <summary>
    /// Implements cluster setup operations.
    /// </summary>
    public static class KubeSetup
    {
        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Holds information about a remote file we'll need to download.
        /// </summary>
        private class RemoteFile
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="path">The file path.</param>
            /// <param name="permissions">Optional file permissions.</param>
            /// <param name="owner">Optional file owner.</param>
            public RemoteFile(string path, string permissions = "600", string owner = "root:root")
            {
                this.Path        = path;
                this.Permissions = permissions;
                this.Owner       = owner;
            }

            /// <summary>
            /// Returns the file path.
            /// </summary>
            public string Path { get; private set; }

            /// <summary>
            /// Returns the file permissions.
            /// </summary>
            public string Permissions { get; private set; }

            /// <summary>
            /// Returns the file owner formatted as: USER:GROUP.
            /// </summary>
            public string Owner { get; private set; }
        }

        //---------------------------------------------------------------------
        // Private constants

        private const string                joinCommandMarker       = "kubeadm join";
        private const int                   defaultMaxParallelNodes = 10;
        private const int                   maxJoinAttempts         = 5;
        private static readonly TimeSpan    joinRetryDelay          = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan    clusterOpTimeout        = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan    clusterOpRetryInterval  = TimeSpan.FromSeconds(10);

        //---------------------------------------------------------------------
        // These string constants are used to persist state in [SetupControllers].

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="ClusterProxy"/> property.
        /// </summary>
        public const string ClusterProxyProperty = "cluster-proxy";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="ClusterLogin"/> property.
        /// </summary>
        public const string ClusterLoginProperty = "cluster-login";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="IHostingManager"/> property.
        /// </summary>
        public const string HostingManagerProperty = "hosting-manager";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="Kubernetes"/> client property.
        /// </summary>
        public const string K8sClientProperty = "k8sclient";

        //---------------------------------------------------------------------
        // Implementation

        /// <summary>
        /// Returns the <see cref="Kubernetes"/> client persisted in the dictionary passed.
        /// </summary>
        /// <param name="setupState">The setup state.</param>
        /// <returns>The <see cref="Kubernetes"/> client.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when there is no persisted client, indicating that <see cref="ConnectCluster(ObjectDictionary)"/>
        /// has not been called yet.
        /// </exception>
        public static Kubernetes GetK8sClient(ObjectDictionary setupState)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));

            try
            {
                return setupState.Get<Kubernetes>(K8sClientProperty);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Cannot retrieve the Kubernetes client because the cluster hasn't been connected via [{nameof(ConnectCluster)}()].", e);
            }
        }

        /// <summary>
        /// Renders a Kubernetes label value in a format suitable for labeling a node.
        /// </summary>
        private static string GetLabelValue(object value)
        {
            if (value is bool)
            {
                value = NeonHelper.ToBoolString((bool)value);
            }

            return $"\"{value}\"";
        }

        /// <summary>
        /// Gets a list of taints that are currently applied to all nodes matching the given node label/value pair.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="labelKey">The target nodes label key.</param>
        /// <param name="labelValue">The target nodes label value.</param>
        /// <returns>The taint list.</returns>
        public static async Task<List<V1Taint>> GetTaintsAsync(ObjectDictionary setupState, string labelKey, string labelValue)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));

            var taints = new List<V1Taint>();

            foreach (var n in (await GetK8sClient(setupState).ListNodeAsync()).Items.Where(n => n.Metadata.Labels.Any(l => l.Key == labelKey && l.Value == labelValue)))
            {
                if (n.Spec.Taints?.Count() > 0)
                {
                    foreach (var t in n.Spec.Taints)
                    {
                        if (!taints.Any(x => x.Key == t.Key && x.Effect == t.Effect && x.Value == t.Value))
                        {
                            taints.Add(t);
                        }
                    }
                }
            }

            return taints;
        }

        /// <summary>
        /// Downloads and installs any required binaries to the workstation cache if they're not already present.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        public static async Task InstallWorkstationBinariesAsync(ObjectDictionary setupState)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));

            var cluster           = setupState.Get<ClusterProxy>(KubeSetup.ClusterProxyProperty);
            var firstMaster       = cluster.FirstMaster;
            var hostPlatform      = KubeHelper.HostPlatform;
            var cachedKubeCtlPath = KubeHelper.GetCachedComponentPath(hostPlatform, "kubectl", KubeVersions.KubernetesVersion);
            var cachedHelmPath    = KubeHelper.GetCachedComponentPath(hostPlatform, "helm", KubeVersions.HelmVersion);

            string kubeCtlUri;
            string helmUri;

            switch (hostPlatform)
            {
                case KubeClientPlatform.Linux:

                    kubeCtlUri = KubeDownloads.KubeCtlLinuxUri;
                    helmUri    = KubeDownloads.HelmLinuxUri;
                    break;

                case KubeClientPlatform.Osx:

                    kubeCtlUri = KubeDownloads.KubeCtlOsxUri;
                    helmUri    = KubeDownloads.HelmOsxUri;
                    break;

                case KubeClientPlatform.Windows:

                    kubeCtlUri = KubeDownloads.KubeCtlWindowsUri;
                    helmUri    = KubeDownloads.HelmWindowsUri;
                    break;

                default:

                    throw new NotSupportedException($"Unsupported workstation platform [{hostPlatform}]");
            }

            // Download the components if they're not already cached.

            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using (var httpClient = new HttpClient(handler, disposeHandler: true))
            {
                if (!File.Exists(cachedKubeCtlPath))
                {
                    firstMaster.Status = "download: kubectl";

                    using (var response = await httpClient.GetStreamAsync(kubeCtlUri))
                    {
                        using (var output = new FileStream(cachedKubeCtlPath, FileMode.Create, FileAccess.ReadWrite))
                        {
                            await response.CopyToAsync(output);
                        }
                    }
                }

                if (!File.Exists(cachedHelmPath))
                {
                    firstMaster.Status = "download: Helm";

                    using (var response = await httpClient.GetStreamAsync(helmUri))
                    {
                        // This is a [zip] file for Windows and a [tar.gz] file for Linux and OS/X.
                        // We're going to download to a temporary file so we can extract just the
                        // Helm binary.

                        var cachedTempHelmPath = cachedHelmPath + ".tmp";

                        try
                        {
                            using (var output = new FileStream(cachedTempHelmPath, FileMode.Create, FileAccess.ReadWrite))
                            {
                                await response.CopyToAsync(output);
                            }

                            switch (hostPlatform)
                            {
                                case KubeClientPlatform.Linux:
                                case KubeClientPlatform.Osx:

                                    throw new NotImplementedException($"Unsupported workstation platform [{hostPlatform}]");

                                case KubeClientPlatform.Windows:

                                    // The downloaded file is a ZIP archive for Windows.  We're going
                                    // to extract the [windows-amd64/helm.exe] file.

                                    using (var input = new FileStream(cachedTempHelmPath, FileMode.Open, FileAccess.ReadWrite))
                                    {
                                        using (var zip = new ZipFile(input))
                                        {
                                            foreach (ZipEntry zipEntry in zip)
                                            {
                                                if (!zipEntry.IsFile)
                                                {
                                                    continue;
                                                }

                                                if (zipEntry.Name == "windows-amd64/helm.exe")
                                                {
                                                    using (var zipStream = zip.GetInputStream(zipEntry))
                                                    {
                                                        using (var output = new FileStream(cachedHelmPath, FileMode.Create, FileAccess.ReadWrite))
                                                        {
                                                            zipStream.CopyTo(output);
                                                        }
                                                    }
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    break;

                                default:

                                    throw new NotSupportedException($"Unsupported workstation platform [{hostPlatform}]");
                            }
                        }
                        finally
                        {
                            NeonHelper.DeleteFile(cachedTempHelmPath);
                        }
                    }
                }
            }

            // We're going to assume that the workstation tools are backwards 
            // compatible with older versions of Kubernetes and other infrastructure
            // components and simply compare the installed tool (if present) version
            // with the requested tool version and overwrite the installed tool if
            // the new one is more current.

            KubeHelper.InstallKubeCtl();
            KubeHelper.InstallWorkstationHelm();

            firstMaster.Status = string.Empty;
        }

        /// <summary>
        /// <para>
        /// Connects to a Kubernetes cluster if it already exists.  This sets the <see cref="K8sClientProperty"/>
        /// property in the setup controller state when Kubernetes is running and a connection has not already 
        /// been established.
        /// </para>
        /// <note>
        /// The <see cref="K8sClientProperty"/> will not be set when Kubernetes has not been started, so 
        /// <see cref="ObjectDictionary.Get{TValue}(string)"/> calls for this property will fail when the
        /// cluster has not been connected yet, which will be useful for debugging setup steps that require
        /// a connection but this hasn't happened yet.
        /// </note>
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        public static void ConnectCluster(ObjectDictionary setupState)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));

            if (setupState.ContainsKey(K8sClientProperty))
            {
                return;     // Already connected
            }

            var cluster    = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var configFile = Environment.GetEnvironmentVariable("KUBECONFIG").Split(';').Where(s => s.Contains("config")).FirstOrDefault();

            if (!string.IsNullOrEmpty(configFile) && File.Exists(configFile))
            {
                var k8sClient = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(configFile, currentContext: cluster.KubeContext.Name));

                setupState.Add(K8sClientProperty, k8sClient);
            }
        }

        /// <summary>
        /// Configures a local HAProxy container that makes the Kubernetes Etc
        /// cluster highly available.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        public static void SetupEtcdHaProxy(ObjectDictionary setupState)
        {
            var cluster   = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var sbHaProxy = new StringBuilder();

            sbHaProxy.Append(
$@"global
    daemon
    log stdout  format raw  local0  info
    maxconn 32000

defaults
    balance                 roundrobin
    retries                 2
    http-reuse              safe
    timeout connect         5000
    timeout client          50000
    timeout server          50000
    timeout check           5000
    timeout http-keep-alive 500

frontend kubernetes_masters
    bind                    *:6442
    mode                    tcp
    log                     global
    option                  tcplog
    default_backend         kubernetes_masters_backend

backend kubernetes_masters_backend
    mode                    tcp
    balance                 roundrobin");

            foreach (var master in cluster.Masters)
            {
                sbHaProxy.Append(
$@"
    server {master.Name}         {master.Address}:6443");
            }

            foreach (var node in cluster.Nodes)
            {
                node.InvokeIdempotent("setup/etcd-ha",
                    () =>
                    {
                        node.Status = "setup: etcd HA";

                        node.UploadText("/etc/neonkube/neon-etcd-proxy.cfg", sbHaProxy);

                        node.SudoCommand("docker run",
                            "--name=neon-etcd-proxy",
                            "--detach",
                            "--restart=always",
                            "-v=/etc/neonkube/neon-etcd-proxy.cfg:/etc/haproxy/haproxy.cfg",
                            "--network=host",
                            "--log-driver=k8s-file",
                            $"{NeonHelper.NeonLibraryBranchRegistry}/haproxy:neonkube-{KubeConst.NeonKubeVersion}"
                        );
                    });
            }
        }

        /// <summary>
        /// Adds the Kubernetes node labels.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The first master node where the operation will be performed.</param>
        public static void LabelNodes(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = setupState.Get<ClusterProxy>(ClusterProxyProperty);

            master.InvokeIdempotent("setup/label-nodes",
                () =>
                {
                    master.Status = "label: nodes";

                    try
                    {
                        // Generate a Bash script we'll submit to the first master
                        // that initializes the labels for all nodes.

                        var sbScript = new StringBuilder();
                        var sbArgs = new StringBuilder();

                        sbScript.AppendLineLinux("#!/bin/bash");

                        foreach (var node in cluster.Nodes)
                        {
                            var labelDefinitions = new List<string>();

                            if (node.Metadata.IsWorker)
                            {
                                // Kubernetes doesn't set the role for worker nodes so we'll do that here.

                                labelDefinitions.Add("kubernetes.io/role=worker");
                            }

                            labelDefinitions.Add($"{NodeLabels.LabelDatacenter}={GetLabelValue(cluster.Definition.Datacenter.ToLowerInvariant())}");
                            labelDefinitions.Add($"{NodeLabels.LabelEnvironment}={GetLabelValue(cluster.Definition.Environment.ToString().ToLowerInvariant())}");

                            foreach (var label in node.Metadata.Labels.All)
                            {
                                labelDefinitions.Add($"{label.Key}={GetLabelValue(label.Value)}");
                            }

                            sbArgs.Clear();

                            foreach (var label in labelDefinitions)
                            {
                                sbArgs.AppendWithSeparator(label);
                            }

                            sbScript.AppendLine();
                            sbScript.AppendLineLinux($"kubectl label nodes --overwrite {node.Name} {sbArgs}");

                        }

                        master.SudoCommand(CommandBundle.FromScript(sbScript));
                    }
                    finally
                    {
                        master.Status = string.Empty;
                    }
                });
        }

        /// <summary>
        /// Initializes the cluster on the first manager, then joins the remaining
        /// masters and workers to the cluster.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="maxParallel">
        /// The maximum number of operations on separate nodes to be performed in parallel.
        /// This defaults to <see cref="defaultMaxParallelNodes"/>.
        /// </param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task SetupClusterAsync(ObjectDictionary setupState, int maxParallel = defaultMaxParallelNodes)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentException>(maxParallel > 0, nameof(maxParallel));

            var cluster      = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var clusterLogin = setupState.Get<ClusterLogin>(ClusterLoginProperty);
            var firstMaster  = cluster.FirstMaster;

            cluster.ClearStatus();

            ConfigureKubernetes(setupState, cluster.FirstMaster);
            ConfigureWorkstation(setupState, firstMaster);
            KubeSetup.ConnectCluster(setupState);
            InstallCalicoCni(setupState, firstMaster);
            LabelNodes(setupState, firstMaster);
            InstallIstio(setupState, firstMaster);
            CreateRootUser(setupState, firstMaster);
            InstallKubeDashboard(setupState, firstMaster);
            ConfigureMasterTaints(setupState, firstMaster);
            await InstallOpenEBSAsync(setupState, firstMaster);
            await CreateNeonNamespaceAsync(setupState, firstMaster);
            await InstallSystemDbAsync(setupState, firstMaster);
            await InstallClusterManagerAsync(setupState, firstMaster);
            await InstallContainerRegistryAsync(setupState, firstMaster);
            await InstallKialiAsync(setupState, firstMaster);
        }

        /// <summary>
        /// Basic Kubernetes cluster initialization.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="firstMaster">The first master node.</param>
        public static void ConfigureKubernetes(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> firstMaster)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(firstMaster != null, nameof(firstMaster));

            var cluster      = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var clusterLogin = setupState.Get<ClusterLogin>(ClusterLoginProperty);

            firstMaster.InvokeIdempotent("setup/cluster-init",
                () =>
                {
                    //---------------------------------------------------------
                    // Initialize the cluster on the first master:

                    firstMaster.Status = "create: cluster";

                    // Initialize Kubernetes:

                    firstMaster.InvokeIdempotent("setup/kubernetes-init",
                        () =>
                        {
                            firstMaster.Status = "initialize: cluster";

                            // It's possible that a previous cluster initialization operation
                            // was interrupted.  This command resets the state.

                            firstMaster.SudoCommand("kubeadm reset --force");

                            // Configure the control plane's API server endpoint and initialize
                            // the certificate SAN names to include each master IP address as well
                            // as the HOSTNAME/ADDRESS of the API load balancer (if any).

                            var controlPlaneEndpoint = $"kubernetes-masters:6442";
                            var sbCertSANs           = new StringBuilder();

                            if (!string.IsNullOrEmpty(cluster.Definition.Kubernetes.ApiLoadBalancer))
                            {
                                controlPlaneEndpoint = cluster.Definition.Kubernetes.ApiLoadBalancer;

                                var fields = cluster.Definition.Kubernetes.ApiLoadBalancer.Split(':');

                                sbCertSANs.AppendLine($"  - \"{fields[0]}\"");
                            }

                            foreach (var node in cluster.Masters)
                            {
                                sbCertSANs.AppendLine($"  - \"{node.Address}\"");
                            }

                            var clusterConfig =
$@"
apiVersion: kubeadm.k8s.io/v1beta2
kind: ClusterConfiguration
clusterName: {cluster.Name}
kubernetesVersion: ""v{KubeVersions.KubernetesVersion}""
imageRepository: ""{NeonHelper.NeonLibraryBranchRegistry}""
apiServer:
  extraArgs:
    logging-format: json
    default-not-ready-toleration-seconds: ""30"" # default 300
    default-unreachable-toleration-seconds: ""30"" #default  300
    allow-privileged: ""true""
  certSANs:
{sbCertSANs}
controlPlaneEndpoint: ""{controlPlaneEndpoint}""
networking:
  podSubnet: ""{cluster.Definition.Network.PodSubnet}""
  serviceSubnet: ""{cluster.Definition.Network.ServiceSubnet}""
controllerManager:
  extraArgs:
    logging-format: json
    node-monitor-grace-period: 15s #default 40s
    node-monitor-period: 5s #default 5s
    pod-eviction-timeout: 30s #default 5m0s
scheduler:
  extraArgs:
    logging-format: json
---
apiVersion: kubelet.config.k8s.io/v1beta1
kind: KubeletConfiguration
logging:
  format: json
nodeStatusReportFrequency: 4s
volumePluginDir: /var/lib/kubelet/volume-plugins
";
                            var kubeInitScript =
@"
systemctl enable kubelet.service
kubeadm init --config cluster.yaml
";
                            var response = firstMaster.SudoCommand(CommandBundle.FromScript(kubeInitScript).AddFile("cluster.yaml", clusterConfig));

                            // Extract the cluster join command from the response.  We'll need this to join
                            // other nodes to the cluster.

                            var output = response.OutputText;
                            var pStart = output.IndexOf(joinCommandMarker, output.IndexOf(joinCommandMarker) + 1);

                            if (pStart == -1)
                            {
                                throw new KubeException("Cannot locate the [kubadm join ...] command in the [kubeadm init ...] response.");
                            }

                            var pEnd = output.Length;

                            if (pEnd == -1)
                            {
                                clusterLogin.SetupDetails.ClusterJoinCommand = Regex.Replace(output.Substring(pStart).Trim(), @"\t|\n|\r|\\", "");
                            }
                            else
                            {
                                clusterLogin.SetupDetails.ClusterJoinCommand = Regex.Replace(output.Substring(pStart, pEnd - pStart).Trim(), @"\t|\n|\r|\\", "");
                            }

                            clusterLogin.Save();
                        });

                    firstMaster.Status = "created";

                    // kubectl config:

                    firstMaster.InvokeIdempotent("setup/kubectl",
                        () =>
                        {
                            // Edit the Kubernetes configuration file to rename the context:
                            //
                            //       CLUSTERNAME-admin@kubernetes --> root@CLUSTERNAME
                            //
                            // rename the user:
                            //
                            //      CLUSTERNAME-admin --> CLUSTERNAME-root 

                            var adminConfig = firstMaster.DownloadText("/etc/kubernetes/admin.conf");

                            adminConfig = adminConfig.Replace($"kubernetes-admin@{cluster.Definition.Name}", $"root@{cluster.Definition.Name}");
                            adminConfig = adminConfig.Replace("kubernetes-admin", $"root@{cluster.Definition.Name}");

                            firstMaster.UploadText("/etc/kubernetes/admin.conf", adminConfig, permissions: "600", owner: "root:root");
                        });

                    // Download the boot master files that will need to be provisioned on
                    // the remaining masters and may also be needed for other purposes
                    // (if we haven't already downloaded these).

                    if (clusterLogin.SetupDetails.MasterFiles != null)
                    {
                        clusterLogin.SetupDetails.MasterFiles = new Dictionary<string, KubeFileDetails>();
                    }

                    if (clusterLogin.SetupDetails.MasterFiles.Count == 0)
                    {
                        // I'm hardcoding the permissions and owner here.  It would be nice to
                        // scrape this from the source files in the future but it's not worth
                        // the bother at this point.

                        var files = new RemoteFile[]
                        {
                            new RemoteFile("/etc/kubernetes/admin.conf", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.crt", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.pub", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.key", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.key", "600", "root:root"),
                        };

                        foreach (var file in files)
                        {
                            var text = firstMaster.DownloadText(file.Path);

                            clusterLogin.SetupDetails.MasterFiles[file.Path] = new KubeFileDetails(text, permissions: file.Permissions, owner: file.Owner);
                        }
                    }

                    // Persist the cluster join command and downloaded master files.

                    clusterLogin.Save();

                    firstMaster.Status = "joined";

                    //---------------------------------------------------------
                    // Join the remaining masters to the cluster:

                    foreach (var master in cluster.Masters.Where(m => m != firstMaster))
                    {
                        try
                        {
                            master.InvokeIdempotent("setup/kubectl",
                                () =>
                                {
                                    // It's possible that a previous cluster join operation
                                    // was interrupted.  This command resets the state.

                                    master.SudoCommand("kubeadm reset --force");

                                    // The other (non-boot) masters need files downloaded from the boot master.

                                    master.Status = "upload: master files";

                                    foreach (var file in clusterLogin.SetupDetails.MasterFiles)
                                    {
                                        master.UploadText(file.Key, file.Value.Text, permissions: file.Value.Permissions, owner: file.Value.Owner);
                                    }

                                    // Join the cluster:

                                    master.InvokeIdempotent("setup/master-join",
                                        () =>
                                        {
                                            var joined = false;

                                            master.Status = "join: as master";

                                            for (int attempt = 0; attempt < maxJoinAttempts; attempt++)
                                            {
                                                var response = master.SudoCommand(clusterLogin.SetupDetails.ClusterJoinCommand + " --control-plane", RunOptions.Defaults & ~RunOptions.FaultOnError);

                                                if (response.Success)
                                                {
                                                    joined = true;
                                                    break;
                                                }

                                                Thread.Sleep(joinRetryDelay);
                                            }

                                            if (!joined)
                                            {
                                                throw new Exception($"Unable to join node [{master.Name}] to the after [{maxJoinAttempts}] attempts.");
                                            }
                                        });
                                });
                        }
                        catch (Exception e)
                        {
                            master.Fault(NeonHelper.ExceptionError(e));
                            master.LogException(e);
                        }

                        master.Status = "joined";
                    }

                    // Configure [kube-apiserver] on all the masters

                    foreach (var master in cluster.Masters)
                    {
                        try
                        {
                            master.Status = "configure: kubernetes apiserver";

                            master.InvokeIdempotent("setup/kubernetes-apiserver",
                                () =>
                                {
                                    master.Status = "configure: kube-apiserver";
                                    master.SudoCommand(CommandBundle.FromScript(
@"#!/bin/bash

sed -i 's/.*--enable-admission-plugins=.*/    - --enable-admission-plugins=NamespaceLifecycle,LimitRanger,ServiceAccount,DefaultStorageClass,DefaultTolerationSeconds,MutatingAdmissionWebhook,ValidatingAdmissionWebhook,Priority,ResourceQuota/' /etc/kubernetes/manifests/kube-apiserver.yaml
"));
                                }); 
                        }
                        catch (Exception e)
                        {
                            master.Fault(NeonHelper.ExceptionError(e));
                            master.LogException(e);
                        }

                        master.Status = string.Empty;
                    }

                    //---------------------------------------------------------
                    // Join the remaining workers to the cluster:

                    var parallelOptions = new ParallelOptions()
                    {
                        MaxDegreeOfParallelism = defaultMaxParallelNodes
                    };

                    Parallel.ForEach(cluster.Workers, parallelOptions,
                        worker =>
                        {
                            try
                            {
                                worker.InvokeIdempotent("setup/worker-join",
                                    () =>
                                    {
                                        var joined = false;

                                        worker.Status = "join: as worker";

                                        for (int attempt = 0; attempt < maxJoinAttempts; attempt++)
                                        {
                                            var response = worker.SudoCommand(clusterLogin.SetupDetails.ClusterJoinCommand, RunOptions.Defaults & ~RunOptions.FaultOnError);

                                            if (response.Success)
                                            {
                                                joined = true;
                                                break;
                                            }

                                            Thread.Sleep(joinRetryDelay);
                                        }

                                        if (!joined)
                                        {
                                            throw new Exception($"Unable to join node [{worker.Name}] to the cluster after [{maxJoinAttempts}] attempts.");
                                        }
                                    });
                            }
                            catch (Exception e)
                            {
                                worker.Fault(NeonHelper.ExceptionError(e));
                                worker.LogException(e);
                            }

                            worker.Status = "joined";
                        });
                });
        }

        /// <summary>
        /// Configures the local workstation.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void ConfigureWorkstation(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.InvokeIdempotent("setup/workstation",
                () =>
                {
                    var cluster        = setupState.Get<ClusterProxy>(ClusterProxyProperty);
                    var clusterLogin   = setupState.Get<ClusterLogin>(ClusterLoginProperty);
                    var kubeConfigPath = KubeHelper.KubeConfigPath;

                    // Update kubeconfig.

                    // $todo(marcusbooyah):
                    //
                    // This is hardcoding the kubeconfig to point to the first master.  Issue 
                    // https://github.com/nforgeio/neonKUBE/issues/888 will fix this by adding a proxy
                    // to neonDESKTOP and load balancing requests across the k8s api servers.

                    var configText = clusterLogin.SetupDetails.MasterFiles["/etc/kubernetes/admin.conf"].Text;

                    configText = configText.Replace("kubernetes-masters", $"{cluster.Definition.Masters.FirstOrDefault().Address}");

                    if (!File.Exists(kubeConfigPath))
                    {
                        File.WriteAllText(kubeConfigPath, configText);  
                    }
                    else
                    {
                        // The user already has an existing kubeconfig, so we need
                        // to merge in the new config.

                        var newConfig      = NeonHelper.YamlDeserialize<KubeConfig>(configText);
                        var existingConfig = KubeHelper.Config;

                        // Remove any existing user, context, and cluster with the same names.
                        // Note that we're assuming that there's only one of each in the config
                        // we downloaded from the cluster.

                        var newCluster      = newConfig.Clusters.Single();
                        var newContext      = newConfig.Contexts.Single();
                        var newUser         = newConfig.Users.Single();
                        var existingCluster = existingConfig.GetCluster(newCluster.Name);
                        var existingContext = existingConfig.GetContext(newContext.Name);
                        var existingUser    = existingConfig.GetUser(newUser.Name);

                        if (existingConfig != null)
                        {
                            existingConfig.Clusters.Remove(existingCluster);
                        }

                        if (existingContext != null)
                        {
                            existingConfig.Contexts.Remove(existingContext);
                        }

                        if (existingUser != null)
                        {
                            existingConfig.Users.Remove(existingUser);
                        }

                        existingConfig.Clusters.Add(newCluster);
                        existingConfig.Contexts.Add(newContext);
                        existingConfig.Users.Add(newUser);

                        existingConfig.CurrentContext = newContext.Name;

                        KubeHelper.SetConfig(existingConfig);
                    }
                });
        }

        /// <summary>
        /// Installs the Calico CNI.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void InstallCalicoCni(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;

            master.InvokeIdempotent("setup/cluster-deploy-cni",
                () =>
                {
                    // Deploy Calico

                    var script =
$@"
# We need to edit the setup manifest to specify the 
# cluster subnet before applying it.

curl {KubeHelper.CurlOptions} {KubeDownloads.CalicoSetupYamlUri} > /tmp/calico.yaml
sed -i 's;192.168.0.0/16;{cluster.Definition.Network.PodSubnet};' /tmp/calico.yaml
sed -i 's;calico/cni:v{KubeVersions.CalicoVersion}.*;{NeonHelper.NeonLibraryBranchRegistry}/calico-cni:neonkube-{KubeConst.NeonKubeVersion};' /tmp/calico.yaml
sed -i 's;calico/kube-controllers:v{KubeVersions.CalicoVersion}.*;{NeonHelper.NeonLibraryBranchRegistry}/calico-kube-controllers:neonkube-{KubeConst.NeonKubeVersion};' /tmp/calico.yaml
sed -i 's;calico/node:v{KubeVersions.CalicoVersion}.*;{NeonHelper.NeonLibraryBranchRegistry}/calico-node:neonkube-{KubeConst.NeonKubeVersion};' /tmp/calico.yaml
sed -i 's;calico/pod2daemon-flexvol:v{KubeVersions.CalicoVersion}.*;{NeonHelper.NeonLibraryBranchRegistry}/calico-pod2daemon-flexvol:neonkube-{KubeConst.NeonKubeVersion};' /tmp/calico.yaml
kubectl apply -f /tmp/calico.yaml
rm /tmp/calico.yaml
";
                    master.SudoCommand(CommandBundle.FromScript(script));

                    // Wait for Calico and CoreDNS pods to report that they're running.
                    // We're going to wait a maximum of 300 seconds.

                    NeonHelper.WaitFor(
                        () =>
                        {
                            var pods = GetK8sClient(setupState).ListPodForAllNamespaces();

                            foreach (var pod in pods.Items)
                            {
                                if (pod.Status.Phase != "Running")
                                {
                                    if (pod.Metadata.Name.Contains("coredns") && pod.Status.Phase == "Pending")
                                    {
                                        master.SudoCommand("kubectl rollout restart --namespace kube-system deployment/coredns", RunOptions.LogOnErrorOnly);
                                    }

                                    return false;
                                }
                            }

                            return true;
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });
        }

        /// <summary>
        /// Configures pods to be schedule on masters when enabled.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void ConfigureMasterTaints(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;

            master.InvokeIdempotent("setup/kubernetes-master-pods",
                () =>
                {
                    // The [kubectl taint] command looks like it can return a non-zero exit code.
                    // We'll ignore this.

                    if (cluster.Definition.Kubernetes.AllowPodsOnMasters.GetValueOrDefault())
                    {
                        master.SudoCommand(@"until [ `kubectl get nodes | grep ""NotReady"" | wc -l ` == ""0"" ]; do sleep 1; done", master.DefaultRunOptions & ~RunOptions.FaultOnError);
                        master.SudoCommand("kubectl taint nodes --all node-role.kubernetes.io/master-", master.DefaultRunOptions & ~RunOptions.FaultOnError);
                        master.SudoCommand(@"until [ `kubectl get nodes -o json | jq .items[].spec | grep ""NoSchedule"" | wc -l ` == ""0"" ]; do sleep 1; done", master.DefaultRunOptions & ~RunOptions.FaultOnError);
                    }
                });
        }

        /// <summary>
        /// Installs Istio.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void InstallIstio(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "deploy: istio";

            master.InvokeIdempotent("setup/istio",
                () =>
                {
                    var istioScript0 =
$@"
tmp=$(mktemp -d /tmp/istioctl.XXXXXX)
cd ""$tmp"" || exit

curl -fsLO {KubeDownloads.IstioLinuxUri}

tar -xzf ""istioctl-{KubeVersions.IstioVersion}-linux-amd64.tar.gz""

# setup istioctl
cd ""$HOME"" || exit
mkdir -p "".istioctl/bin""
mv ""${{tmp}}/istioctl"" "".istioctl/bin/istioctl""
chmod +x "".istioctl/bin/istioctl""
rm -r ""${{tmp}}""

export PATH=$PATH:$HOME/.istioctl/bin

istioctl operator init --set hub={NeonHelper.NeonLibraryBranchRegistry}

kubectl create ns istio-system

cat <<EOF > istio-cni.yaml
apiVersion: install.istio.io/v1alpha1
kind: IstioOperator
metadata:
  namespace: istio-system
  name: istiocontrolplane
spec:
  hub: {NeonHelper.NeonLibraryBranchRegistry}
  tag: neonkube-{KubeConst.NeonKubeVersion}
  meshConfig:
    rootNamespace: istio-system
  components:
    ingressGateways:
    - name: istio-ingressgateway
      enabled: true
      k8s:
        overlays:
          - apiVersion: apps/v1
            kind: Deployment
            name: istio-ingressgateway
            patches:
              - path: kind
                value: DaemonSet
        service:
          ports:
          - name: http2
            protocol: TCP
            port: 80
            targetPort: 8080
            nodePort: 30080
          - name: https
            protocol: TCP
            port: 443
            targetPort: 8443
            nodePort: 30443
          - name: tls
            protocol: TCP
            port: 15443
            targetPort: 15443
            nodePort: 31922
        resources:
          requests:
            cpu: 100m
            memory: 128Mi
          limits:
            cpu: 2000m
            memory: 1024Mi
        strategy:
          rollingUpdate:
            maxSurge: ""100%""
            maxUnavailable: ""25%""
    cni:
      enabled: true
      namespace: kube-system
  values:
    global:
      logging:
        level: ""default:info""
      logAsJson: true
      defaultNodeSelector: 
        neonkube.io/istio: true
      tracer:
        zipkin:
          address: neon-logging-jaeger-collector.monitoring.svc.cluster.local:9411
    pilot:
      traceSampling: 100
    meshConfig:
      accessLogFile: """"
      accessLogFormat: '{{   ""authority"": ""%REQ(:AUTHORITY)%"",   ""mode"": ""%PROTOCOL%"",   ""upstream_service_time"": ""%RESP(X-ENVOY-UPSTREAM-SERVICE-TIME)%"",   ""upstream_local_address"": ""%UPSTREAM_LOCAL_ADDRESS%"",   ""duration"": ""%DURATION%"",   ""request_duration"": ""%REQUEST_DURATION%"",   ""response_duration"": ""%RESPONSE_DURATION%"",   ""response_tx_duration"": ""%RESPONSE_TX_DURATION%"",   ""downstream_local_address"": ""%DOWNSTREAM_LOCAL_ADDRESS%"",   ""upstream_transport_failure_reason"": ""%UPSTREAM_TRANSPORT_FAILURE_REASON%"",   ""route_name"": ""%ROUTE_NAME%"",   ""response_code"": ""%RESPONSE_CODE%"",   ""response_code_details"": ""%RESPONSE_CODE_DETAILS%"",   ""user_agent"": ""%REQ(USER-AGENT)%"",   ""response_flags"": ""%RESPONSE_FLAGS%"",   ""start_time"": ""%START_TIME(%s.%6f)%"",   ""method"": ""%REQ(:METHOD)%"",   ""host"": ""%REQ(:Host)%"",   ""referer"": ""%REQ(:Referer)%"",   ""request_id"": ""%REQ(X-REQUEST-ID)%"",   ""forwarded_host"": ""%REQ(X-FORWARDED-HOST)%"",   ""forwarded_proto"": ""%REQ(X-FORWARDED-PROTO)%"",   ""upstream_host"": ""%UPSTREAM_HOST%"",   ""downstream_local_uri_san"": ""%DOWNSTREAM_LOCAL_URI_SAN%"",   ""downstream_peer_uri_san"": ""%DOWNSTREAM_PEER_URI_SAN%"",   ""downstream_local_subject"": ""%DOWNSTREAM_LOCAL_SUBJECT%"",   ""downstream_peer_subject"": ""%DOWNSTREAM_PEER_SUBJECT%"",   ""downstream_peer_issuer"": ""%DOWNSTREAM_PEER_ISSUER%"",   ""downstream_tls_session_id"": ""%DOWNSTREAM_TLS_SESSION_ID%"",   ""downstream_tls_cipher"": ""%DOWNSTREAM_TLS_CIPHER%"",   ""downstream_tls_version"": ""%DOWNSTREAM_TLS_VERSION%"",   ""downstream_peer_serial"": ""%DOWNSTREAM_PEER_SERIAL%"",   ""downstream_peer_cert"": ""%DOWNSTREAM_PEER_CERT%"",   ""client_ip"": ""%REQ(X-FORWARDED-FOR)%"",   ""requested_server_name"": ""%REQUESTED_SERVER_NAME%"",   ""bytes_received"": ""%BYTES_RECEIVED%"",   ""bytes_sent"": ""%BYTES_SENT%"",   ""upstream_cluster"": ""%UPSTREAM_CLUSTER%"",   ""downstream_remote_address"": ""%DOWNSTREAM_REMOTE_ADDRESS%"",   ""path"": ""%REQ(X-ENVOY-ORIGINAL-PATH?:PATH)%"" }}'
      accessLogEncoding: ""JSON""
    gateways:
      istio-ingressgateway:
        type: NodePort
        externalTrafficPolicy: Local
        sds:
          enabled: true
    prometheus:
      enabled: false
    grafana:
      enabled: false
    istiocoredns:
      enabled: true
      coreDNSImage: {NeonHelper.NeonLibraryBranchRegistry}/coredns-coredns
      coreDNSTag: {KubeVersions.CoreDNSVersion}
      coreDNSPluginImage: {NeonHelper.NeonLibraryBranchRegistry}/coredns-plugin:neonkube-{KubeConst.NeonKubeVersion}
    cni:
      excludeNamespaces:
       - istio-system
       - kube-system
       - kube-node-lease
       - kube-public
       - jobs
      logLevel: info
EOF

istioctl install -f istio-cni.yaml
";
                master.SudoCommand(CommandBundle.FromScript(istioScript0));
            });
        }

        /// <summary>
        /// Configures the root Kubernetes user.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void CreateRootUser(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "create: kubernetes root user";

            master.InvokeIdempotent("setup/root-user",
                () =>
                {
                    var userYaml =
$@"
apiVersion: v1
kind: ServiceAccount
metadata:
  name: {KubeConst.RootUser}-user
  namespace: kube-system
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: {KubeConst.RootUser}-user
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: cluster-admin
subjects:
- kind: ServiceAccount
  name: {KubeConst.RootUser}-user
  namespace: kube-system
";
                    master.KubectlApply(userYaml);
                });
        }

        /// <summary>
        /// Configures the root Kubernetes user.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void InstallKubeDashboard(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster      = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var clusterLogin = setupState.Get<ClusterLogin>(ClusterLoginProperty);

            master.Status = "install: kubernetes dashboard";

            master.InvokeIdempotent("setup/kube-dashboard",
                () =>
                {
                    if (clusterLogin.DashboardCertificate != null)
                    {
                        master.Status = "generate: dashboard certificate";

                        // We're going to tie the custom certificate to the IP addresses
                        // of the master nodes only.  This means that only these nodes
                        // can accept the traffic and also that we'd need to regenerate
                        // the certificate if we add/remove a master node.
                        //
                        // Here's the tracking task:
                        //
                        //      https://github.com/nforgeio/neonKUBE/issues/441

                        var masterAddresses = new List<string>();

                        foreach (var master in cluster.Masters)
                        {
                            masterAddresses.Add(master.Address.ToString());
                        }

                        var utcNow = DateTime.UtcNow;
                        var utc10Years = utcNow.AddYears(10);

                        var certificate = TlsCertificate.CreateSelfSigned(
                            hostnames: masterAddresses,
                            validDays: (int)(utc10Years - utcNow).TotalDays,
                            issuedBy: "kubernetes-dashboard");

                        clusterLogin.DashboardCertificate = certificate.CombinedPem;
                        clusterLogin.Save();
                    }

                    // Deploy the dashboard.  Note that we need to insert the base-64
                    // encoded certificate and key PEM into the dashboard configuration
                    // YAML first.

                    master.Status = "deploy: kubernetes dashboard";

                    var dashboardYaml =
$@"# Copyright 2017 The Kubernetes Authors.
#
# Licensed under the Apache License, Version 2.0 (the """"License"""");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
# http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an """"AS IS"""" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.


apiVersion: v1
kind: Namespace
metadata:
  name: kubernetes-dashboard

---

apiVersion: v1
kind: ServiceAccount
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard

---

kind: Service
apiVersion: v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
spec:
  type: NodePort
  ports:
  - port: 443
    targetPort: 8443
    nodePort: {KubeNodePorts.KubeDashboard}
  selector:
    k8s-app: kubernetes-dashboard

---

apiVersion: v1
kind: Secret
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-certs
  namespace: kubernetes-dashboard
type: Opaque
data:
  cert.pem: $<CERTIFICATE>
  key.pem: $<PRIVATEKEY>

---

apiVersion: v1
kind: Secret
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-csrf
  namespace: kubernetes-dashboard
type: Opaque
data:
  csrf: """"

---

apiVersion: v1
kind: Secret
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-key-holder
  namespace: kubernetes-dashboard
type: Opaque

---

kind: ConfigMap
apiVersion: v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-settings
  namespace: kubernetes-dashboard

---

kind: Role
apiVersion: rbac.authorization.k8s.io/v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
rules:
# Allow Dashboard to get, update and delete Dashboard exclusive secrets.
  - apiGroups: [""""]
    resources: [""secrets""]
    resourceNames: [""kubernetes-dashboard-key-holder"", ""kubernetes-dashboard-certs"", ""kubernetes-dashboard-csrf""]
    verbs: [""get"", ""update"", ""delete""]
# Allow Dashboard to get and update 'kubernetes-dashboard-settings' config map.
  - apiGroups: [""""]
    resources: [""configmaps""]
    resourceNames: [""kubernetes-dashboard-settings""]
    verbs: [""get"", ""update""]
# Allow Dashboard to get metrics.
  - apiGroups: [""""]
    resources: [""services""]
    resourceNames: [""heapster"", ""dashboard-metrics-scraper""]
    verbs: [""proxy""]
  - apiGroups: [""""]
    resources: [""services/proxy""]
    resourceNames: [""heapster"", ""http:heapster:"", ""https:heapster:"", ""dashboard-metrics-scraper"", ""http:dashboard-metrics-scraper""]
    verbs: [""get""]

---

kind: ClusterRole
apiVersion: rbac.authorization.k8s.io/v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
rules:
# Allow Metrics Scraper to get metrics from the Metrics server
  - apiGroups: [""metrics.k8s.io""]
    resources: [""pods"", ""nodes""]
    verbs: [""get"", ""list"", ""watch""]

---

apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: kubernetes-dashboard
subjects:
  - kind: ServiceAccount
    name: kubernetes-dashboard
    namespace: kubernetes-dashboard

---

apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: kubernetes-dashboard
subjects:
  - kind: ServiceAccount
    name: kubernetes-dashboard
    namespace: kubernetes-dashboard

---

kind: Deployment
apiVersion: apps/v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
spec:
  replicas: 1
  revisionHistoryLimit: 10
  selector:
    matchLabels:
      k8s-app: kubernetes-dashboard
  template:
    metadata:
      labels:
        k8s-app: kubernetes-dashboard
    spec:
      containers:
        - name: kubernetes-dashboard
          image: {NeonHelper.NeonLibraryBranchRegistry}/kubernetesui-dashboard:neonkube-{KubeConst.NeonKubeVersion}
          imagePullPolicy: Always
          ports:
            - containerPort: 8443
              protocol: TCP
          args:
            - --auto-generate-certificates=false
            - --tls-cert-file=cert.pem
            - --tls-key-file=key.pem
            - --namespace=kubernetes-dashboard
# Uncomment the following line to manually specify Kubernetes API server Host
# If not specified, Dashboard will attempt to auto discover the API server and connect
# to it. Uncomment only if the default does not work.
# - --apiserver-host=http://my-address:port
          volumeMounts:
            - name: kubernetes-dashboard-certs
              mountPath: /certs
# Create on-disk volume to store exec logs
            - mountPath: /tmp
              name: tmp-volume
          livenessProbe:
            httpGet:
              scheme: HTTPS
              path: /
              port: 8443
            initialDelaySeconds: 30
            timeoutSeconds: 30
      volumes:
        - name: kubernetes-dashboard-certs
          secret:
            secretName: kubernetes-dashboard-certs
        - name: tmp-volume
          emptyDir: {{}}
      serviceAccountName: kubernetes-dashboard
# Comment the following tolerations if Dashboard must not be deployed on master
      tolerations:
        - key: node-role.kubernetes.io/master
          effect: NoSchedule

---

kind: Service
apiVersion: v1
metadata:
  labels:
    k8s-app: dashboard-metrics-scraper
  name: dashboard-metrics-scraper
  namespace: kubernetes-dashboard
spec:
  ports:
    - port: 8000
      targetPort: 8000
  selector:
    k8s-app: dashboard-metrics-scraper

---

kind: Deployment
apiVersion: apps/v1
metadata:
  labels:
    k8s-app: dashboard-metrics-scraper
  name: dashboard-metrics-scraper
  namespace: kubernetes-dashboard
spec:
  replicas: 1
  revisionHistoryLimit: 10
  selector:
    matchLabels:
      k8s-app: dashboard-metrics-scraper
  template:
    metadata:
      labels:
        k8s-app: dashboard-metrics-scraper
    spec:
      containers:
        - name: dashboard-metrics-scraper
          image: {NeonHelper.NeonLibraryBranchRegistry}/kubernetesui-metrics-scraper:neonkube-{KubeConst.NeonKubeVersion}
          ports:
            - containerPort: 8000
              protocol: TCP
          livenessProbe:
            httpGet:
              scheme: HTTP
              path: /
              port: 8000
            initialDelaySeconds: 30
            timeoutSeconds: 30
          volumeMounts:
          - mountPath: /tmp
            name: tmp-volume
      serviceAccountName: kubernetes-dashboard
# Comment the following tolerations if Dashboard must not be deployed on master
      tolerations:
        - key: node-role.kubernetes.io/master
          effect: NoSchedule
      volumes:
        - name: tmp-volume
          emptyDir: {{}}
";

                    var dashboardCert = TlsCertificate.Parse(clusterLogin.DashboardCertificate);
                    var variables = new Dictionary<string, string>();

                    variables.Add("CERTIFICATE", Convert.ToBase64String(Encoding.UTF8.GetBytes(dashboardCert.CertPemNormalized)));
                    variables.Add("PRIVATEKEY", Convert.ToBase64String(Encoding.UTF8.GetBytes(dashboardCert.KeyPemNormalized)));

                    using (var preprocessReader =
                        new PreprocessReader(dashboardYaml, variables)
                        {
                            StripComments = false,
                            ProcessStatements = false
                        }
                    )
                    {
                        dashboardYaml = preprocessReader.ReadToEnd();
                    }

                    master.KubectlApply(dashboardYaml);
                });
        }

        /// <summary>
        /// Initializes the EFK stack and other monitoring services.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task SetupMonitoringAsync(ObjectDictionary setupState)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));

            var cluster     = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var firstMaster = cluster.FirstMaster;

            await KubeSetupAsync(setupState, firstMaster);
            await InstallNeonMetricsAsync(setupState, firstMaster);
            await InstallJaegerAsync(setupState, firstMaster);
        }

        /// <summary>
        /// Adds the node taints.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        public static void TaintNodes(ObjectDictionary setupState)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));

            var cluster     = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var firstMaster = cluster.FirstMaster;

            firstMaster.InvokeIdempotent("setup/cluster-taint-nodes",
                () =>
                {
                    firstMaster.Status = "taint: nodes";

                    try
                    {
                        // Generate a Bash script we'll submit to the first master
                        // that initializes the taints for all nodes.

                        var sbScript = new StringBuilder();
                        var sbArgs = new StringBuilder();

                        sbScript.AppendLineLinux("#!/bin/bash");

                        foreach (var node in cluster.Nodes)
                        {
                            var taintDefinitions = new List<string>();

                            if (node.Metadata.IsWorker)
                            {
                                // Kubernetes doesn't set the role for worker nodes so we'll do that here.

                                taintDefinitions.Add("kubernetes.io/role=worker");
                            }

                            taintDefinitions.Add($"{NodeLabels.LabelDatacenter}={GetLabelValue(cluster.Definition.Datacenter.ToLowerInvariant())}");
                            taintDefinitions.Add($"{NodeLabels.LabelEnvironment}={GetLabelValue(cluster.Definition.Environment.ToString().ToLowerInvariant())}");

                            if (node.Metadata.Taints != null)
                            {
                                foreach (var taint in node.Metadata.Taints)
                                {
                                    sbScript.AppendLine();
                                    sbScript.AppendLineLinux($"kubectl taint nodes {node.Name} {taint}");
                                }
                            }
                        }

                        firstMaster.SudoCommand(CommandBundle.FromScript(sbScript));
                    }
                    finally
                    {
                        firstMaster.Status = string.Empty;
                    }
                });
        }

        /// <summary>
        /// Deploy Kiali.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        private static async Task InstallKialiAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "deploy: kiali";
           
            await master.InvokeIdempotentAsync("setup/kiali",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("cr.spec.deployment.image_name", $"{NeonHelper.NeonLibraryBranchRegistry}/kiali-kiali"));
                    values.Add(new KeyValuePair<string, object>("cr.spec.deployment.image_version", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelIstio, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync("kiali", releaseName: "kiali-operator", @namespace: "istio-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/kiali-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("istio-system", labelSelector: "app=kiali-operator");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("istio-system", labelSelector: "app=kiali");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                   });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Some initial kubernetes configuration.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task KubeSetupAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "deploy: cluster-setup";

            await master.InvokeIdempotentAsync("", async () => await master.InstallHelmChartAsync("cluster-setup"));
        }

        /// <summary>
        /// Installs OpenEBS.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallOpenEBSAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = setupState.Get<ClusterProxy>(ClusterProxyProperty);

            master.Status = "deploy: openebs";

            master.InvokeIdempotent("setup/openebs-namespace",
                () =>
                {
                    GetK8sClient(setupState).CreateNamespace(new V1Namespace()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = "openebs",
                            Labels = new Dictionary<string, string>()
                            {
                                { "istio-injection", "disabled" }
                            }
                        }
                    });
                });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-install",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("apiserver.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("apiserver.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("helper.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("helper.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("localprovisioner.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("localprovisioner.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("policies.monitoring.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("policies.monitoring.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("snapshotOperator.controller.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("snapshotOperator.controller.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("snapshotOperator.provisioner.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("snapshotOperator.provisioner.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("provisioner.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("provisioner.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("ndm.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("ndm.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("ndmOperator.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("ndmOperator.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("webhook.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("webhook.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("jiva.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("jiva.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    if (cluster.Definition.Workers.Count() >= 3)
                    {
                        var replicas = Math.Max(1, cluster.Definition.Workers.Count() / 3);

                        values.Add(new KeyValuePair<string, object>($"apiserver.replicas", replicas));
                        values.Add(new KeyValuePair<string, object>($"provisioner.replicas", replicas));
                        values.Add(new KeyValuePair<string, object>($"localprovisioner.replicas", replicas));
                        values.Add(new KeyValuePair<string, object>($"snapshotOperator.replicas", replicas));
                        values.Add(new KeyValuePair<string, object>($"ndmOperator.replicas", 1));
                        values.Add(new KeyValuePair<string, object>($"webhook.replicas", replicas));
                    }

                    await master.InstallHelmChartAsync("openebs", releaseName: "neon-storage-openebs", values: values, @namespace: "openebs");
                });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-cstor-install",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();
                    values.Add(new KeyValuePair<string, object>("cspcOperator.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.poolManager.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.poolManager.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.cstorPool.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.cstorPool.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.cstorPoolExporter.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cspcOperator.cstorPoolExporter.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("cvcOperator.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.target.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.target.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.volumeMgmt.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.volumeMgmt.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.volumeExporter.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cvcOperator.volumeExporter.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("csiController.resizer.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiController.resizer.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("csiController.snapshotter.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiController.snapshotter.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("csiController.snapshotController.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiController.snapshotController.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("csiController.attacher.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiController.attacher.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("csiController.provisioner.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiController.provisioner.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("csiController.driverRegistrar.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiController.driverRegistrar.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("cstorCSIPlugin.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("cstorCSIPlugin.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("csiNode.driverRegistrar.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("csiNode.driverRegistrar.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("admissionServer.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("admissionServer.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    await master.InstallHelmChartAsync("openebs-cstor-operator", releaseName: "neon-storage-openebs-cstor", values: values, @namespace: "openebs");
                });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-install-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("openebs");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        }, 
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var daemonsets = await GetK8sClient(setupState).ListNamespacedDaemonSetAsync("openebs");
                            if (daemonsets == null || daemonsets.Items.Count == 0)
                            {
                                return false;
                            }

                            return daemonsets.Items.All(p => p.Status.NumberAvailable == p.Status.DesiredNumberScheduled);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                   });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-cstor-poolcluster",
                async () =>
                {
                    var cStorPoolCluster = new V1CStorPoolCluster()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name              = "cspc-stripe",
                            NamespaceProperty = "openebs"
                        },
                        Spec = new V1CStorPoolClusterSpec()
                        {
                            Pools = new List<V1CStorPoolSpec>()
                        }
                    };

                    var blockDevices = ((JObject)await GetK8sClient(setupState).ListNamespacedCustomObjectAsync("openebs.io", "v1alpha1", "openebs", "blockdevices")).ToObject<V1CStorBlockDeviceList>();

                    foreach (var n in cluster.Definition.Nodes)
                    {
                        if (blockDevices.Items.Any(bd => bd.Spec.NodeAttributes.GetValueOrDefault("nodeName") == n.Name))
                        {
                            var pool = new V1CStorPoolSpec()
                                {
                                    NodeSelector = new Dictionary<string, string>()
                                    {
                                        { "kubernetes.io/hostname", n.Name }
                                    },
                                    DataRaidGroups = new List<V1CStorDataRaidGroup>()
                                    {
                                        new V1CStorDataRaidGroup()
                                        {
                                            BlockDevices = new List<V1CStorBlockDeviceRef>()
                                        }
                                    },
                                    PoolConfig = new V1CStorPoolConfig()
                                    {
                                        DataRaidGroupType = DataRaidGroupType.Stripe,
                                        Tolerations = new List<V1Toleration>()
                                        {
                                            { new V1Toleration() { Effect = "NoSchedule", OperatorProperty = "Exists" } },
                                            { new V1Toleration() { Effect = "NoExecute", OperatorProperty = "Exists" } }
                                        }
                                    }
                                };

                            foreach (var bd in blockDevices.Items.Where(bd => bd.Spec.NodeAttributes.GetValueOrDefault("nodeName") == n.Name))
                            {
                                pool.DataRaidGroups.FirstOrDefault().BlockDevices.Add(
                                    new V1CStorBlockDeviceRef()
                                    {
                                        BlockDeviceName = bd.Metadata.Name
                                    });
                            }

                            cStorPoolCluster.Spec.Pools.Add(pool);
                        }
                    }

                    GetK8sClient(setupState).CreateNamespacedCustomObject(cStorPoolCluster, V1CStorPoolCluster.KubeGroup, V1CStorPoolCluster.KubeApiVersion, "openebs", "cstorpoolclusters");
                });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-cstor-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("openebs", labelSelector: "app=cstor-pool");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            master.InvokeIdempotent("setup/neon-storage-openebs-cstor-storageclass",
                () =>
                {
                    var storageClass = new V1StorageClass()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = "cstor-csi-stripe"
                        },
                        Provisioner = "cstor.csi.openebs.io",
                        AllowVolumeExpansion = true,
                        Parameters = new Dictionary<string, string>()
                        {
                            { "cas-type", "cstor" },
                            { "cstorPoolCluster", "cspc-stripe" },
                            { "replicaCount", "3" }
                        }
                    };
                    GetK8sClient(setupState).CreateStorageClass(storageClass);
                });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-nfs-install",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>($"persistence.size", ByteUnits.Parse(cluster.Definition.OpenEbs.NfsSize)));
                      
                    await master.InstallHelmChartAsync("nfs", releaseName: "neon-storage-nfs", @namespace: "openebs", values: values);
                });

            await master.InvokeIdempotentAsync("setup/neon-storage-openebs-nfs-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var statefulsets = await GetK8sClient(setupState).ListNamespacedStatefulSetAsync("openebs", labelSelector: "release=neon-storage-nfs");
                            if (statefulsets == null || statefulsets.Items.Count == 0)
                            {
                                return false;
                            }

                            return statefulsets.Items.All(p => p.Status.ReadyReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs OpenEBS
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateNeonNamespaceAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/cluster-neon-system-namespace",
                async () =>
                {
                    await GetK8sClient(setupState).CreateNamespaceAsync(new V1Namespace()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = "neon-system",
                            Labels = new Dictionary<string, string>()
                            {
                                { "istio-injection", "enabled" }
                            }
                        }
                    });
                });
        }

        /// <summary>
        /// Setup Kube state metrics.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallKubeStateMetricsAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "deploy: kube-state-metrics";

            await master.InstallHelmChartAsync("kubernetes");
        }

        /// <summary>
        /// Installs an Etcd cluster to the monitoring namespace.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallEtcdAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = setupState.Get<ClusterProxy>(ClusterProxyProperty);

            master.Status = "deploy: neon-metrics-etcd-cluster";

            await master.InvokeIdempotentAsync("deploy/neon-metrics-etcd-cluster",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>($"replicas", cluster.Definition.Nodes.Count(n => n.Labels.Metrics == true).ToString()));

                    values.Add(new KeyValuePair<string, object>($"volumeClaimTemplate.resources.requests.storage", "1Gi"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync("etcd-cluster", releaseName: "neon-metrics-etcd", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-metrics-etcd-cluster-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var statefulsets = await GetK8sClient(setupState).ListNamespacedStatefulSetAsync("monitoring", labelSelector: "release=neon-metrics-etcd");
                            if (statefulsets == null || statefulsets.Items.Count == 0)
                            {
                                return false;
                            }

                            return statefulsets.Items.All(p => p.Status.ReadyReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs an Metrics cluster to the monitoring namespace.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallNeonMetricsAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster      = setupState.Get<ClusterProxy>(ClusterProxyProperty);
            var cortexValues = new List<KeyValuePair<string, object>>();

            master.Status = "deploy: neon-metrics";

            await master.InvokeIdempotentAsync("deploy/neon-metrics-prometheus",
                async () =>
                {
                    master.Status = "deploy: neon-metrics-prometheus";

                    var values = new List<KeyValuePair<string, object>>();
                    
                    values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.image.tag", KubeVersions.AlertManagerVersion));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.tlsProxy.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.tlsProxy.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    //values.Add(new KeyValuePair<string, object>($"prometheusOperator.image.tag", KubeVersions.PrometheusVersion));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.configmapReloadImage.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.configmapReloadImage.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.prometheusConfigReloaderImage.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.prometheusConfigReloaderImage.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.image.tag", KubeVersions.PrometheusVersion));
                    values.Add(new KeyValuePair<string, object>($"global.kubeStateMetrics.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"global.kubeStateMetrics.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"global.nodeExporter.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"global.nodeExporter.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.tolerations[{i}].operator", "Exists"));

                        i++;
                    }

                    await master.InstallHelmChartAsync("prometheus-operator", releaseName: "neon-metrics-prometheus", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-metrics-prometheus-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("monitoring", labelSelector: "release=neon-metrics-prometheus");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var daemonsets = await GetK8sClient(setupState).ListNamespacedDaemonSetAsync("monitoring", labelSelector: "release=neon-metrics-prometheus");
                            if (daemonsets == null || daemonsets.Items.Count == 0)
                            {
                                return false;
                            }

                            return daemonsets.Items.All(p => p.Status.NumberAvailable == p.Status.DesiredNumberScheduled);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var statefulsets = await GetK8sClient(setupState).ListNamespacedStatefulSetAsync("monitoring", labelSelector: "release=neon-metrics-prometheus");
                            if (statefulsets == null || statefulsets.Items.Count < 2)
                            {
                                return false;
                            }

                            return statefulsets.Items.All(p => p.Status.ReadyReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.lifecycler.ring.kvstore.store", "inmemory"));

            await master.InvokeIdempotentAsync("deploy/neon-metrics-cortex",
                async () =>
                {
                    if (cluster.Definition.Nodes.Any(n => n.Vm.GetMemory(cluster.Definition) < 4294965097L))
                    {
                        cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.retain_period", $"120s"));
                        cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.metadata_retain_period", $"5m"));
                        cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.querier.batch_iterators", true));
                        cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.querier.max_samples", 10000000));
                        cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.table_manager.retention_period", "12h"));
                    }

                    switch (cluster.Definition.Monitor.Metrics.Storage)
                    {
                        case MetricsStorageOptions.Ephemeral:

                            cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.schema.configs[0].store", $"boltdb"));
                            cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.schema.configs[0].object_store", $"filesystem"));
                            cortexValues.Add(new KeyValuePair<string, object>($"cortexConfig.schema.configs[0].object_store", $"filesystem"));
                            break;

                        case MetricsStorageOptions.Filesystem:

                            cortexValues.Add(new KeyValuePair<string, object>($"replicas", Math.Min(3, (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count()))));
                            // create folders
                            break;

                        default:

                            throw new NotImplementedException();
                    }

                    master.Status = "deploy: neon-metrics-cortex";

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelMetrics, "true"))
                    {
                        cortexValues.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        cortexValues.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        cortexValues.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    cortexValues.Add(new KeyValuePair<string, object>("image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    cortexValues.Add(new KeyValuePair<string, object>("image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    await master.InstallHelmChartAsync("cortex", releaseName: "neon-metrics-cortex", @namespace: "monitoring", values: cortexValues);
                });

            await master.InvokeIdempotentAsync("deploy/neon-metrics-cortex-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("monitoring", labelSelector: "release=neon-metrics-cortex");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await master.InvokeIdempotentAsync("deploy/istio-prometheus",
                async () =>
                {
                    master.Status = "deploy: neon-metrics-istio";

                    await master.InstallHelmChartAsync("istio-prometheus", @namespace: "monitoring");
                });

            await master.InvokeIdempotentAsync("deploy/neon-metrics-grafana",
                async () =>
                {
                    master.Status = "deploy: neon-metrics-grafana";

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("downloadDashboardsImage.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("downloadDashboardsImage.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>("sidecar.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("sidecar.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync("grafana", releaseName: "neon-metrics-grafana", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-metrics-grafana-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("monitoring", labelSelector: "release=neon-metrics-grafana");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs Jaeger
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <remarks>The tracking <see cref="Task"/>.</remarks>
        public static async Task InstallJaegerAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "deploy: jaeger";

            await master.InvokeIdempotentAsync("deploy/neon-logs-jaeger",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("agent.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("agent.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("collector.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("collector.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("query.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("query.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelLogs, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"ingester.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"ingester.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"ingester.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"agent.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"agent.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"agent.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"collector.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"collector.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"collector.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"query.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"query.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"query.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"esIndexCleaner.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"esIndexCleaner.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"esIndexCleaner.tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync("jaeger", releaseName: "neon-logs-jaeger", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-logs-jaeger-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("monitoring", labelSelector: "release=neon-logs-jaeger");
                            if (deployments == null || deployments.Items.Count < 2)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs a harbor container registry and required components.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallContainerRegistryAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = setupState.Get<ClusterProxy>(ClusterProxyProperty);

            master.Status = "deploy: registry";

            await master.InvokeIdempotentAsync("deploy/neon-system-registry-secret",
                async () =>
                {
                    var cert = TlsCertificate.CreateSelfSigned("*");

                    var harborCert = new V1Secret()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = "neon-registry-harbor"
                        },
                        Type = "Opaque",
                        StringData = new Dictionary<string, string>()
                        {
                            { "tls.crt", cert.CertPemNormalized },
                            { "tls.key", cert.KeyPemNormalized }
                        }
                    };

                    await GetK8sClient(setupState).CreateNamespacedSecretAsync(harborCert, "neon-system");
                });

            await master.InvokeIdempotentAsync("deploy/neon-system-registry-redis",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    var replicas = Math.Min(3, cluster.Definition.Masters.Count());
                    values.Add(new KeyValuePair<string, object>($"replicas", $"{replicas}"));

                    if (replicas < 2)
                    {
                        values.Add(new KeyValuePair<string, object>($"hardAntiAffinity", false));
                        values.Add(new KeyValuePair<string, object>($"sentinel.quorum", 1));
                    }

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelNeonSystemRegistry, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync("redis-ha", releaseName: "neon-system-registry-redis", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-system-registry-redis-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var statefulsets = await GetK8sClient(setupState).ListNamespacedStatefulSetAsync("neon-system", labelSelector: "release=neon-system-registry-redis");

                            if (statefulsets == null || statefulsets.Items.Count == 0)
                            {
                                return false;
                            }

                            return statefulsets.Items.All(p => p.Status.ReadyReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await master.InvokeIdempotentAsync("deploy/neon-system-registry-harbor",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("nginx.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("nginx.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("portal.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("portal.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("core.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("core.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("jobservice.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("jobservice.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("registry.registry.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("registry.registry.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("registry.controller.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("registry.controller.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("chartmuseum.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("chartmuseum.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("clair.clair.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("clair.clair.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("clair.adapter.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("clair.adapter.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("trivy.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("trivy.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("notary.server.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("notary.server.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>("notary.signer.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("notary.signer.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    
                    if (cluster.Definition.Masters.Count() > 1)
                    {
                        var redisConnStr = string.Empty;
                        for (int i = 0; i < Math.Min(3, cluster.Definition.Masters.Count()); i++)
                        {
                            if (i > 0)
                            {
                                redisConnStr += "\\,";
                            }

                            redisConnStr += $"neon-system-registry-redis-announce-{i}:26379";
                        }

                        values.Add(new KeyValuePair<string, object>($"redis.external.addr", redisConnStr));

                        values.Add(new KeyValuePair<string, object>($"redis.external.sentinelMasterSet", "master"));
                    }

                    int j = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelNeonSystemRegistry, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{j}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{j}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{j}].operator", "Exists"));
                        j++;
                    }

                    await master.InstallHelmChartAsync("harbor", releaseName: "neon-system-registry-harbor", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-system-registry-harbor-ready",
                async () =>
                {
                    var startUtc = DateTime.UtcNow;

                    await NeonHelper.WaitForAsync(
                           async () =>
                           {
                               // Restart pods if they aren't happy after 3 minutes.
                               if (DateTime.UtcNow > startUtc.AddMinutes(3))
                               {
                                   var pods = await GetK8sClient(setupState).ListNamespacedPodAsync("neon-system", labelSelector: "release=neon-system-registry-harbor");
                                   foreach (var p in pods.Items.Where(i => i.Status.Phase != "Running"))
                                   {
                                       if (p.Status.ContainerStatuses.Any(c => c.RestartCount > 0))
                                       {
                                           startUtc = DateTime.UtcNow;
                                           await GetK8sClient(setupState).DeleteNamespacedPodAsync(p.Name(), "neon-system");
                                       }
                                   }
                               }
                               var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("neon-system", labelSelector: "release=neon-system-registry-harbor");
                               if (deployments == null || deployments.Items.Count < 8)
                               {
                                   return false;
                               }

                               return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                           },
                           timeout:      clusterOpTimeout,
                           pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs the Neon Cluster Manager.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallClusterManagerAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            master.Status = "deploy: neon-cluster-manager";

            await master.InvokeIdempotentAsync("deploy/neon-cluster-manager",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>("image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    await master.InstallHelmChartAsync("neon-cluster-manager", releaseName: "neon-cluster-manager", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-cluster-manager-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("neon-system", labelSelector: "release=neon-cluster-manager");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs a Citus-postgres database used by neon-system services.
        /// </summary>
        /// <param name="setupState">The setup controller state.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallSystemDbAsync(ObjectDictionary setupState, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(setupState != null, nameof(setupState));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = setupState.Get<ClusterProxy>(ClusterProxyProperty);

            master.Status = "deploy: neon-system-db";

            await master.InvokeIdempotentAsync("deploy/neon-system-db",
                async () =>
                {
                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>($"image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"busybox.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"busybox.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));
                    values.Add(new KeyValuePair<string, object>($"prometheus.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"prometheus.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    values.Add(new KeyValuePair<string, object>($"manager.image.organization", NeonHelper.NeonLibraryBranchRegistry));
                    values.Add(new KeyValuePair<string, object>($"manager.image.tag", $"neonkube-{KubeConst.NeonKubeVersion}"));

                    var replicas = Math.Max(1, cluster.Definition.Masters.Count() / 5);

                    values.Add(new KeyValuePair<string, object>($"master.replicas", replicas));
                    values.Add(new KeyValuePair<string, object>($"manager.replicas", replicas));
                    values.Add(new KeyValuePair<string, object>($"worker.replicas", replicas));

                    if (replicas < 2)
                    {
                        values.Add(new KeyValuePair<string, object>($"manager.minimumWorkers", "1"));
                    }

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(setupState, NodeLabels.LabelNeonSystemDb, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync("citus-postgresql", releaseName: "neon-system-db", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("deploy/neon-system-db-ready",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var statefulsets = await GetK8sClient(setupState).ListNamespacedStatefulSetAsync("neon-system", labelSelector: "release=neon-system-db");
                            if (statefulsets == null || statefulsets.Items.Count < 2)
                            {
                                return false;
                            }

                            return statefulsets.Items.All(p => p.Status.ReadyReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(setupState).ListNamespacedDeploymentAsync("neon-system", labelSelector: "release=neon-system-db");
                            if (deployments == null || deployments.Items.Count == 0)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }
    }
}
