//-----------------------------------------------------------------------------
// FILE:	    Service.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:   Copyright � 2005-2022 by NEONFORGE LLC.  All rights reserved.
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
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.Service;
using Neon.Kube;

using k8s;

using Prometheus;
using Prometheus.DotNetRuntime;
using k8s.Models;

namespace NeonAcme
{
    /// <summary>
    /// Implements the <b>neon-acme</b> service.
    /// </summary>
    public class Service : NeonService
    {
        /// <summary>
        /// The Default <see cref="NeonService"/> name.
        /// </summary>
        public const string ServiceName = "neon-acme";

        /// <summary>
        /// The Kubernetes client.
        /// </summary>
        public KubernetesWithRetry Kubernetes;

        /// <summary>
        /// Resources used for discovery.
        /// </summary>
        public V1APIResourceList Resources;

        // private fields
        private IWebHost webHost;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The service name.</param>
        public Service(string name)
             : base(name, version: KubeVersions.NeonKube, new NeonServiceOptions() { MetricsPrefix = "neonacme" })
        {
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Dispose web host if it's still running.

            if (webHost != null)
            {
                webHost.Dispose();
                webHost = null;
            }
        }

        /// <inheritdoc/>
        protected async override Task<int> OnRunAsync()
        {
            await SetStatusAsync(NeonServiceStatus.Starting);

            Kubernetes = new KubernetesWithRetry(KubernetesClientConfiguration.BuildDefaultConfig());

            Resources = new V1APIResourceList()
            {
                ApiVersion = "v1",
                GroupVersion = "acme.neoncloud.io/v1alpha1",
                Resources = new List<V1APIResource>()
                {
                    new V1APIResource()
                    {
                        Name = "neoncluster_io",
                        SingularName = "neoncluster_io",
                        Namespaced = false,
                        Group = "webhook.acme.cert-manager.io",
                        Version = "v1alpha1",
                        Kind = "ChallengePayload",
                        Verbs = new List<string>(){ "create"}
                    }
                }
            };

            // Start the web service.
            var port = 443;

            if (NeonHelper.IsDevWorkstation)
            {
                port = 11004;
            }

            webHost = new WebHostBuilder()
                .ConfigureAppConfiguration(
                    (hostingcontext, config) =>
                    {
                        config.Sources.Clear();
                    })
                .UseStartup<Startup>()
                .UseKestrel(options => {
                    options.Listen(IPAddress.Any, port, listenOptions =>
                    {
                        if (!NeonHelper.IsDevWorkstation)
                        {
                            listenOptions.UseHttps(X509Certificate2.CreateFromPem(
                                                        File.ReadAllText(@"/tls/tls.crt"),
                                                        File.ReadAllText(@"/tls/tls.key")));
                        }
                    });
                })
                .ConfigureServices(services => services.AddSingleton(typeof(Service), this))
                .UseStaticWebAssets()
                .Build();

            _ = webHost.RunAsync();

            Logger.LogInformationEx(() => $"Listening on {IPAddress.Any}:{port}");

            // Indicate that the service is ready for business.

            await SetStatusAsync(NeonServiceStatus.Running);

            // Handle termination gracefully.

            await Terminator.StopEvent.WaitAsync();
            Terminator.ReadyToExit();

            return 0;
        }
    }
}