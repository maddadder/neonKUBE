﻿//-----------------------------------------------------------------------------
// FILE:	    Test_HiveTrafficManager.Private.Tcp.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Cryptography;
using Neon.Kube;
using Neon.Xunit;
using Neon.Xunit.Hive;

using Xunit;

namespace TestHive
{
    public partial class Test_HiveTrafficManager : IClassFixture<HiveFixture>
    {
        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonKube)]
        public async Task Tcp_Private()
        {
            await TestHttpRule("tcp-private", HiveHostPorts.ProxyPrivateLastUser, HiveConst.PrivateNetwork, hive.PrivateTraffic);
        }
    }
}
