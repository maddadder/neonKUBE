﻿//-----------------------------------------------------------------------------
// FILE:	    LinkMapper.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using Neon.Common;
using Neon.DynamicData;

namespace Neon.DynamicData.Internal
{
    /// <summary>
    /// <b>Platform use only:</b> Used by <see cref="IDynamicEntity"/> implementations to 
    /// map a property to a linked <see cref="IDynamicEntity"/> instance.
    /// </summary>
    /// <typeparam name="TEntity">The property value type.</typeparam>
    /// <remarks>
    /// <note>
    /// This class is intended for use only by classes generated by the 
    /// <b>entity-gen</b> build tool.
    /// </note>
    /// <para>
    /// This class is used to link a <see cref="JProperty"/> value to an external
    /// entity.  The property value will act as the entity link and the
    /// <see cref="IDynamicEntityContext"/> passed to the constructor (if any) will be
    /// used to dereference the link and load the entity.
    /// </para>
    /// <para>
    /// Linked entities are loaded on demand and cached when the <see cref="Value"/> 
    /// getter is called.  Subsequent calls to the getter will return the cached
    /// value.  The getter will return <c>null</c> if the link is null or if the
    /// referenced entity doesn't exist.
    /// </para>
    /// <note>
    /// This class will simply return <c>null</c> if no <see cref="IDynamicEntityContext"/> 
    /// is present.
    /// </note>
    /// </remarks>
    /// <threadsafety instance="false"/>
    public struct LinkMapper<TEntity> : IPropertyMapper
        where TEntity : class, IDynamicEntity, new()
    {
        private IDynamicEntity             parentEntity;
        private IDynamicEntityContext      context;
        private JProperty           property;
        private IDynamicEntity             entityValue;
        private Func<bool>          isDeletedFunc;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="parentEntity">The <see cref="IDynamicEntity"/> that owns this mapper.</param>
        /// <param name="jsonName">The JSON property name.</param>
        /// <param name="propertyName">The entity property name.</param>
        /// <param name="context">The <see cref="IDynamicEntityContext"/> or <c>null</c>.</param>
        public LinkMapper(IDynamicEntity parentEntity, string jsonName, string propertyName, IDynamicEntityContext context)
        {
            Covenant.Requires<ArgumentNullException>(parentEntity != null);
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(jsonName));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(propertyName));

            this.parentEntity  = parentEntity;
            this.context       = context;
            this.JsonName      = jsonName;
            this.PropertyName  = propertyName;
            this.property      = null;
            this.entityValue   = null;
            this.isDeletedFunc = null;
        }

        /// <inheritdoc/>
        public string JsonName { get; private set; }

        /// <inheritdoc/>
        public string PropertyName { get; private set; }

        /// <summary>
        /// Returns the entity link or <c>null</c>.
        /// </summary>
        public string Link
        {
            get
            {
                switch (property.Value.Type)
                {
                    case JTokenType.String:

                        // This is the preferred property value type.

                        return (string)property.Value.ToString();

                    case JTokenType.Bytes:
                    case JTokenType.Float:
                    case JTokenType.Guid:
                    case JTokenType.Integer:
                    case JTokenType.Uri:

                        // These will work too.

                        return property.Value.ToString();

                    default:

                        // The remaining types indicate null or don't really
                        // make sense, so we'll treat them as null.

                        return null;
                }
            }
        }

        /// <summary>
        /// Returns the link string for an entity or <c>null</c>.
        /// </summary>
        /// <param name="entity">The entity or <c>null</c>.</param>
        /// <returns>The entity link or <c>null</c>.</returns>
        /// <exception cref="ArgumentException">Thrown if the value being saved cannot be linked.</exception>
        /// <remarks>
        /// This method returns <c>null</c> when <paramref name="entity"/>=<c>null</c>, otherwise
        /// it returns the entity's link.  A non-<c>null</c> entity must be linkable.
        /// </remarks>
        private static string GetLink(TEntity entity)
        {
            if (entity == null)
            {
                return null;
            }

            var link = entity._GetLink();

            if (link == null)
            {
                throw new ArgumentException($"The [{nameof(TEntity)}] instance cannot be linked.  For Couchbase scenarios, be sure the entity is hosted within a document.");
            }

            return link;
        }

        /// <summary>
        /// The current property value.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the value being saved cannot be linked.</exception>
        public TEntity Value
        {
            get
            {
                if (context == null || Link == null)
                {
                    return null;
                }

                if (entityValue != null)
                {
                    // We have a cached entity.  Return it unless it 
                    // has been deleted.

                    if (!isDeletedFunc())
                    {
                        return (TEntity)entityValue;
                    }

                    // The entity has been deleted.  We'll clear the cache and
                    // then drop through to the code below on the off-chance that
                    // we'll be able to load it again.

                    entityValue   = null;
                    isDeletedFunc = null;
                }

                entityValue = context.LoadEntity<TEntity>(Link, out isDeletedFunc);

                return (TEntity)entityValue;
            }

            set
            {
                if (context == null)
                {
                    return;
                }

                if (value == null)
                {
                    property.Value = null;
                }
                else
                {
                    property.Value = GetLink(value);
                }

                // Purge any cached info.

                entityValue   = null;
                isDeletedFunc = null;
            }
        }

        /// <inheritdoc/>
        public bool Load(JProperty newProperty, bool reload = false)
        {
            Covenant.Requires<ArgumentNullException>(newProperty != null);

            var changed = !NeonHelper.JTokenEquals(property, newProperty);

            this.property      = newProperty;
            this.entityValue   = null;      // Purge any cached entity info
            this.isDeletedFunc = null;

            if (reload && changed)
            {
                parentEntity._OnPropertyChanged(PropertyName);
            }

            return changed;
        }
    }
}
