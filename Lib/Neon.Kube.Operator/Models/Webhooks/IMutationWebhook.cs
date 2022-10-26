﻿//-----------------------------------------------------------------------------
// FILE:	    IMutatingWebhook.cs
// CONTRIBUTOR: Marcus Bowyer
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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.JsonDiffPatch.Diffs.Formatters;
using System.Text.Json.JsonDiffPatch;
using System.Reflection;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using k8s;
using k8s.Autorest;
using k8s.KubeConfigModels;
using k8s.Models;

using Neon.Common;
using Neon.Diagnostics;

namespace Neon.Kube.Operator
{
    /// <summary>
    /// Represents a mutating webhook.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    public interface IMutatingWebhook<TEntity> : IAdmissionWebhook<TEntity, MutationResult>
        where TEntity : IKubernetesObject<V1ObjectMeta>, new()
    {
        /// <summary>
        /// Logger.
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// The namespace selector.
        /// </summary>
        public V1LabelSelector NamespaceSelector => null;

        /// <summary>
        /// The Object selector.
        /// </summary>
        public V1LabelSelector ObjectSelector => null;

        /// <summary>
        /// The webhook configuration.
        /// </summary>
        public V1MutatingWebhookConfiguration WebhookConfiguration
        { 
            get
            {
                var hook = this.GetType().GetCustomAttribute<WebhookAttribute>();

                var webhookConfig = new V1MutatingWebhookConfiguration()
                {
                    Metadata = new V1ObjectMeta()
                    {
                        Name = hook.Name,
                        Annotations = new Dictionary<string, string>()
                        {
                            { "cert-manager.io/inject-ca-from", hook.Certificate }
                        }
                    },
                    Webhooks = new List<V1MutatingWebhook>()
                    {
                        new V1MutatingWebhook()
                        {
                            Name = hook.Name,
                            Rules = new List<V1RuleWithOperations>(),
                            ClientConfig = new Admissionregistrationv1WebhookClientConfig()
                            {
                                Service = new Admissionregistrationv1ServiceReference()
                                {
                                    Name = hook.ServiceName,
                                    NamespaceProperty = hook.Namespace,
                                    Path = WebhookHelper.CreateEndpoint<TEntity>(this.GetType(), WebhookType.Mutate)
                                }
                            },
                            AdmissionReviewVersions = hook.AdmissionReviewVersions,
                            FailurePolicy = hook.FailurePolicy,
                            SideEffects = hook.SideEffects,
                            TimeoutSeconds = hook.TimeoutSeconds,
                            NamespaceSelector = NamespaceSelector,
                            MatchPolicy = hook.MatchPolicy,
                            ObjectSelector = ObjectSelector,
                            ReinvocationPolicy = hook.ReinvocationPolicy
                        }
                    }
                };

                var rules = this.GetType().GetCustomAttributes<WebhookRuleAttribute>();

                foreach (var rule in rules)
                {
                    webhookConfig.Webhooks.FirstOrDefault().Rules.Add(
                        new V1RuleWithOperations()
                        {
                            ApiGroups = rule.ApiGroups,
                            ApiVersions = rule.ApiVersions,
                            Operations = rule.Operations.ToList(),
                            Resources = rule.Resources,
                            Scope = rule.Scope
                        }
                    );
                }

                return webhookConfig;
            }
        }

        /// <inheritdoc />
        string IAdmissionWebhook<TEntity, MutationResult>.Endpoint
        {
            get => WebhookHelper.CreateEndpoint<TEntity>(this.GetType(), WebhookType);
        }

        /// <inheritdoc/>
        WebhookType IAdmissionWebhook<TEntity, MutationResult>.WebhookType
        {
            get => WebhookType.Mutate;
        }

        /// <inheritdoc />
        MutationResult IAdmissionWebhook<TEntity, MutationResult>.Create(TEntity newEntity, bool dryRun)
            => AdmissionResult.NotImplemented<MutationResult>();

        /// <inheritdoc />
        MutationResult IAdmissionWebhook<TEntity, MutationResult>.Update(
            TEntity oldEntity,
            TEntity newEntity,
            bool dryRun)
            => AdmissionResult.NotImplemented<MutationResult>();

        /// <inheritdoc />
        MutationResult IAdmissionWebhook<TEntity, MutationResult>.Delete(TEntity oldEntity, bool dryRun)
            => AdmissionResult.NotImplemented<MutationResult>();

        AdmissionResponse IAdmissionWebhook<TEntity, MutationResult>.TransformResult(
            MutationResult result,
            AdmissionRequest<TEntity> request)
        {
            var response = new AdmissionResponse
            {
                Allowed = result.Valid,
                Status = result.StatusMessage == null
                    ? null
                    : new AdmissionResponse.Reason { Code = result.StatusCode ?? 0, Message = result.StatusMessage, },
                Warnings = result.Warnings.ToArray(),
            };

            if (result.ModifiedObject != null)
            {
                response.PatchType = AdmissionResponse.JsonPatch;

                var node1 = JsonNode.Parse(KubernetesJson.Serialize(
                    request.Operation == "DELETE"
                        ? request.OldObject
                        : request.Object));

                var node2 = JsonNode.Parse(KubernetesJson.Serialize(result.ModifiedObject));

                var diff = node1.Diff(node2, new JsonPatchDeltaFormatter());

                response.Patch = Convert.ToBase64String(Encoding.UTF8.GetBytes(KubernetesJson.Serialize(diff)));
                response.PatchType = AdmissionResponse.JsonPatch;
            }

            return response;
        }

        internal async Task Create(IKubernetes k8s)
        {
            Logger?.LogInformationEx(() => $"Checking for webhook {this.GetType().Name}.");

            try
            {
                var webhook = await k8s.ReadMutatingWebhookConfigurationAsync(WebhookConfiguration.Name());
            }
            catch (HttpOperationException e) 
            {
                Logger?.LogInformationEx(() => $"Webhook {this.GetType().Name} not found, creating.");

                if (e.Response.StatusCode == System.Net.HttpStatusCode.NotFound) 
                {
                    await k8s.CreateMutatingWebhookConfigurationAsync(WebhookConfiguration);

                    Logger?.LogInformationEx(() => $"Webhook {this.GetType().Name} created.");
                }
                else 
                {
                    Logger?.LogErrorEx(e);

                    throw e;
                }
            }

            Logger?.LogInformationEx(() => $"Webhook {this.GetType().Name} already exists.");
        }
    }
}
