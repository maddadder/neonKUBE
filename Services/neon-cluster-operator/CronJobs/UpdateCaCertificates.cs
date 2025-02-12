﻿//-----------------------------------------------------------------------------
// FILE:	    UpdateCaCertificates.cs
// CONTRIBUTOR: Marcus Bowyer
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
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Diagnostics;
using Neon.Kube;
using Neon.Kube.Resources;

using k8s;
using k8s.Models;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Prometheus;

using Quartz;

namespace NeonClusterOperator
{
    /// <summary>
    /// Handles updating of Linux CA certificates on cluster nodes.
    /// </summary>
    public class UpdateCaCertificates : CronJob, IJob
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public UpdateCaCertificates()
            : base(typeof(UpdateCaCertificates))
        {
        }
        
        /// <inheritdoc/>
        public async Task Execute(IJobExecutionContext context)
        {
            Covenant.Requires<ArgumentNullException>(context != null, nameof(context));

            using (var activity = TelemetryHub.ActivitySource.StartActivity())
            {
                Tracer.CurrentSpan?.AddEvent("execute", attributes => attributes.Add("cronjob", nameof(UpdateCaCertificates)));

                var dataMap   = context.MergedJobDataMap;
                var k8s       = (IKubernetes)dataMap["Kubernetes"];
                var nodes     = await k8s.ListNodeAsync();
                var startTime = DateTime.UtcNow.AddSeconds(10);

                foreach (var node in nodes.Items)
                {
                    var nodeTask = new V1NeonNodeTask()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name   = $"ca-certificate-update-{NeonHelper.CreateBase36Uuid()}",
                            Labels = new Dictionary<string, string>
                            {
                                { NeonLabel.ManagedBy, KubeService.NeonClusterOperator },
                                { NeonLabel.NodeTaskType, NeonNodeTaskType.NodeCaCertUpdate }
                            }
                        },
                        Spec = new V1NeonNodeTask.TaskSpec()
                        {
                            Node                = node.Name(),
                            StartAfterTimestamp = startTime,
                            BashScript          = @"/usr/sbin/update-ca-certificates",
                            RetentionSeconds    = (int)TimeSpan.FromHours(1).TotalSeconds
                        }
                    };

                    var tasks = await k8s.ListClusterCustomObjectAsync<V1NeonNodeTask>(labelSelector: $"{NeonLabel.NodeTaskType}={NeonNodeTaskType.NodeCaCertUpdate}");

                    if (!tasks.Items.Any(task => task.Spec.Node == nodeTask.Spec.Node && (task.Status.Phase <= V1NeonNodeTask.Phase.Running || task.Status == null)))
                    {
                        await k8s.CreateClusterCustomObjectAsync<V1NeonNodeTask>(nodeTask, name: nodeTask.Name());
                    }

                    startTime = startTime.AddMinutes(10);
                }
            }
        }
    }
}
