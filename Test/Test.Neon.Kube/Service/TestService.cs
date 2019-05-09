﻿//-----------------------------------------------------------------------------
// FILE:	    TestService.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Neon.Common;
using Neon.IO;
using Neon.Kube;
using Neon.Kube.Service;
using Neon.Service;
using Neon.Xunit;
using Neon.Xunit.Kube;

using Xunit;

namespace TestKube
{
    /// <summary>
    /// Implements a simple web service used for testing <see cref="KubeService"/>
    /// and <see cref="KubeServiceFixture{TService}"/>.
    /// </summary>
    public class TestService : KubeService
    {
        //---------------------------------------------------------------------
        // Local types

        public class Startup
        {
            private TestService service;

            public Startup(IConfiguration configuration, TestService service)
            {
                this.Configuration = configuration;
                this.service       = service;
            }

            public IConfiguration Configuration { get; }

            public void ConfigureServices(IServiceCollection services)
            {
            }

            public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
            {
                // Forward all requests to the parent service to have them
                // handled there.

                app.Run(async context => await service.OnWebRequest(context));
            }
        }

        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Returns the service description.
        /// </summary>
        private static ServiceDescription GetServiceDescription()
        {
            var description = new ServiceDescription()
            {
                Name    = nameof(TestService),
                Address = IPAddress.Parse("127.0.0.10")
            };

            description.Endpoints.Add("default",
                new ServiceEndpoint()
                {
                    Name       = "default",
                    Protocol   = ServiceEndpointProtocol.Http,
                    PathPrefix = "/",
                    Port       = 666
                });

            return description;
        }

        //---------------------------------------------------------------------
        // Instance members

        private IWebHost    webHost;
        private Thread      thread;

        /// <summary>
        /// Constructor.
        /// </summary>
        public TestService()
            : base(GetServiceDescription(), ThisAssembly.Git.Branch, ThisAssembly.Git.Commit, ThisAssembly.Git.IsDirty)
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
            // Start the web service.

            var endpoint = Description.Endpoints["default"];

            webHost = new WebHostBuilder()
                .UseStartup<Startup>()
                .UseKestrel(options => options.Listen(Description.Address, endpoint.Port))
                .Build();

            webHost.Start();

            // Start the worker thread.

            thread = new Thread(new ThreadStart(ThreadFunc));
            thread.Start();

            // Start the worker task.  Note that this call won't return
            // until the task completes.

            Task.Run(() => TaskFunc()).Wait();

            // Wait for the worker thread to exit.

            thread.Join();

            // Return the exit code specified by the configuration.

            return await Task.FromResult(0);
        }

        /// <summary>
        /// Handles web requests.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task OnWebRequest(HttpContext context)
        {
            await context.Response.WriteAsync("Test");
        }

        /// <summary>
        /// Demonstrates how a service thread can be signalled to terminate.
        /// </summary>
        private void ThreadFunc()
        {
            // Terminating threads are a bit tricky.  The only acceptable way
            // to do this by fairly frequently polling a stop signal and then
            // exiting the thread.
            //
            // The [Thread.Abort()] exists on .NET CORE but it throws a 
            // [NotImplementedException].  This method does do something 
            // for .NET Framework, but most folks believe that using that
            // is a very bad idea anyway.
            //
            // So this means that you're going to have to poll [Terminator.TerminateNow]
            // frequently.  This is trivial in the example below, but for threads
            // performing complex long running activities, you may need to
            // sprinkle these checks across many of your methods.

            var shortDelay = TimeSpan.FromSeconds(1);

            while (!Terminator.TerminateNow)
            {
                Thread.Sleep(shortDelay);
            }
        }

        /// <summary>
        /// Demonstrates how a service task can be signalled to terminate.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private async Task TaskFunc()
        {
            while (true)
            {
                try
                {
                    // Note that we're sleeping here for 365 days!  This simulates
                    // service that's waiting (for a potentially long period of time)
                    // for something to do.

                    await Task.Delay(TimeSpan.FromDays(365), Terminator.CancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // This exception will be thrown when the terminator receives a
                    // signal to terminate the process because we passed the
                    // [Terminator.CancellationToken] to [Task.Async.Delay()].
                    // 
                    // The terminator calls [Cancel()] on it's cancellation token
                    // when the termination signal is received which causes any
                    // pending async operations that were passed the token to 
                    // abort and throw a [TaskCancelledException].
                    //
                    // This is a common .NET async programming pattern.
                    //
                    // We're going to use this exception as a signal to 
                    // exit the task.

                    return;
                }
            }
        }
    }
}
