﻿//-----------------------------------------------------------------------------
// FILE:	    IFinalizerManager.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:	Copyright © 2005-2022 by NEONFORGE LLC.  All rights reserved.
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

using k8s.Models;
using k8s;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Neon.Kube.Operator
{
    /// <summary>
    /// Finalizer manager.
    /// </summary>
    /// <typeparam name="TEntity">The type of the k8s entity.</typeparam>
    public interface IFinalizerManager<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>, new()
    {
        /// <summary>
        /// Registers a specific <see cref="IResourceFinalizer{TEntity}"/> to an entity.
        /// </summary>
        /// <typeparam name="TFinalizer"></typeparam>
        /// <param name="entity"></param>
        /// <returns></returns>
        Task RegisterFinalizerAsync<TFinalizer>(TEntity entity)
            where TFinalizer : IResourceFinalizer<TEntity>;

        /// <summary>
        /// Registers all <see cref="IResourceFinalizer{TEntity}"/> to an entity.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        Task RegisterAllFinalizersAsync(TEntity entity);

        /// <summary>
        /// Removes a <see cref="IResourceFinalizer{TEntity}"/> from an entity.
        /// </summary>
        /// <typeparam name="TFinalizer"></typeparam>
        /// <param name="entity"></param>
        /// <returns></returns>
        Task RemoveFinalizerAsync<TFinalizer>(TEntity entity)
            where TFinalizer : IResourceFinalizer<TEntity>;

        internal Task FinalizeAsync(TEntity entity);
    }
}
