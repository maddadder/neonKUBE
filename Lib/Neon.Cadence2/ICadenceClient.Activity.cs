﻿//-----------------------------------------------------------------------------
// FILE:	    ICadenceClient.Activity.cs
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;

namespace Neon.Cadence
{
    public partial interface ICadenceClient
    {
        //---------------------------------------------------------------------
        // Cadence activity related operations.

        /// <summary>
        /// Registers an activity implementation with Cadence.
        /// </summary>
        /// <typeparam name="TActivity">The <see cref="ActivityBase"/> derived type implementing the activity.</typeparam>
        /// <param name="activityTypeName">
        /// Optionally specifies a custom activity type name that will be used 
        /// for identifying the activity implementation in Cadence.  This defaults
        /// to the fully qualified <typeparamref name="TActivity"/> type name.
        /// </param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if a different activity class has already been registered for <paramref name="activityTypeName"/>.</exception>
        /// <exception cref="CadenceActivityWorkerStartedException">
        /// Thrown if an activity worker has already been started for the client.  You must
        /// register activity implementations before starting workers.
        /// </exception>
        /// <remarks>
        /// <note>
        /// Be sure to register all of your activity implementations before starting a workflow worker.
        /// </note>
        /// </remarks>
        Task RegisterActivityAsync<TActivity>(string activityTypeName = null)
            where TActivity : ActivityBase;

        /// <summary>
        /// Scans the assembly passed looking for activity implementations derived from
        /// <see cref="ActivityBase"/> and tagged with <see cref="AutoRegisterAttribute"/>
        /// and registers them with Cadence.
        /// </summary>
        /// <param name="assembly">The target assembly.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="TypeLoadException">
        /// Thrown for types tagged by <see cref="AutoRegisterAttribute"/> that are not 
        /// derived from <see cref="WorkflowBase"/> or <see cref="ActivityBase"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown if one of the tagged classes conflict with an existing registration.</exception>
        /// <exception cref="CadenceActivityWorkerStartedException">
        /// Thrown if an activity worker has already been started for the client.  You must
        /// register activity implementations before starting workers.
        /// </exception>
        /// <remarks>
        /// <note>
        /// Be sure to register all of your activity implementations before starting a workflow worker.
        /// </note>
        /// </remarks>
        Task RegisterAssemblyActivitiesAsync(Assembly assembly);

        /// <summary>
        /// Used to send record activity heartbeat externally by task token.
        /// </summary>
        /// <param name="taskToken">The opaque activity task token.</param>
        /// <param name="details">Optional heartbeart details.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        Task RecordActivityHeartbeatAsync(byte[] taskToken, byte[] details = null);

        /// <summary>
        /// Used to send record activity heartbeat externally by activity ID.
        /// </summary>
        /// <param name="domain">The Cadence domain.</param>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="runId">The workflow run ID.</param>
        /// <param name="activityId">The activity ID.</param>
        /// <param name="details">Optional heartbeart details.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        Task RecordActivityHeartbeatByIdAsync(string domain, string workflowId, string runId, string activityId, byte[] details = null);

        /// <summary>
        /// Used to externally complete an activity identified by task token.
        /// </summary>
        /// <param name="taskToken">The opaque activity task token.</param>
        /// <param name="result">Passed as the activity result for activity success.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the activity no longer exists.</exception>
        Task RespondActivityCompletedAsync(byte[] taskToken, byte[] result = null);

        /// <summary>
        /// Used to externally complete an activity identified by activity ID.
        /// </summary>
        /// <param name="domain">The Cadence domain.</param>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="runId">The workflow run ID.</param>
        /// <param name="activityId">The activity ID.</param>
        /// <param name="result">Passed as the activity result for activity success.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the activity no longer exists.</exception>
        Task RespondActivityCompletedByIdAsync(string domain, string workflowId, string runId, string activityId, byte[] result = null);

        /// <summary>
        /// Used to externally cancel an activity identified by task token.
        /// </summary>
        /// <param name="taskToken">The opaque activity task token.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the activity no longer exists.</exception>
        Task RespondActivityCancelAsync(byte[] taskToken);

        /// <summary>
        /// Used to externally cancel an activity identified by activity ID.
        /// </summary>
        /// <param name="domain">The Cadence domain.</param>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="runId">The workflow run ID.</param>
        /// <param name="activityId">The activity ID.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the activity no longer exists.</exception>
        Task RespondActivityCancelByIdAsync(string domain, string workflowId, string runId, string activityId);

        /// <summary>
        /// Used to externally fail an activity by task token.
        /// </summary>
        /// <param name="taskToken">The opaque activity task token.</param>
        /// <param name="error">Specifies the activity error.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the activity no longer exists.</exception>
        Task RespondActivityFailAsync(byte[] taskToken, Exception error);

        /// <summary>
        /// Used to externally fail an activity by task token.
        /// </summary>
        /// <param name="domain">The Cadence domain.</param>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="runId">The workflow run ID.</param>
        /// <param name="activityId">The activity ID.</param>
        /// <param name="error">Specifies the activity error.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the activity no longer exists.</exception>
        Task RespondActivityFailByIdAsync(string domain, string workflowId, string runId, string activityId, Exception error);
    }
}
