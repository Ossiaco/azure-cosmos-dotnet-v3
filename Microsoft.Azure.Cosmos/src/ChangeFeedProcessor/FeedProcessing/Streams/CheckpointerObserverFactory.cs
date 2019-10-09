﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.ChangeFeed.FeedProcessing.Streams
{
    using System;
    using Microsoft.Azure.Cosmos.ChangeFeed.Configuration;

    /// <summary>
    /// Factory class used to create instance(s) of <see cref="ChangeFeedObserver{T}"/>.
    /// </summary>
    internal sealed class CheckpointerObserverFactory : ChangeFeedObserverFactory
    {
        private readonly ChangeFeedObserverFactory observerFactory;
        private readonly CheckpointFrequency checkpointFrequency;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckpointerObserverFactory{T}"/> class.
        /// </summary>
        /// <param name="observerFactory">Instance of Observer Factory</param>
        /// <param name="checkpointFrequency">Defined <see cref="CheckpointFrequency"/></param>
        public CheckpointerObserverFactory(ChangeFeedObserverFactory observerFactory, CheckpointFrequency checkpointFrequency)
        {
            if (observerFactory == null)
                throw new ArgumentNullException(nameof(observerFactory));
            if (checkpointFrequency == null)
                throw new ArgumentNullException(nameof(checkpointFrequency));

            this.observerFactory = observerFactory;
            this.checkpointFrequency = checkpointFrequency;
        }

        /// <summary>
        /// Creates a new instance of <see cref="ChangeFeedObserver{T}"/>.
        /// </summary>
        /// <returns>Created instance of <see cref="ChangeFeedObserver{T}"/>.</returns>
        public override ChangeFeedObserver CreateObserver()
        {
            ChangeFeedObserver observer = new ObserverExceptionWrappingChangeFeedObserverDecorator(this.observerFactory.CreateObserver());
            if (this.checkpointFrequency.ExplicitCheckpoint) return observer;

            return new AutoCheckpointer(this.checkpointFrequency, observer);
        }
    }
}