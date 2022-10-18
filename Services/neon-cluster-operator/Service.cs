﻿//------------------------------------------------------------------------------
// FILE:        Service.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.Kube;
using Neon.Kube.Operator;
using Neon.Net;
using Neon.Retry;
using Neon.Service;
using Neon.Tasks;

using DnsClient;

using DotnetKubernetesClient;

using k8s;
using k8s.Models;

using KubeOps.Operator;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Npgsql;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Instrumentation.Quartz;

using Prometheus;

using Quartz;
using Quartz.Impl;
using Quartz.Logging;
using Microsoft.AspNetCore;
using System.Web.Services.Description;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace NeonClusterOperator
{
    /// <summary>
    /// Implements the <b>neon-cluster-operator</b> service.
    /// </summary>
    /// <remarks>
    /// <para><b>ENVIRONMENT VARIABLES</b></para>
    /// <para>
    /// The <b>neon-node-agent</b> is configured using these environment variables:
    /// </para>
    /// <list type="table">
    /// <item>
    ///     <term><b>WATCHER_TIMEOUT_INTERVAL</b></term>
    ///     <description>
    ///     <b>timespan:</b> Specifies the maximum time the resource watcher will wait without
    ///     a response before creating a new request.  This defaults to <b>2 minutes</b>.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>WATCHER_MAX_RETRY_INTERVAL</b></term>
    ///     <description>
    ///     <b>timespan:</b> Specifies the maximum time the KubeOps resource watcher will wait
    ///     after a watch failure.  This defaults to <b>15 seconds</b>.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>NODETASK_IDLE_INTERVAL</b></term>
    ///     <description>
    ///     <b>timespan:</b> Specifies the interval at which IDLE events will be raised
    ///     for <b>NodeTask</b> giving the operator the chance to delete node tasks assigned
    ///     to nodes that don't exist.  This defaults to <b>60 seconds</b>.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>NODETASK_ERROR_MIN_REQUEUE_INTERVAL</b></term>
    ///     <description>
    ///     <b>timespan:</b> Specifies the minimum requeue interval to use when an
    ///     exception is thrown when handling NodeTask events.  This
    ///     value will be doubled when subsequent events also fail until the
    ///     requeue time maxes out at <b>CONTAINERREGISTRY_ERROR_MIN_REQUEUE_INTERVAL</b>.
    ///     This defaults to <b>5 seconds</b>.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>NODETASK_ERROR_MIN_REQUEUE_INTERVAL</b></term>
    ///     <description>
    ///     <b>timespan:</b> Specifies the maximum requeue time for NodeTask
    ///     handler exceptions.  This defaults to <b>60 seconds</b>.
    ///     </description>
    /// </item>
    /// </list>
    /// </remarks>
    public partial class Service : NeonService
    {
        /// <summary>
        /// Information about the cluster.
        /// </summary>
        public ClusterInfo ClusterInfo;

        public X509Certificate2 Certificate;

        public IKubernetes K8s;

        // private fields
        private IWebHost webHost;

        private string currentNamespace { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="serviceMap">Optionally specifies the service map.</param>
        public Service(string name, ServiceMap serviceMap = null)
            : base(name, version: KubeVersions.NeonKube, new NeonServiceOptions() { MetricsPrefix = "neonclusteroperator" })
        {
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected async override Task<int> OnRunAsync()
        {
            currentNamespace = await KubeHelper.GetCurrentNamespaceAsync();

            //-----------------------------------------------------------------
            // Start the controllers: these need to be started before starting KubeOps

            K8s = new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
            LogContext.SetCurrentLogProvider(TelemetryHub.LoggerFactory);

            //await GlauthController.StartAsync(K8s);
            //await NeonClusterOperatorController.StartAsync(K8s);
            //await NeonContainerRegistryController.StartAsync(K8s);
            //await NeonSsoClientController.StartAsync(K8s);
            //await NodeTaskController.StartAsync(K8s);

            _ = K8s.WatchAsync<V1ConfigMap>(async (@event) =>
            {
                await SyncContext.Clear;

                ClusterInfo = TypeSafeConfigMap<ClusterInfo>.From(@event.Value).Config;

                Logger.LogInformationEx("Updated cluster info");
            },
            KubeNamespace.NeonStatus,
            fieldSelector: $"metadata.name={KubeConfigMapName.ClusterInfo}");

            await CheckCertificateAsync();

            // Start the web service.
            var port = 443;

            if (NeonHelper.IsDevWorkstation)
            {
                port = 11005;
            }

            webHost = new WebHostBuilder()
                .ConfigureAppConfiguration(
                    (hostingcontext, config) =>
                    {
                        config.Sources.Clear();
                    })
                .UseStartup<OperatorStartup>()
                .UseKestrel(options => {
                    options.ConfigureEndpointDefaults(o =>
                    {
                        o.UseHttps(Certificate);
                    });
                    options.ConfigureHttpsDefaults(o =>
                    {
                        o.ServerCertificateSelector = (context, dnsName) =>
                        {
                            return Certificate;
                        };
                    });
                    options.Listen(IPAddress.Any, port);
                        
                })
                .ConfigureServices(services => services.AddSingleton(typeof(Service), this))
                .UseStaticWebAssets()
                .Build();

            _ = webHost.RunAsync();

#if DISABLED
            _ = Host.CreateDefaultBuilder()
                    .ConfigureHostOptions(
                        options =>
                        {
                            // Ensure that the processor terminator and ASP.NET shutdown times match.

                            options.ShutdownTimeout = ProcessTerminator.DefaultMinShutdownTime;
                        })
                    .ConfigureAppConfiguration(
                        (hostingContext, config) =>
                        {
                            // $note(jefflill): 
                            //
                            // The .NET runtime watches the entire file system for configuration
                            // changes which can cause real problems on Linux.  We're working around
                            // this by removing all configuration sources which we aren't using
                            // anyway for Kubernetes apps.
                            //
                            // https://github.com/nforgeio/neonKUBE/issues/1390

                            config.Sources.Clear();
                        })
                    .ConfigureLogging(
                        logging =>
                        {
                            logging.ClearProviders();
                            logging.AddProvider(base.TelemetryHub);
                        })
                    .ConfigureWebHostDefaults(builder => builder.UseStartup<Startup>())
                    .Build()
                    .RunOperatorAsync(Array.Empty<string>());
#endif

            // Indicate that the service is running.

            await StartedAsync();

            // Handle termination gracefully.
            await Terminator.StopEvent.WaitAsync();
            Terminator.ReadyToExit();

            return 0;
        }

        /// <inheritdoc/>
        protected override bool OnTracerConfig(TracerProviderBuilder builder)
        {
            builder.AddHttpClientInstrumentation(
                options =>
                {
                    options.Filter = (httpcontext) =>
                    {
                        return true;
                    };
                });
            builder.AddAspNetCoreInstrumentation();
            builder.AddGrpcCoreInstrumentation();
            builder.AddNpgsql();
            builder.AddQuartzInstrumentation();
            builder.AddOtlpExporter(
                options =>
                {
                    options.ExportProcessorType = ExportProcessorType.Batch;
                    options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>();
                    options.Endpoint = new Uri(NeonHelper.NeonKubeOtelCollectorUri);
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
            return true;
        }

        private async Task CheckCertificateAsync()
        {
            Logger.LogInformationEx(() => "Checking webhook certificate.");

            var cert = await K8s.ListNamespacedCustomObjectAsync<Certificate>(
                currentNamespace,
                labelSelector: $"{NeonLabel.ManagedBy}={Name}");

            if (!cert.Items.Any())
            {
                Logger.LogInformationEx(() => "Webhook certificate does not exist, creating...");

                var certificate = new Certificate()
                {
                    Metadata = new V1ObjectMeta()
                    {
                        Name = Name,
                        NamespaceProperty = currentNamespace,
                        Labels = new Dictionary<string, string>()
                    {
                        { NeonLabel.ManagedBy, Name }
                    }
                    },
                    Spec = new CertificateSpec()
                    {
                        DnsNames = new List<string>()
                    {
                        "neon-cluster-operator",
                        "neon-cluster-operator.neon-system",
                        "neon-cluster-operator.neon-system.svc",
                        "neon-cluster-operator.neon-system.svc.cluster.local",
                    },
                        Duration = "2160h0m0s",
                        IssuerRef = new IssuerRef()
                        {
                            Name = "neon-system-selfsigned-issuer",
                        },
                        SecretName = $"{Name}-webhook-tls"
                    }
                };

                await K8s.UpsertNamespacedCustomObjectAsync(certificate, certificate.Namespace(), certificate.Name());

                Logger.LogInformationEx(() => "Webhook certificate created.");
            }

            _ = K8s.WatchAsync<V1Secret>(
                async (@event) =>
                {
                    await SyncContext.Clear;

                    Certificate = X509Certificate2.CreateFromPem(
                        Encoding.UTF8.GetString(@event.Value.Data["tls.crt"]),
                        Encoding.UTF8.GetString(@event.Value.Data["tls.key"]));

                    Logger.LogInformationEx("Updated webhook certificate");
                },
                KubeNamespace.NeonSystem,
                fieldSelector: $"metadata.name={Name}-webhook-tls");

            await NeonHelper.WaitForAsync(
               async () =>
               {
                   return Certificate != null;
               },
               timeout: TimeSpan.FromSeconds(300),
               pollInterval: TimeSpan.FromMilliseconds(500));
        }

#if TODO
        private const string StateTable = "state";
        
        /// <summary>
        /// Responsible for making sure cluster container images are present in the local
        /// cluster registry.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task CheckNodeImagesAsync()
        {
            // check busybox doesn't already exist

            var pods = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem);

            if (pods.Items.Any(p => p.Metadata.Name == "check-node-images-busybox"))
            {
                Log.LogInformationEx(() => $"[check-node-images] Removing existing busybox pod.");
                
                await k8s.DeleteNamespacedPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem);

                await NeonHelper.WaitForAsync(
                    async () =>
                    {
                        pods = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem);

                        return !pods.Items.Any(p => p.Metadata.Name == "check-node-images-busybox");
                    }, 
                    timeout:      TimeSpan.FromSeconds(60),
                    pollInterval: TimeSpan.FromSeconds(2));
            }

            Log.LogInformationEx(() => $"[check-node-images] Creating busybox pod.");

            var busybox = await k8s.CreateNamespacedPodAsync(
                new V1Pod()
                {
                    Metadata = new V1ObjectMeta()
                    {
                        Name              = "check-node-images-busybox",
                        NamespaceProperty = KubeNamespaces.NeonSystem
                    },
                    Spec = new V1PodSpec()
                    {
                        Tolerations = new List<V1Toleration>()
                        {
                            { new V1Toleration() { Effect = "NoSchedule", OperatorProperty = "Exists" } },
                            { new V1Toleration() { Effect = "NoExecute", OperatorProperty = "Exists" } }
                        },
                        HostNetwork = true,
                        HostPID     = true,
                        HostIPC     = true,
                        Volumes     = new List<V1Volume>()
                        {
                            new V1Volume()
                            {
                                Name     = "noderoot",
                                HostPath = new V1HostPathVolumeSource()
                                {
                                    Path = "/",
                                }
                            }
                        },
                        Containers = new List<V1Container>()
                        {
                            new V1Container()
                            {
                                Name            = "check-node-images-busybox",
                                Image           = $"{KubeConst.LocalClusterRegistry}/busybox:{KubeVersions.Busybox}",
                                Command         = new List<string>() {"sleep", "infinity" },
                                ImagePullPolicy = "IfNotPresent",
                                SecurityContext = new V1SecurityContext()
                                {
                                    Privileged = true
                                },
                                VolumeMounts = new List<V1VolumeMount>()
                                {
                                    new V1VolumeMount()
                                    {
                                        Name      = "noderoot",
                                        MountPath = "/host"
                                    }
                                }
                            }
                        },
                        RestartPolicy      = "Always",
                        ServiceAccount     = KubeService.NeonClusterOperator,
                        ServiceAccountName = KubeService.NeonClusterOperator
                    }
                }, KubeNamespaces.NeonSystem);

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    pods = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem);

                    return pods.Items.Any(p => p.Metadata.Name == "check-node-images-busybox");
                },
                timeout:      TimeSpan.FromSeconds(60),
                pollInterval: TimeSpan.FromSeconds(2));

            Log.LogInformationEx(() => $"[check-node-images] Loading cluster manifest.");

            var clusterManifestJson = Program.Resources.GetFile("/cluster-manifest.json").ReadAllText();
            var clusterManifest     = NeonHelper.JsonDeserialize<ClusterManifest>(clusterManifestJson);

            Log.LogInformationEx(() => $"[check-node-images] Getting images currently on node.");

            var crioOutput = NeonHelper.JsonDeserialize<dynamic>(await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"crictl images --output json",  retry: true));
            var nodeImages = ((IEnumerable<dynamic>)crioOutput.images).Select(image => image.repoTags).SelectMany(x => (JArray)x);

            foreach (var image in clusterManifest.ContainerImages)
            {
                if (nodeImages.Contains(image.InternalRef))
                {
                    Log.LogInformationEx(() => $"[check-node-images] Image [{image.InternalRef}] exists. Pushing to registry.");
                    await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman push {image.InternalRef}", retry: true);
                } 
                else
                {
                    Log.LogInformationEx(() => $"[check-node-images] Image [{image.InternalRef}] doesn't exist. Pulling from [{image.SourceRef}].");
                    await ExecInPodAsync(() => "check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman pull {image.SourceRef}", retry: true);
                    await ExecInPodAsync(() => "check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman tag {image.SourceRef} {image.InternalRef}");

                    Log.LogInformationEx(() => $"[check-node-images] Pushing [{image.InternalRef}] to cluster registry.");
                    await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman push {image.InternalRef}", retry: true);
                }
            }

            Log.LogInformationEx(() => $"[check-node-images] Removing busybox.");
            await k8s.DeleteNamespacedPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem);

            Log.LogInformationEx(() => $"[check-node-images] Finished.");
        }

        /// <summary>
        /// Helper method for running node commands via a busybox container.
        /// </summary>
        /// <param name="podName"></param>
        /// <param name="namespace"></param>
        /// <param name="command"></param>
        /// <param name="containerName"></param>
        /// <param name="retry"></param>
        /// <returns>The command output as lines of text.</returns>
        public async Task<string> ExecInPodAsync(
            string      podName,
            string      @namespace,
            string      command,
            string      containerName = null,
            bool        retry         = false)
        {
            var podCommand = new string[]
            {
                "chroot",
                "/host",
                "/bin/bash",
                "-c",
                command
            };

            var pod = await k8s.ReadNamespacedPodAsync(podName, @namespace);

            if (string.IsNullOrEmpty(containerName))
            {
                containerName = pod.Spec.Containers.FirstOrDefault().Name;
            }

            string stdOut = "";
            string stdErr = "";

            var handler = new ExecAsyncCallback(async (_stdIn, _stdOut, _stdError) =>
            {
                stdOut = Encoding.UTF8.GetString(await _stdOut.ReadToEndAsync());
                stdErr = Encoding.UTF8.GetString(await _stdError.ReadToEndAsync());
            });

            var exitcode = await k8s.NamespacedPodExecAsync(podName, @namespace, containerName, podCommand, true, handler, CancellationToken.None);

            if (exitcode != 0)
            {
                throw new KubernetesException($@"{stdOut}

{stdErr}");
            }

            var result = new StringBuilder();
            foreach (var line in stdOut.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                result.AppendLine(line);
            }

            return result.ToString();
        }
#endif
    }
}