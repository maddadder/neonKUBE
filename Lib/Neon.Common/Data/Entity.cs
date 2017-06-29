﻿//-----------------------------------------------------------------------------
// FILE:	    Entity.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2017 by NeonForge, LLC.  All rights reserved.

using System;
using System.ComponentModel;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;

namespace Neon.Data
{
    /// <summary>
    /// Common base implementation of <see cref="IEntity{T}"/>
    /// </summary>
    /// <typeparam name="T">The entity content type.</typeparam>
    public class Entity<T> : IEntity<T>
        where T : class, new()
    {
        /// <inheritdoc/>
        public virtual string GetKey()
        {
            throw new NotSupportedException($"[{this.GetType().FullName}] does not implement [{nameof(GetKey)}].");
        }

        /// <inheritdoc/>
        [JsonProperty(PropertyName = "Type", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string Type { get; set; }

        /// <inheritdoc/>
        public virtual bool Equals(T other)
        {
            return NeonHelper.JsonEquals(this, other);
        }
    }
}
