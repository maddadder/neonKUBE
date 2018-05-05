﻿//-----------------------------------------------------------------------------
// FILE:	    Test_DockerService.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Consul;

using Neon.Common;
using Neon.Cluster;
using Neon.Cryptography;
using Neon.Docker;
using Neon.Xunit;
using Neon.Xunit.Cluster;

using Xunit;

namespace TestNeonCluster
{
    /// <summary>
    /// Verifies that the <see cref="ServiceDetails"/> class maps correctly to
    /// the service inspection details returned for actual Docker services.
    /// </summary>
    public class Test_ServiceInspect : IClassFixture<DockerFixture>
    {
        // Enable strict JSON parsing by default so that unit tests will be
        // able to detect misnamed properties and also be able to discover 
        // new service properties added to the REST API by Docker.

        private const bool strict = true;

        private DockerFixture docker;

        public Test_ServiceInspect(DockerFixture docker)
        {
            this.docker = docker;

            // We're passing [login=null] below to connect to the cluster specified
            // by the NEON_TEST_CLUSTER environment variable.  This needs to be 
            // initialized with the login for a deployed cluster.

            if (this.docker.Initialize())
            {
                // Initialize the service with some secrets, configs, and networks
                // we can reference from services in our tests.

                docker.Reset();

                docker.CreateSecret("secret-1", "password1");
                docker.CreateSecret("secret-2", "password2");

                docker.CreateSecret("config-1", "config1");
                docker.CreateSecret("config-2", "config2");

                docker.CreateNetwork("network-1");
                docker.CreateNetwork("network-2");
            }
            else
            {
                docker.ClearServices();
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCluster)]
        public void Simple()
        {
            // Deploy a very simple service and then verify that the
            // service details were parsed correctly.

            docker.CreateService("test", "neoncluster/test");

            var info = docker.ListServices().Single(s => s.Name == "test");
            var details = docker.InspectService("test", strict);

            // ID, Version, and Time fields

            Assert.Equal(info.ID, details.ID.Substring(0, 12));     // ListServices returns the 12 character short ID
            Assert.True(details.Version.Index > 0);

            Assert.Equal(details.CreatedAtUtc, details.UpdatedAtUtc);

            var minTime = DateTime.UtcNow - TimeSpan.FromMinutes(10);
            var maxTime = DateTime.UtcNow;

            Assert.True(minTime <= details.CreatedAtUtc);
            Assert.True(details.CreatedAtUtc < maxTime);

            Assert.True(minTime <= details.UpdatedAtUtc);
            Assert.True(details.UpdatedAtUtc < maxTime);

            // Spec.TaskTemplate.ContainerSpec

            Assert.Equal("neoncluster/test:latest", details.Spec.TaskTemplate.ContainerSpec.ImageWithoutSHA);
            Assert.Equal(10000000000L, details.Spec.TaskTemplate.ContainerSpec.StopGracePeriod);
            Assert.Equal(ServiceIsolationMode.Default, details.Spec.TaskTemplate.ContainerSpec.Isolation);

            Assert.Empty(details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Test);
            Assert.Equal(0L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Interval);
            Assert.Equal(0L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Timeout);
            Assert.Equal(0L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Retries);
            Assert.Equal(0L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.StartPeriod);

            // Spec.TaskTemplate.Resources

            Assert.Equal(0, details.Spec.TaskTemplate.Resources.Limits.NanoCPUs);
            Assert.Equal(0, details.Spec.TaskTemplate.Resources.Limits.MemoryBytes);
            Assert.Empty(details.Spec.TaskTemplate.Resources.Limits.GenericResources);

            Assert.Equal(0, details.Spec.TaskTemplate.Resources.Reservations.NanoCPUs);
            Assert.Equal(0, details.Spec.TaskTemplate.Resources.Reservations.MemoryBytes);
            Assert.Empty(details.Spec.TaskTemplate.Resources.Reservations.GenericResources);

            // Spec.TaskTemplate.RestartPolicy

            Assert.Equal(ServiceRestartCondition.Any, details.Spec.TaskTemplate.RestartPolicy.Condition);
            Assert.Equal(5000000000L, details.Spec.TaskTemplate.RestartPolicy.Delay);
            Assert.Equal(0L, details.Spec.TaskTemplate.RestartPolicy.MaxAttempts);

            // Spec.TaskTemplate.Placement

            Assert.Single(details.Spec.TaskTemplate.Placement.Platforms);
            Assert.Equal("amd64", details.Spec.TaskTemplate.Placement.Platforms[0].Architecture);
            Assert.Equal("linux", details.Spec.TaskTemplate.Placement.Platforms[0].OS);

            // Spec.TaskTemplate (misc)

            Assert.Equal(0, details.Spec.TaskTemplate.ForceUpdate);
            Assert.Equal("container", details.Spec.TaskTemplate.Runtime);

            // Spec.Mode

            Assert.NotNull(details.Spec.Mode.Replicated);
            Assert.Equal(1, details.Spec.Mode.Replicated.Replicas);

            Assert.Null(details.Spec.Mode.Global);

            // Spec.UpdateConfig

            Assert.Equal(1, details.Spec.UpdateConfig.Parallelism);
            Assert.Equal(ServiceUpdateFailureAction.Pause, details.Spec.UpdateConfig.FailureAction);
            Assert.Equal(5000000000L, details.Spec.UpdateConfig.Monitor);
            Assert.Equal(0.0, details.Spec.UpdateConfig.MaxFailureRatio);
            Assert.Equal(ServiceUpdateOrder.StopFirst, details.Spec.UpdateConfig.Order);

            // Spec.RollbackConfig

            Assert.Equal(1, details.Spec.RollbackConfig.Parallelism);
            Assert.Equal(ServiceRollbackFailureAction.Pause, details.Spec.RollbackConfig.FailureAction);
            Assert.Equal(5000000000L, details.Spec.RollbackConfig.Monitor);
            Assert.Equal(0.0, details.Spec.RollbackConfig.MaxFailureRatio);
            Assert.Equal(ServiceRollbackOrder.StopFirst, details.Spec.RollbackConfig.Order);

            // Spec.EndpointSpec

            Assert.Equal(ServiceEndpointMode.Vip, details.Spec.EndpointSpec.Mode);
            Assert.Empty(details.Spec.EndpointSpec.Ports);

            // Endpoint

            Assert.Empty(details.Endpoint.Ports);
            Assert.Empty(details.Endpoint.VirtualIPs);

            // UpdateStatus

            Assert.Null(details.UpdateStatus);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCluster)]
        public void ServiceLabels()
        {
            // Verify that we can deploy and parse service labels.

            docker.CreateService("test", "neoncluster/test",
                dockerArgs: 
                    new string[]
                    {
                        "--label", "foo=bar",
                        "--label", "hello=world"
                    });

            var info    = docker.ListServices().Single(s => s.Name == "test");
            var details = docker.InspectService("test", strict);

            Assert.Equal(2, details.Spec.Labels.Count);
            Assert.Equal("bar", details.Spec.Labels["foo"]);
            Assert.Equal("world", details.Spec.Labels["hello"]);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCluster)]
        public void ServiceEnv()
        {
            // Verify that we can specify environment variables.

            docker.CreateService("test", "neoncluster/test",
                dockerArgs:
                    new string[]
                    {
                        "--env", "foo=bar",
                        "--env", "hello=world",
                        "--env", "MAIL"
                    });

            var info    = docker.ListServices().Single(s => s.Name == "test");
            var details = docker.InspectService("test", strict);

            Assert.Equal(3, details.Spec.TaskTemplate.ContainerSpec.Env.Count);
            Assert.Contains("foo=bar", details.Spec.TaskTemplate.ContainerSpec.Env);
            Assert.Contains("hello=world", details.Spec.TaskTemplate.ContainerSpec.Env);
            Assert.Contains("MAIL", details.Spec.TaskTemplate.ContainerSpec.Env);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCluster)]
        public void CommandAndArgs()
        {
            // Verify that we can specify the container command and arguments.

            docker.CreateService("test", "neoncluster/test",
                dockerArgs:
                    new string[]
                    {
                        "--entrypoint", "sleep"
                    },
                serviceArgs:
                    new string[]
                    {
                        "50000000"
                    });

            var info    = docker.ListServices().Single(s => s.Name == "test");
            var details = docker.InspectService("test", strict);

            Assert.Equal(new string[] { "sleep" }, details.Spec.TaskTemplate.ContainerSpec.Command);
            Assert.Equal(new string[] { "50000000" }, details.Spec.TaskTemplate.ContainerSpec.Args);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCluster)]
        public void ContainerSpecMisc()
        {
            // Verify that we can specify misc container properties.

            docker.CreateService("test", "neoncluster/test",
                dockerArgs:
                    new string[]
                    {
                        "--hostname", "sleeper",
                        "--workdir", "/",
                        "--user", "test",
                        "--group", "test",
                        "--tty",
                        "--read-only",
                        "--stop-signal", "kill",
                        "--stop-grace-period", "20000000ns",
                    });

            var info    = docker.ListServices().Single(s => s.Name == "test");
            var details = docker.InspectService("test", strict);

            Assert.Equal("sleeper", details.Spec.TaskTemplate.ContainerSpec.Hostname);
            Assert.Equal("/", details.Spec.TaskTemplate.ContainerSpec.Dir);
            Assert.Equal("test", details.Spec.TaskTemplate.ContainerSpec.User);
            Assert.Equal("test", details.Spec.TaskTemplate.ContainerSpec.Groups.Single());
            Assert.True(details.Spec.TaskTemplate.ContainerSpec.ReadOnly);
            Assert.True(details.Spec.TaskTemplate.ContainerSpec.ReadOnly);
            Assert.Equal("kill", details.Spec.TaskTemplate.ContainerSpec.StopSignal);
            Assert.Equal(20000000L, details.Spec.TaskTemplate.ContainerSpec.StopGracePeriod);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCluster)]
        public void ServiceHealthCheck()
        {
            // Verify that we can customize health checks.

            docker.CreateService("test", "neoncluster/test",
                dockerArgs:
                    new string[]
                    {
                        "--health-cmd", "exit 0",
                        "--health-interval", "5000000000000ns",
                        "--health-retries", "5",
                        "--health-start-period", "1000000000000ns",
                        "--health-timeout", "2000000000000ns",
                    });

            var info    = docker.ListServices().Single(s => s.Name == "test");
            var details = docker.InspectService("test", strict);

            Assert.Equal(new string[] { "echo", "ok" }, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Test);
            Assert.Equal(5000000000000L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Interval);
            Assert.Equal(5L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Retries);
            Assert.Equal(1000000000000L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.StartPeriod);
            Assert.Equal(2000000000000L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Timeout);
        }
    }
}
