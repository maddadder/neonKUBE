﻿//-----------------------------------------------------------------------------
// FILE:	    NodeTaskController.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2022 by NEONFORGE LLC.  All rights reserved.
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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using JsonDiffPatch;

using Neon.Common;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Kube;
using Neon.Kube.Operator;
using Neon.Kube.Resources;
using Neon.Retry;
using Neon.Tasks;
using Neon.Time;

using k8s;
using k8s.Autorest;
using k8s.Models;

using Newtonsoft.Json;

using OpenTelemetry.Trace;

using Prometheus;

namespace NeonClusterOperator
{
    /// <summary>
    /// <para>
    /// Removes <see cref="V1NeonNodeTask"/> resources assigned to nodes that don't exist.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// This controller relies on a lease named <b>neon-cluster-operator.nodetask</b>.  
    /// This lease will be persisted in the <see cref="KubeNamespace.NeonSystem"/> namespace
    /// and will be used to a leader to manage these resources.
    /// </para>
    /// <para>
    /// The <b>neon-cluster-operator</b> won't conflict with node agents because we're only 
    /// removing tasks that don't belong to an existing node.
    /// </para>
    /// </remarks>
    public class NodeTaskController : IOperatorController<V1NeonNodeTask>
    {
        //---------------------------------------------------------------------
        // Static members

        private static readonly ILogger log = TelemetryHub.CreateLogger<NodeTaskController>();

        private static ResourceManager<V1NeonNodeTask, NodeTaskController>  resourceManager;

        /// <summary>
        /// Static constructor.
        /// </summary>
        static NodeTaskController()
        {
        }

        /// <summary>
        /// Starts the controller.
        /// </summary>
        /// <param name="k8s">The <see cref="IKubernetes"/> client to use.</param>
        /// <param name="serviceProvider">The <see cref="IServiceProvider"/>.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task StartAsync(
            IKubernetes k8s,
            IServiceProvider serviceProvider)
        {
            Covenant.Requires<ArgumentNullException>(k8s != null, nameof(k8s));

            // Load the configuration settings.

            var leaderConfig = 
                new LeaderElectionConfig(
                    k8s,
                    @namespace: KubeNamespace.NeonSystem,
                    leaseName:        $"{Program.Service.Name}.nodetask",
                    identity:         Pod.Name,
                    promotionCounter: Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_promoted", "Leader promotions"),
                    demotionCounter:  Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_demoted", "Leader demotions"),
                    newLeaderCounter: Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_new_leader", "Leadership changes"));

            var options = new ResourceManagerOptions()
            {
                IdleInterval             = Program.Service.Environment.Get("NODETASK_IDLE_INTERVAL", TimeSpan.FromSeconds(1)),
                ErrorMinRequeueInterval  = Program.Service.Environment.Get("NODETASK_ERROR_MIN_REQUEUE_INTERVAL", TimeSpan.FromSeconds(15)),
                ErrorMaxRequeueInterval  = Program.Service.Environment.Get("NODETASK_ERROR_MAX_REQUEUE_INTERVAL", TimeSpan.FromSeconds(60)),
                IdleCounter              = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_idle", "IDLE events processed."),
                ReconcileCounter         = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_idle", "RECONCILE events processed."),
                DeleteCounter            = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_idle", "DELETED events processed."),
                StatusModifyCounter      = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_idle", "STATUS-MODIFY events processed."),
                FinalizeCounter          = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_finalize", "FINALIZE events processed."),
                IdleErrorCounter         = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_idle_error", "Failed IDLE event processing."),
                ReconcileErrorCounter    = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_reconcile_error", "Failed RECONCILE event processing."),
                DeleteErrorCounter       = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_delete_error", "Failed DELETE event processing."),
                StatusModifyErrorCounter = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_statusmodify_error", "Failed STATUS-MODIFY events processing."),
                FinalizeErrorCounter     = Metrics.CreateCounter($"{Program.Service.MetricsPrefix}nodetask_finalize_error", "Failed FINALIZE events processing.")
            };

            resourceManager = new ResourceManager<V1NeonNodeTask, NodeTaskController>(
                k8s,
                options:      options,
                leaderConfig: leaderConfig,
                serviceProvider: serviceProvider);

            await resourceManager.StartAsync();
        }

        //---------------------------------------------------------------------
        // Instance members

        private readonly IKubernetes k8s;

        /// <summary>
        /// Constructor.
        /// </summary>
        public NodeTaskController(IKubernetes k8s)
        {
            Covenant.Requires(k8s != null, nameof(k8s));

            this.k8s = k8s;
        }

        /// <summary>
        /// Called periodically to allow the operator to perform global events.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task IdleAsync()
        {
            await SyncContext.Clear;

            log.LogInformationEx("[IDLE]");

            // We're going to handle this by looking at each node task and checking
            // to see whether the target node actually exists.  Rather than listing
            // the node first, which would be expensive for a large cluster we'll
            // fetch and cache node information as we go along.

            var nodeNameToExists = new Dictionary<string, bool>(StringComparer.InvariantCultureIgnoreCase);
            var resources        = (await k8s.ListClusterCustomObjectAsync<V1NeonNodeTask>()).Items;

            foreach (var nodeTask in resources)
            {
                var deleteMessage = $"Deleting node task [{nodeTask.Name()}] because it is assigned to the non-existent cluster node [{nodeTask.Spec.Node}].";

                if (nodeNameToExists.TryGetValue(nodeTask.Spec.Node, out var nodeExists))
                {
                    // Target node status is known.

                    if (nodeExists)
                    {
                        continue;
                    }
                    else
                    {
                        log.LogInformationEx(deleteMessage);

                        try
                        {
                            await k8s.DeleteClusterCustomObjectAsync(nodeTask);
                        }
                        catch (Exception e)
                        {
                            log.LogErrorEx(e);
                        }

                        continue;
                    }
                }

                // Determine whether the node exists.

                try
                {
                    var node = await k8s.ReadNodeAsync(nodeTask.Spec.Node);

                    nodeExists = true;
                    nodeNameToExists.Add(nodeTask.Spec.Node, nodeExists);
                }
                catch (HttpOperationException e)
                {
                    if (e.Response.StatusCode == HttpStatusCode.NotFound)
                    {
                        nodeExists = false;
                        nodeNameToExists.Add(nodeTask.Spec.Node, nodeExists);
                    }
                    else
                    {
                        log.LogErrorEx(e);
                        continue;
                    }
                }
                catch (Exception e)
                {
                    log.LogErrorEx(e);
                    continue;
                }

                if (!nodeExists)
                {
                    log.LogInformationEx(deleteMessage);

                    try
                    {
                        await k8s.DeleteClusterCustomObjectAsync(nodeTask);
                    }
                    catch (Exception e)
                    {
                        log.LogErrorEx(e);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task StatusModifiedAsync(V1NeonNodeTask resource)
        {
            await SyncContext.Clear;

            using (var activity = TelemetryHub.ActivitySource.StartActivity())
            {
                Tracer.CurrentSpan?.AddEvent("status-modified", attributes => attributes.Add("customresource", nameof(V1NeonNodeTask)));

                if (resource.Metadata.Labels.TryGetValue(NeonLabel.NodeTaskType, out var nodeTaskType))
                {
                    switch (nodeTaskType) 
                    {
                        case NeonNodeTaskType.ControlPlaneCertExpirationCheck:

                            if (resource.Status.Phase == V1NeonNodeTask.Phase.Success)
                            {
                                await ProcessControlPlaneCertCheckAsync(resource);
                            }
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        private async Task ProcessControlPlaneCertCheckAsync(V1NeonNodeTask resource)
        {
            await SyncContext.Clear;

            using (var activity = TelemetryHub.ActivitySource.StartActivity())
            {
                Dictionary<string, int> expirations = new Dictionary<string, int>();

                var reading = false;
                foreach (var line in resource.Status.Output.ToLines())
                {
                    if (reading && line.StartsWith("CERTIFICATE AUTHORITY") || line.IsEmpty())
                    {
                        reading = false;
                        continue;
                    }

                    if (line.StartsWith("CERTIFICATE"))
                    {
                        reading = true;
                        continue;
                    }

                    if (!reading)
                    {
                        continue;
                    }

                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    expirations.Add(parts[0], int.Parse(parts[6].TrimEnd('d')));
                }

                if (expirations.Any(exp => exp.Value < 90))
                {
                    using (var nodeTaskActivity = TelemetryHub.ActivitySource.StartActivity("CreateUpdateCertNodeTask"))
                    {
                        var nodeTask = new V1NeonNodeTask()
                        {
                            Metadata = new V1ObjectMeta()
                            {
                                Name = $"control-plane-cert-update-{NeonHelper.CreateBase36Uuid()}",
                                Labels = new Dictionary<string, string>
                            {
                                { NeonLabel.ManagedBy, KubeService.NeonClusterOperator },
                                { NeonLabel.NodeTaskType, NeonNodeTaskType.ControlPlaneCertUpdate }
                            }
                            },
                            Spec = new V1NeonNodeTask.TaskSpec()
                            {
                                Node = resource.Spec.Node,
                                StartAfterTimestamp = DateTime.UtcNow,
                                BashScript = @"
set -e

/usr/bin/kubeadm certs renew all

for pod in `crictl pods | tr -s ' ' | cut -d "" "" -f 1-6 | grep kube | cut -d "" "" -f1`;
do 
    crictl stopp $pod;
    crictl rmp $pod;
done
",
                                CaptureOutput = true,
                                RetentionSeconds = TimeSpan.FromDays(1).Seconds
                            }
                        };

                        var tasks = await k8s.ListClusterCustomObjectAsync<V1NeonNodeTask>(labelSelector: $"{NeonLabel.NodeTaskType}={NeonNodeTaskType.ControlPlaneCertUpdate}");

                        if (!tasks.Items.Any(
                                        task => task.Spec.Node == nodeTask.Spec.Node
                                                && (task.Status.Phase <= V1NeonNodeTask.Phase.Running
                                                    || task.Status == null)))
                        {
                            await k8s.CreateClusterCustomObjectAsync<V1NeonNodeTask>(nodeTask, name: nodeTask.Name());
                        }
                    }
                }
            }
        }
    }
}
