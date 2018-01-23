﻿//-----------------------------------------------------------------------------
// FILE:	    SetupController.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;

namespace Neon.Cluster
{
    /// <summary>
    /// Manages a cluster setup operation consisting of a series of  setup operations
    /// steps, while displaying status to the <see cref="Console"/>.
    /// </summary>
    public class SetupController
    {
        //---------------------------------------------------------------------
        // Private types

        private enum StepStatus
        {
            None,
            Running,
            Done,
            Failed
        }

        private class Step
        {
            public string                                   Label;
            public bool                                     Quiet;
            public Action                                   GlobalAction;
            public Action<SshProxy<NodeDefinition>>         NodeAction;
            public Func<SshProxy<NodeDefinition>, bool>     Predicate;
            public StepStatus                               Status;
            public bool                                     NoParallelLimit;
        }

        //---------------------------------------------------------------------
        // Implementation

        private string                                      operationTitle;
        private string                                      operationStatus;
        private List<SshProxy<NodeDefinition>>              nodes;
        private List<Step>                                  steps;
        private Step                                        currentStep;
        private bool                                        error;
        private bool                                        hasNodeSteps;
        private StringBuilder                               sbDisplay;
        private string                                      lastDisplay;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="operationTitle">Summarizes the high-level operation being performed.</param>
        /// <param name="nodes">The node proxies for the cluster nodes being manipulated.</param>
        public SetupController(string operationTitle, IEnumerable<SshProxy<NodeDefinition>> nodes)
            : this(new string[] { operationTitle }, nodes)
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="operationTitle">Summarizes the high-level operation being performed.</param>
        /// <param name="nodes">The node proxies for the cluster nodes being manipulated.</param>
        public SetupController(string[] operationTitle, IEnumerable<SshProxy<NodeDefinition>> nodes)
        {
            var title = string.Empty;

            foreach (var name in operationTitle)
            {
                if (title.Length > 0)
                {
                    title += ' ';
                }

                title += name;
            }

            this.operationTitle  = title;
            this.operationStatus = string.Empty;
            this.nodes           = nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            this.steps           = new List<Step>();
            this.sbDisplay       = new StringBuilder();
            this.lastDisplay     = string.Empty;
        }

        /// <summary>
        /// Specifies whether the class should print setup status to the console.
        /// This defaults to <c>false</c>.
        /// </summary>
        public bool ShowStatus { get; set; } = false;

        /// <summary>
        /// Specifies whether that node status will be displayed.  This
        /// defaults to <c>true</c>.
        ///</summary>
        public bool ShowNodeStatus { get; set; } = true;

        /// <summary>
        /// The maximum number of nodes that will execute setup steps in parallel.  This
        /// defaults to essentially unconstrained.
        /// </summary>
        public int MaxParallel { get; set; } = int.MaxValue;

        /// <summary>
        /// Appends a configuration step.
        /// </summary>
        /// <param name="stepLabel">Brief step summary.</param>
        /// <param name="nodeAction">The action to be performed on each node.</param>
        /// <param name="nodePredicate">
        /// Optional predicate used to select the nodes that participate in the step
        /// or <c>null</c> to select all nodes.
        /// </param>
        /// <param name="quiet">Optionally specifies that the step is not to be reported in the progress.</param>
        public void AddStep(string stepLabel,
                            Action<SshProxy<NodeDefinition>> nodeAction,
                            Func<SshProxy<NodeDefinition>, bool> nodePredicate = null,
                            bool quiet = false)
        {
            nodeAction    = nodeAction ?? new Action<SshProxy<NodeDefinition>>(n => { });
            nodePredicate = nodePredicate ?? new Func<SshProxy<NodeDefinition>, bool>(n => true);

            steps.Add(
                new Step()
                {
                    Label      = stepLabel,
                    Quiet      = quiet,
                    NodeAction = nodeAction,
                    Predicate  = nodePredicate
                });
        }

        /// <summary>
        /// Appends a configuration step that will not be limited by <see cref="MaxParallel"/>.
        /// </summary>
        /// <param name="stepLabel">Brief step summary.</param>
        /// <param name="nodeAction">The action to be performed on each node.</param>
        /// <param name="nodePredicate">
        /// Optional predicate used to select the nodes that participate in the step
        /// or <c>null</c> to select all nodes.
        /// </param>
        /// <param name="quiet">Optionally specifies that the step is not to be reported in the progress.</param>
        public void AddStepNoParallelLimit(string stepLabel,
                                           Action<SshProxy<NodeDefinition>> nodeAction,
                                           Func<SshProxy<NodeDefinition>, bool> nodePredicate = null,
                                           bool quiet = false)
        {
            nodeAction    = nodeAction ?? new Action<SshProxy<NodeDefinition>>(n => { });
            nodePredicate = nodePredicate ?? new Func<SshProxy<NodeDefinition>, bool>(n => true);

            steps.Add(
                new Step()
                {
                    Label           = stepLabel,
                    Quiet           = quiet,
                    NodeAction      = nodeAction,
                    Predicate       = nodePredicate,
                    NoParallelLimit = true
                });
        }

        /// <summary>
        /// Adds a global cluster configuration step.
        /// </summary>
        /// <param name="stepLabel">Brief step summary.</param>
        /// <param name="action">The global action to be performed.</param>
        /// <param name="quiet">Optionally specifies that the step is not to be reported in the progress.</param>
        public void AddGlobalStep(string stepLabel, Action action, bool quiet = false)
        {
            steps.Add(
                new Step()
                {
                    Label        = stepLabel,
                    Quiet        = quiet,
                    GlobalAction = action,
                    Predicate    = n => true,
                });
        }

        /// <summary>
        /// Adds a step that waits for nodes to be online.
        /// </summary>
        /// <param name="stepLabel">Brief step summary.</param>
        /// <param name="status">The optional node status.</param>
        /// <param name="nodePredicate">
        /// Optional predicate used to select the nodes that participate in the step
        /// or <c>null</c> to select all nodes.
        /// </param>
        /// <param name="quiet">Optionally specifies that the step is not to be reported in the progress.</param>
        /// <param name="timeout">Optionally specifies the maximum time to wait (defaults to <b>10 minutes</b>).</param>
        public void AddWaitUntilOnlineStep(string stepLabel = "connect", string status = null, Func<SshProxy<NodeDefinition>, bool> nodePredicate = null, bool quiet = false, TimeSpan? timeout = null)
        {
            if (timeout == null)
            {
                timeout = TimeSpan.FromMinutes(10);
            }

            AddStepNoParallelLimit(stepLabel,
                n =>
                {
                    n.Status = status ?? "connecting";
                    n.WaitForBoot(timeout: timeout);
                    n.IsReady = true;
                },
                nodePredicate,
                quiet);
        }

        /// <summary>
        /// Adds a step that waits for a specified period of time.
        /// </summary>
        /// <param name="stepLabel">Brief step summary.</param>
        /// <param name="delay">The amount of time to wait.</param>
        /// <param name="status">The optional node status.</param>
        /// <param name="nodePredicate">
        /// Optional predicate used to select the nodes that participate in the step
        /// or <c>null</c> to select all nodes.
        /// </param>
        /// <param name="quiet">Optionally specifies that the step is not to be reported in the progress.</param>
        public void AddDelayStep(string stepLabel, TimeSpan delay, string status = null, Func<SshProxy<NodeDefinition>, bool> nodePredicate = null, bool quiet = false)
        {
            AddStepNoParallelLimit(stepLabel,
                n =>
                {
                    n.Status = status ?? $"delay: [{delay.TotalSeconds}] seconds";
                    Thread.Sleep(delay);
                    n.IsReady = true;
                },
                nodePredicate, quiet);
        }

        /// <summary>
        /// Performs the operation steps in the order they were added.
        /// </summary>
        /// <param name="leaveNodesConnected">Pass <c>true</c> leave the node proxies connected.</param>
        /// <returns><c>true</c> if all steps completed successfully.</returns>
        public bool Run(bool leaveNodesConnected = false)
        {
            hasNodeSteps = steps.Exists(s => s.NodeAction != null);     // We don't display node status if there aren't any node specific steps.

            try
            {
                foreach (var step in steps)
                {
                    currentStep = step;

                    try
                    {
                        if (!PerformStep(step))
                        {
                            break;
                        }
                    }
                    finally
                    {
                        currentStep = null;
                    }
                }

                if (error)
                {
                    return false;
                }

                foreach (var node in nodes)
                {
                    node.Status = "ready";
                }

                DisplayStatus();
                return true;
            }
            finally
            {
                if (!leaveNodesConnected)
                {
                    // Disconnect all of the nodes.

                    foreach (var node in nodes)
                    {
                        node.Disconnect();
                    }
                }
            }
        }

        /// <summary>
        /// Sets the optation status text.
        /// </summary>
        /// <param name="status">The optional operation status text to be displayed below the operation title.</param>
        public void SetOperationStatus(string status = null)
        {
            operationStatus = status ?? string.Empty;
        }

        /// <summary>
        /// Performs an operation step on the selected nodes.
        /// </summary>
        /// <param name="step">A step being performed.</param>
        /// <returns><c>true</c> if the step succeeded.</returns>
        /// <remarks>
        /// <para>
        /// This method begins by setting the <see cref="SshProxy{TMetadata}.IsReady"/>
        /// state of each selected node to <c>false</c> and then it starts a new thread for
        /// each node and performs the action on these servnodeer threads.
        /// </para>
        /// <para>
        /// In parallel, the method spins on the current thread, displaying status while
        /// waiting for each of the nodes to transition to the <see cref="SshProxy{TMetadata}.IsReady"/>=<c>true</c>
        /// state.
        /// </para>
        /// <para>
        /// The method returns <c>true</c> after all of the node actions have completed
        /// and none of the nodes have <see cref="SshProxy{TMetadata}.IsFaulted"/>=<c>true</c>.
        /// </para>
        /// <note>
        /// This method does nothing if a previous step failed.
        /// </note>
        /// </remarks>
        private bool PerformStep(Step step)
        {
            if (error)
            {
                return false;
            }

            step.Status = StepStatus.Running;

            var stepNodes        = nodes.Where(step.Predicate);
            var stepNodeNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in stepNodes)
            {
                stepNodeNamesSet.Add(node.Name);

                node.IsReady = false;
            }

            foreach (var node in nodes)
            {
                if (stepNodeNamesSet.Contains(node.Name))
                {
                    node.Status = string.Empty;
                }
                else
                {
                    node.Status = string.Empty;
                }
            }

            DisplayStatus(stepNodeNamesSet);

            var parallelOptions = new ParallelOptions()
            {
                MaxDegreeOfParallelism = step.NoParallelLimit ? 500 : MaxParallel
            };

            NeonHelper.ThreadRun(
                () =>
                {
                    if (step.NodeAction != null)
                    {
                        Parallel.ForEach(stepNodes, parallelOptions,
                            node =>
                            {
                                try
                                {
                                    step.NodeAction(node);

                                    node.Status  = "[step done]";
                                    node.IsReady = true;
                                }
                                catch (Exception e)
                                {
                                    node.Fault(NeonHelper.ExceptionError(e));
                                    node.LogException(e);
                                }
                            });
                    }
                    else if (step.GlobalAction != null)
                    {
                        try
                        {
                            step.GlobalAction();
                        }
                        catch (Exception e)
                        {
                            // $todo(jeff.lill):
                            //
                            // We're going to report global step exceptions as if they
                            // happened on the first manager node because there's no
                            // other place to log this in the current design.
                            //
                            // I suppose we could create a "global.log" file or something
                            // and put this there and also indicate this somewhere in
                            // the console output, but this is not worth messing with
                            // right now.

                            var firstManager = nodes
                                .Where(n => n.Metadata.IsManager)
                                .OrderBy(n => n.Name)
                                .First();

                            firstManager.Fault(NeonHelper.ExceptionError(e));
                            firstManager.LogException(e);
                        }

                        foreach (var node in stepNodes)
                        {
                            node.IsReady = true;
                        }
                    }
                });

            while (true)
            {
                DisplayStatus(stepNodeNamesSet);

                if (stepNodes.Count(n => !n.IsReady) == 0)
                {
                    DisplayStatus(stepNodeNamesSet);
                    break;
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

            error = stepNodes.FirstOrDefault(n => n.IsFaulted) != null;

            if (error)
            {
                step.Status = StepStatus.Failed;

                return false;
            }
            else
            {
                step.Status = StepStatus.Done;

                return true;
            }
        }

        /// <summary>
        /// Returns the current status for a node.
        /// </summary>
        /// <param name="stepNodeNamesSet">The set of node names participating in the current step.</param>
        /// <param name="node">The node being queried.</param>
        /// <returns>The status prefix.</returns>
        private string GetStatus(HashSet<string> stepNodeNamesSet, SshProxy<NodeDefinition> node)
        {
            if (stepNodeNamesSet != null && !stepNodeNamesSet.Contains(node.Name))
            {
                return "  -";
            }
            else
            {
                // We mark completed steps with a "* " prefix and indent
                // non-completed steps status with two blanks.

                if (node.Status.StartsWith("* "))
                {
                    return node.Status;
                }
                else
                {
                    return "  " + node.Status;
                }
            }
        }

        /// <summary>
        /// Formats a step index into a form suitable for display.
        /// </summary>
        /// <param name="stepNumber">The step index.</param>
        /// <returns>The formatted step number.</returns>
        private string FormatStepNumber(int stepNumber)
        {
            int     stepCount = steps.Count();
            string  number;

            if (stepCount < 10)
            {
                number = $"{stepNumber,1}";
            }
            else if (stepCount < 100)
            {
                number = $"{stepNumber,2}";
            }
            else
            {
                number = stepNumber.ToString();
            }

            return $"{number}. ";
        }

        /// <summary>
        /// Displays the current operation status on the <see cref="Console"/>.
        /// </summary>
        /// <param name="stepNodeNamesSet">
        /// The set of node names that participating in the current step or
        /// <c>null</c> if all nodes are included.
        /// </param>
        private void DisplayStatus(HashSet<string> stepNodeNamesSet = null)
        {
            if (!ShowStatus || steps.Count == 0)
            {
                return;
            }

            var underline         = " " + new string('-', 39);
            var maxStepLabelWidth = steps.Max(n => n.Label.Length);
            var maxNameWidth      = nodes.Max(n => n.Name.Length);
            var stepNumber        = 0;

            sbDisplay.Clear();

            sbDisplay.AppendLine();
            sbDisplay.AppendLine($" {operationTitle}");
            sbDisplay.AppendLine($" {new string('-', operationTitle.Length)}");
            sbDisplay.AppendLine($" {operationStatus}");
            sbDisplay.AppendLine();

            sbDisplay.AppendLine();
            sbDisplay.AppendLine(" Steps:");
            sbDisplay.AppendLine(underline);
            sbDisplay.AppendLine();

            foreach (var step in steps.Where(s => !s.Quiet))
            {
                stepNumber++;

                switch (step.Status)
                {
                    case StepStatus.None:

                        sbDisplay.AppendLine($"     {FormatStepNumber(stepNumber)}{step.Label}");
                        break;

                    case StepStatus.Running:

                        sbDisplay.AppendLine($" --> {FormatStepNumber(stepNumber)}{step.Label}");
                        break;

                    case StepStatus.Done:

                        sbDisplay.AppendLine($"     {FormatStepNumber(stepNumber)}{step.Label}{new string(' ', maxStepLabelWidth - step.Label.Length)}   [done]");
                        break;

                    case StepStatus.Failed:

                        sbDisplay.AppendLine($"     {FormatStepNumber(stepNumber)}{step.Label}{new string(' ', maxStepLabelWidth - step.Label.Length)}   [fail]"); ;
                        break;
                }
            }

            if (hasNodeSteps && ShowNodeStatus)
            {
                if (nodes.First().Metadata != null)
                {
                    if (nodes.Exists(n => n.Metadata.IsManager))
                    {
                        sbDisplay.AppendLine();
                        sbDisplay.AppendLine();
                        sbDisplay.AppendLine(" Managers:");
                        sbDisplay.AppendLine(underline);
                        sbDisplay.AppendLine();

                        foreach (var node in nodes.Where(n => n.Metadata.IsManager))
                        {
                            sbDisplay.AppendLine($"    {node.Name}{new string(' ', maxNameWidth - node.Name.Length)}   {GetStatus(stepNodeNamesSet, node)}");
                        }
                    }

                    if (nodes.Exists(n => n.Metadata.IsWorker))
                    {
                        sbDisplay.AppendLine();
                        sbDisplay.AppendLine();
                        sbDisplay.AppendLine(" Workers:");
                        sbDisplay.AppendLine(underline);
                        sbDisplay.AppendLine();

                        foreach (var node in nodes.Where(n => n.Metadata.IsWorker))
                        {
                            sbDisplay.AppendLine($"    {node.Name}{new string(' ', maxNameWidth - node.Name.Length)}   {GetStatus(stepNodeNamesSet, node)}");
                        }
                    }

                    if (nodes.Exists(n => n.Metadata.IsPet))
                    {
                        sbDisplay.AppendLine();
                        sbDisplay.AppendLine();
                        sbDisplay.AppendLine(" Pets:");
                        sbDisplay.AppendLine(underline);
                        sbDisplay.AppendLine();

                        foreach (var node in nodes.Where(n => n.Metadata.IsPet))
                        {
                            sbDisplay.AppendLine($"    {node.Name}{new string(' ', maxNameWidth - node.Name.Length)}   {GetStatus(stepNodeNamesSet, node)}");
                        }
                    }
                }
                else
                {
                    sbDisplay.AppendLine();
                    sbDisplay.AppendLine();
                    sbDisplay.AppendLine(" Nodes:");
                    sbDisplay.AppendLine(underline);
                    sbDisplay.AppendLine();

                    foreach (var node in nodes)
                    {
                        sbDisplay.AppendLine($"    {node.Name}{new string(' ', maxNameWidth - node.Name.Length)}   {GetStatus(stepNodeNamesSet, node)}");
                    }
                }
            }

            sbDisplay.AppendLine();
            sbDisplay.AppendLine();

            var newDisplay = sbDisplay.ToString();

            if (newDisplay != lastDisplay)
            {
                Console.Clear();
                Console.Write(newDisplay);

                lastDisplay = newDisplay;
            }
        }

        /// <summary>
        /// Throws an exception if any of the operation steps did not complete successfully.
        /// </summary>
        public void ThrowOnError()
        {
            if (error)
            {
                throw new NeonClusterException($"[{nodes.Count(n => n.IsFaulted)}] nodes are faulted.");
            }
        }
    }
}
