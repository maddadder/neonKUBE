﻿//-----------------------------------------------------------------------------
// FILE:	    ICadenceClient.Workflow.cs
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
using Neon.Time;

namespace Neon.Cadence
{
    public partial interface ICadenceClient
    {
        //---------------------------------------------------------------------
        // Cadence workflow related operations.

        /// <summary>
        /// Registers a workflow implementation with Cadence.
        /// </summary>
        /// <typeparam name="TWorkflow">The <see cref="WorkflowBase"/> derived type implementing the workflow.</typeparam>
        /// <param name="workflowTypeName">
        /// Optionally specifies a custom workflow type name that will be used 
        /// for identifying the workflow implementation in Cadence.  This defaults
        /// to the fully qualified <typeparamref name="TWorkflow"/> type name.
        /// </param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if another workflow class has already been registered for <paramref name="workflowTypeName"/>.</exception>
        /// <exception cref="CadenceWorkflowWorkerStartedException">
        /// Thrown if a workflow worker has already been started for the client.  You must
        /// register activity workflow implementations before starting workers.
        /// </exception>
        /// <remarks>
        /// <note>
        /// Be sure to register all of your workflow implementations before starting a workflow worker.
        /// </note>
        /// </remarks>
        Task RegisterWorkflowAsync<TWorkflow>(string workflowTypeName = null)
            where TWorkflow : WorkflowBase;

        /// <summary>
        /// Scans the assembly passed looking for workflow implementations derived from
        /// <see cref="WorkflowBase"/> and tagged with <see cref="AutoRegisterAttribute"/>
        /// and registers them with Cadence.
        /// </summary>
        /// <param name="assembly">The target assembly.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="TypeLoadException">
        /// Thrown for types tagged by <see cref="AutoRegisterAttribute"/> that are not 
        /// derived from <see cref="WorkflowBase"/> or <see cref="ActivityBase"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown if one of the tagged classes conflict with an existing registration.</exception>
        /// <exception cref="CadenceWorkflowWorkerStartedException">
        /// Thrown if a workflow worker has already been started for the client.  You must
        /// register activity workflow implementations before starting workers.
        /// </exception>
        /// <remarks>
        /// <note>
        /// Be sure to register all of your workflow implementations before starting a workflow worker.
        /// </note>
        /// </remarks>
        Task RegisterAssemblyWorkflowsAsync(Assembly assembly);

        /// <summary>
        /// Starts an external workflow using the fully qualified type name for <typeparamref name="TWorkflow"/> 
        /// ast the workflow type name, returning a <see cref="WorkflowExecution"/> that can be used
        /// to track the workflow and also wait for its result via <see cref="GetWorkflowResultAsync(WorkflowExecution)"/>.
        /// </summary>
        /// <typeparam name="TWorkflow">Identifies the workflow to be exedcuted.</typeparam>
        /// <param name="args">Optionally specifies the workflow arguments encoded into a byte array.</param>
        /// <param name="taskList">Optionally specifies the target task list.  This defaults to <b>"default"</b>.</param>
        /// <param name="options">Optionally specifies the workflow options.</param>
        /// <returns>A <see cref="WorkflowExecution"/> identifying the new running workflow instance.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if there is no workflow registered for <typeparamref name="TWorkflow"/>.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is not valid.</exception>
        /// <exception cref="CadenceWorkflowRunningException">Thrown if a workflow with this ID is already running.</exception>
        /// <remarks>
        /// This method kicks off a new workflow instance and returns after Cadence has
        /// queued the operation but the method <b>does not</b> wait for the workflow to
        /// complete.
        /// </remarks>
        Task<WorkflowExecution> StartWorkflowAsync<TWorkflow>(byte[] args = null, string taskList = CadenceClient.DefaultTaskList, WorkflowOptions options = null)
            where TWorkflow : WorkflowBase;

        /// <summary>
        /// Starts an external workflow using a specific workflow type name, returning a <see cref="WorkflowExecution"/>
        /// that can be used to track the workflow and also wait for its result via <see cref="GetWorkflowResultAsync(WorkflowExecution)"/>.
        /// </summary>
        /// <param name="workflowTypeName">
        /// The type name used when registering the workers that will handle this workflow.
        /// This name will often be the fully qualified name of the workflow type but 
        /// this may have been customized when the workflow worker was registered.
        /// </param>
        /// <param name="args">Optionally specifies the workflow arguments encoded into a byte array.</param>
        /// <param name="taskList">Optionally specifies the target task list.  This defaults to <b>"default"</b>.</param>
        /// <param name="options">Specifies the workflow options.</param>
        /// <returns>A <see cref="WorkflowExecution"/> identifying the new running workflow instance.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if there is no workflow registered for <paramref name="workflowTypeName"/>.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is not valid.</exception>
        /// <exception cref="CadenceWorkflowRunningException">Thrown if a workflow with this ID is already running.</exception>
        /// <remarks>
        /// This method kicks off a new workflow instance and returns after Cadence has
        /// queued the operation but the method <b>does not</b> wait for the workflow to
        /// complete.
        /// </remarks>
        Task<WorkflowExecution> StartWorkflowAsync(string workflowTypeName, byte[] args = null, string taskList = CadenceClient.DefaultTaskList, WorkflowOptions options = null);

        /// <summary>
        /// Returns the current state of a running workflow.
        /// </summary>
        /// <param name="workflowExecution">Identifies the workflow run.</param>
        /// <returns>A <see cref="WorkflowDetails"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task<WorkflowDetails> GetWorkflowStateAsync(WorkflowExecution workflowExecution);

        /// <summary>
        /// Returns the result from a workflow execution, blocking until the workflow
        /// completes if it is still running.
        /// </summary>
        /// <param name="workflowExecution">Identifies the workflow execution.</param>
        /// <returns>The workflow result encoded as bytes or <c>null</c>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task<byte[]> GetWorkflowResultAsync(WorkflowExecution workflowExecution);

        /// <summary>
        /// <para>
        /// Cancels a workflow if it has not already finished.
        /// </para>
        /// <note>
        /// Workflow cancellation is typically considered to be a normal activity
        /// and not an error as opposed to workflow termination which will usually
        /// happen due to an error.
        /// </note>
        /// </summary>
        /// <param name="workflowExecution">Identifies the running workflow.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task RequestCancelWorkflowExecution(WorkflowExecution workflowExecution);

        /// <summary>
        /// <para>
        /// Cancels a workflow if it has not already finished.
        /// </para>
        /// <note>
        /// Workflow termination is typically considered to be due to an error as
        /// opposed to cancellation which is usually considered as a normal activity.
        /// </note>
        /// </summary>
        /// <param name="workflowExecution">Identifies the running workflow.</param>
        /// <param name="reason">Optionally specifies an error reason string.</param>
        /// <param name="details">Optionally specifies additional details as a byte array.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task TerminateWorkflowAsync(WorkflowExecution workflowExecution, string reason = null, byte[] details = null);

        /// <summary>
        /// Calls an external workflow using the fully qualified type name for <typeparamref name="TWorkflow"/> 
        /// and then waits for the workflow to complete, returning the workflow result.
        /// </summary>
        /// <typeparam name="TWorkflow">Identifies the workflow to be exedcuted.</typeparam>
        /// <param name="args">Optionally specifies the workflow arguments encoded into a byte array.</param>
        /// <param name="domain">Optionally specifies the Cadence domain where the workflow will run.  This defaults to the client domain.</param>
        /// <param name="taskList">Optionally specifies the target task list.  This defaults to <b>"default"</b>.</param>
        /// <param name="options">Optionally specifies the workflow options.</param>
        /// <returns>A <see cref="WorkflowExecution"/> identifying the new running workflow instance.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if there is no workflow registered for <typeparamref name="TWorkflow"/>.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is not valid.</exception>
        /// <exception cref="CadenceWorkflowRunningException">Thrown if a workflow with this ID is already running.</exception>
        /// <remarks>
        /// This method kicks off a new workflow instance and returns after Cadence has
        /// queued the operation but the method <b>does not</b> wait for the workflow to
        /// complete.
        /// </remarks>
        Task<byte[]> CallWorkflowAsync<TWorkflow>(byte[] args = null, string domain = null, string taskList = CadenceClient.DefaultTaskList, WorkflowOptions options = null)
            where TWorkflow : WorkflowBase;

        /// <summary>
        /// Calls an external workflow using a custom workflow type name and then waits for the woirkflow to complete, 
        /// returning the workflow result.
        /// </summary>
        /// <param name="workflowTypeName">
        /// The type name used when registering the workers that will handle this workflow.
        /// This name will often be the fully qualified name of the workflow type but 
        /// this may have been customized when the workflow worker was registered.
        /// </param>
        /// <param name="args">Optionally specifies the workflow arguments encoded into a byte array.</param>
        /// <param name="domain">Optionally specifies the Cadence domain where the workflow will run.  This defaults to the client domain.</param>
        /// <param name="taskList">Optionally specifies the target task list.  This defaults to <b>"default"</b>.</param>
        /// <param name="options">Specifies the workflow options.</param>
        /// <returns>A <see cref="WorkflowExecution"/> identifying the new running workflow instance.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if there is no workflow registered for <paramref name="workflowTypeName"/>.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is not valid.</exception>
        /// <exception cref="CadenceWorkflowRunningException">Thrown if a workflow with this ID is already running.</exception>
        /// <remarks>
        /// This method kicks off a new workflow instance and returns after Cadence has
        /// queued the operation but the method <b>does not</b> wait for the workflow to
        /// complete.
        /// </remarks>
        Task<byte[]> CallWorkflowAsync(string workflowTypeName, byte[] args = null, string domain = null, string taskList = CadenceClient.DefaultTaskList, WorkflowOptions options = null);

        /// <summary>
        /// Transmits a signal to a running workflow.
        /// </summary>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="signalName">Identifies the signal.</param>
        /// <param name="signalArgs">Optionally specifies signal arguments as a byte array.</param>
        /// <param name="runId">
        /// Optionally specifies the workflow's current run ID.  When <c>null</c> or empty
        /// Cadence will automatically signal the lastest workflow execution.
        /// </param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        /// <remarks>
        /// This is generally used to signal workflows written in another language or where you don't
        /// have access to the workflow interface definition.  This method takes a byte array as the signal
        /// arguments.  You'll be responsible for serializing the parameters into the format that matches what
        /// the workflow's signal method expects.
        /// </remarks>
        Task SignalWorkflowAsync(string workflowId, string signalName, byte[] signalArgs = null, string runId = null);

        /// <summary>
        /// Transmits a signal to a running workflow.  This method is relatively low-level because you'll need to 
        /// manually encode the signal arguments.
        /// </summary>
        /// <param name="workflowExecution">The <see cref="WorkflowExecution"/>.</param>
        /// <param name="signalName">Identifies the signal.</param>
        /// <param name="signalArgs">Optionally specifies signal arguments as a byte array.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        /// <remarks>
        /// This is generally used to signal workflows written in another language or where you don't
        /// have access to the workflow interface definition.  This method takes a byte array as the signal
        /// arguments.  You'll be responsible for serializing the parameters into the format that matches what
        /// the workflow's signal method expects.
        /// </remarks>
        Task SignalWorkflowAsync(WorkflowExecution workflowExecution, string signalName, byte[] signalArgs = null);

        /// <summary>
        /// Transmits a signal to a workflow, starting the workflow if it's not currently running.  This
        /// method is relatively low-level because you'll need to manually encode the signal and workflow
        /// arguments.
        /// </summary>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="signalName">Identifies the signal.</param>
        /// <param name="signalArgs">Optionally specifies signal arguments as a byte array.</param>
        /// <param name="workflowArgs">Optionally specifies the workflow arguments.</param>
        /// <param name="options">Optionally specifies the options to be used for starting the workflow when required.</param>
        /// <param name="taskList">Optionally specifies the task list.  This defaults to <b>"default"</b>.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the domain does not exist.</exception>
        /// <exception cref="CadenceBadRequestException">Thrown if the request is invalid.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task SignalWorkflowWithStartAsync(string workflowId, string signalName, byte[] signalArgs = null, byte[] workflowArgs = null, string taskList = CadenceClient.DefaultTaskList, WorkflowOptions options = null);

        /// <summary>
        /// Performs a low-level query on a workflow.  This method is relatively low-level because 
        /// you'll need to manually handle encoding for the query arguments and result.
        /// </summary>
        /// <param name="workflowId">The workflow ID.</param>
        /// <param name="queryName">Identifies the signal.</param>
        /// <param name="runId">
        /// Optionally specifies the workflow's current run ID.  When <c>null</c> or empty
        /// Cadence will automatically query the lastest workflow execution.
        /// </param>
        /// <param name="queryArgs">Optionally specifies query arguments encoded as a byte array.</param>
        /// <returns>The query result encoded as a byte array.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task<byte[]> QueryWorkflowAsync(string workflowId, string queryName, byte[] queryArgs = null, string runId = null);

        /// <summary>
        /// Queries a workflow.
        /// </summary>
        /// <param name="workflowExecution">The <see cref="WorkflowExecution"/>.</param>
        /// <param name="queryName">Identifies the signal.</param>
        /// <param name="queryArgs">Optionally specifies query arguments encoded as a byte array.</param>
        /// <returns>The query result encoded as a byte array.</returns>
        /// <exception cref="CadenceEntityNotExistsException">Thrown if the workflow no longer exists.</exception>
        /// <exception cref="CadenceInternalServiceException">Thrown for internal Cadence problems.</exception>
        Task<byte[]> QueryWorkflowAsync(WorkflowExecution workflowExecution, string queryName, byte[] queryArgs = null);

        /// <summary>
        /// <para>
        /// Sets the maximum number of bytes of history that will be retained
        /// for sticky workflows for workflow workers created by this client
        /// as a performance optimization.  When this is exceeded, Cadence will
        /// need to retrieve the entire workflow history from the Cadence cluster
        /// every time the workflow is assigned to a worker.
        /// </para>
        /// <para>
        /// This defaults to <b>10K</b> bytes.
        /// </para>
        /// </summary>
        /// <param name="maxCacheSize">The maximum number of bytes to cache for each sticky workflow.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        Task SetWorkflowCacheSizeAsync(int maxCacheSize);

        /// <summary>
        /// Returns the current maximum maximum number of bytes of history that 
        /// will be retained for sticky workflows for workflow workers created 
        /// by this client as a performance optimization.
        /// </summary>
        /// <returns>The maximum individual workflow cache size in bytes.</returns>
        Task<int> GetworkflowCacheSizeAsync();
    }
}
