﻿//-----------------------------------------------------------------------------
// FILE:	    PowerShellException.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.IO;

namespace Neon.Windows
{
    /// <summary>
    /// Thrown by <see cref="PowerShell"/> when an error is detected.
    /// </summary>
    public class PowerShellException : Exception
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">Optionally specifies an inner exception.</param>
        public PowerShellException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}
