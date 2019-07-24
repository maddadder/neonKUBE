﻿//-----------------------------------------------------------------------------
// FILE:	    ChildWorkflowOptions.cs
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

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;

namespace Neon.Cadence
{
    /// <summary>
    /// Specifies the options to use when executing a child workflow.
    /// </summary>
    public class ChildWorkflowOptions
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ChildWorkflowOptions()
        {
        }

        /// <summary>
        /// Optionally specifies the domain where the child workflow will run. 
        /// This defaults to the parent workflow's domain.
        /// </summary>
        public string Domain { get; set; } = null;

        /// <summary>
        /// Optionally specifies the workflow ID to assign to the child workflow.
        /// A UUID will be generated by default.
        /// </summary>
        public string WorkflowID { get; set; } = null;

        /// <summary>
        /// Optionally specifies the task list where the child workflow will be
        /// scheduled.  This defaults to the parent's task list.
        /// </summary>
        public string TaskList { get; set; } = null;

        /// <summary>
        /// Specifies the maximum time the child workflow may execute from start
        /// to finish.  This defaults to 24 hours.
        /// </summary>
        public TimeSpan ExecutionStartToCloseTimeout { get; set; } = CadenceClient.DefaultTimeout;

        /// <summary>
        /// Optionally specifies the decision task timeout for the child workflow.
        /// This defaults to <b>10 seconds</b>.
        /// </summary>
        public TimeSpan TaskStartToCloseTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Optionally specifies what happens to the child workflow when the parent is terminated.
        /// This defaults to <see cref="ChildPolicy.Abandon"/>.
        /// </summary>
        public ChildPolicy ChildPolicy { get; set; } = ChildPolicy.Abandon;

        /// <summary>
        /// Optionally specifies whether to wait for the child workflow to finish for any
        /// reason including being: completed, failed, timedout, terminated, or canceled.
        /// </summary>
        public bool WaitUntilFinished { get; set; } = false;

        /// <summary>
        /// Controls how Cadence handles workflows that attempt to reuse workflow IDs.
        /// This defaults to <see cref="WorkflowIdReusePolicy.AllowDuplicateFailedOnly"/>.
        /// </summary>
        public int WorkflowIdReusePolicy { get; set; } = (int)Cadence.WorkflowIdReusePolicy.AllowDuplicateFailedOnly;

        /// <summary>
        /// Optionally specifies retry options.
        /// </summary>
        public RetryOptions RetryPolicy { get; set; } = null;

        /// <summary>
        /// Optionally specifies a recurring schedule for the workflow.  This can be set to a string specifying
        /// the minute, hour, day of month, month, and day of week scheduling parameters using the standard Linux
        /// CRON format described here: <a href="https://en.wikipedia.org/wiki/Cron"/>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cadence accepts a CRON string formatted as a single line of text with 5 parameters separated by
        /// spaces.  The parameters specified the minute, hour, day of month, month, and day of week values:
        /// </para>
        /// <code>
        /// ┌───────────── minute (0 - 59)
        /// │ ┌───────────── hour (0 - 23)
        /// │ │ ┌───────────── day of the month (1 - 31)
        /// │ │ │ ┌───────────── month (1 - 12)
        /// │ │ │ │ ┌───────────── day of the week (0 - 6) (Sunday to Saturday)
        /// │ │ │ │ │
        /// │ │ │ │ │
        /// * * * * * 
        /// </code>
        /// <para>
        /// Each parameter may be set to one of:
        /// </para>
        /// <list type="table">
        /// <item>
        ///     <term><b>*</b></term>
        ///     <description>
        ///     Matches any value.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>value</b></term>
        ///     <description>
        ///     Matches a specific integer value.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>first-last</b></term>
        ///     <description>
        ///     Matches a range of integer values (inclusive).
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>value1,value2,...</b></term>
        ///     <description>
        ///     Matches a list of integer values.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>first/step</b></term>
        ///     <description>
        ///     Matches values starting at <b>first</b> and then succeeding incremented by <b>step</b>.
        ///     </description>
        /// </item>
        /// </list>
        /// <para>
        /// You can use this handy CRON calculator to see how this works: <a href="https://crontab.guru"/>
        /// </para>
        /// </remarks>
        public string CronSchedule { get; set; }

        /// <summary>
        /// Optionally specifies workflow metadata as a dictionary of named byte array values.
        /// </summary>
        public Dictionary<string, byte[]> Memo { get; set; }

        /// <summary>
        /// Converts this instance into the corresponding internal object.
        /// </summary>
        /// <returns>The equivalent <see cref="InternalChildWorkflowOptions"/>.</returns>
        internal InternalChildWorkflowOptions ToInternal()
        {
            return new InternalChildWorkflowOptions()
            {
                Domain                       = this.Domain,
                WorkflowID                   = this.WorkflowID,
                TaskList                     = this.TaskList,
                ExecutionStartToCloseTimeout = CadenceHelper.ToCadence(this.ExecutionStartToCloseTimeout),
                TaskStartToCloseTimeout      = CadenceHelper.ToCadence(this.TaskStartToCloseTimeout),
                ChildPolicy                  = (int)this.ChildPolicy,
                WaitForCancellation          = this.WaitUntilFinished,
                WorkflowIdReusePolicy        = (int)this.WorkflowIdReusePolicy,
                RetryPolicy                  = this.RetryPolicy?.ToInternal(),
                CronSchedule                 = this.CronSchedule
            };
        }
    }
}
