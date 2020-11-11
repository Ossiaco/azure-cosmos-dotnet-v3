﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests.FeedRange
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Pagination;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Tests.Pagination;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class ReadFeedIteratorCoreTests
    {
        [TestMethod]
        public void ReadFeedIteratorCore_HasMoreResultsDefault()
        {
            FeedIterator iterator = CreateReadFeedIterator(
                Mock.Of<IDocumentContainer>(),
                default,
                default);
            Assert.IsTrue(iterator.HasMoreResults);
        }

        [TestMethod]
        public async Task ReadFeedIteratorCore_ReadNextAsync()
        {
            int numItems = 100;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);
            FeedIterator iterator = CreateReadFeedIterator(
                documentContainer,
                continuationToken: null,
                pageSize: 10);

            int count = 0;
            while (iterator.HasMoreResults)
            {
                ResponseMessage message = await iterator.ReadNextAsync();
                CosmosArray documents = GetDocuments(message.Content);
                count += documents.Count;
            }

            Assert.AreEqual(numItems, count);
        }

        [TestMethod]
        public async Task ReadFeedIteratorCore_ReadNextAsync_WithContinuationToken()
        {
            int numItems = 100;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);

            int count = 0;
            string continuationToken = null;
            do
            {
                FeedIterator iterator = CreateReadFeedIterator(
                    documentContainer,
                    continuationToken: continuationToken,
                    pageSize: 10);
                ResponseMessage message = await iterator.ReadNextAsync();
                Assert.AreEqual(HttpStatusCode.OK, message.StatusCode, message.ErrorMessage);
                CosmosArray documents = GetDocuments(message.Content);
                count += documents.Count;
                continuationToken = message.ContinuationToken;
            } while (continuationToken != null);

            Assert.AreEqual(numItems, count);
        }

        [TestMethod]
        public async Task ReadFeedIteratorCore_DoesNotUpdateContinuation_OnError()
        {
            int numItems = 100;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(
                numItems,
                failureConfigs: new FlakyDocumentContainer.FailureConfigs(inject429s: true, injectEmptyPages: true));

            int count = 0;
            string continuationToken = null;
            bool needsToRetry = false;
            do
            {
                needsToRetry = false;
                FeedIterator iterator = CreateReadFeedIterator(
                    documentContainer,
                    continuationToken: continuationToken,
                    pageSize: 10);
                ResponseMessage message = await iterator.ReadNextAsync();
                if (message.IsSuccessStatusCode)
                {
                    CosmosArray documents = GetDocuments(message.Content);
                    count += documents.Count;
                    continuationToken = message.ContinuationToken;
                }
                else if((int)message.StatusCode == 429)
                {
                    Assert.IsNull(message.ContinuationToken);
                    needsToRetry = true;
                }
                else
                {
                    Assert.Fail();
                }
            } while (continuationToken != null || needsToRetry);

            Assert.AreEqual(numItems, count);
        }

        [TestMethod]
        public async Task ReadFeedIteratorCore_HandlesSplitsThroughPipeline()
        {
            int numItems = 100;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);
            FeedIterator iterator = CreateReadFeedIterator(
                documentContainer,
                continuationToken: null,
                pageSize: 10);

            Random random = new Random();
            int count = 0;
            while (iterator.HasMoreResults)
            {
                ResponseMessage message = await iterator.ReadNextAsync();
                CosmosArray documents = GetDocuments(message.Content);
                count += documents.Count;

                IReadOnlyList<FeedRangeEpk> ranges = await documentContainer.GetFeedRangesAsync(cancellationToken: default);
                FeedRangeEpk rangeToSplit = ranges[random.Next(0, ranges.Count)];
                await documentContainer.SplitAsync(rangeToSplit, cancellationToken: default);
            }

            Assert.AreEqual(numItems, count);
        }

        private static CosmosArray GetDocuments(Stream stream)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                CosmosObject element = CosmosObject.CreateFromBuffer(memoryStream.ToArray());
                if (!element.TryGetValue("Documents", out CosmosArray value))
                {
                    Assert.Fail();
                }

                return value;
            }
        }

        private static FeedIteratorInternal CreateReadFeedIterator(
            IDocumentContainer documentContainer,
            string continuationToken,
            int pageSize)
        {
            return new ReadFeedIteratorCore(
                documentContainer,
                queryRequestOptions: null,
                continuationToken: continuationToken,
                pageSize: pageSize,
                cancellationToken: default);
        }

        private static async Task<IDocumentContainer> CreateDocumentContainerAsync(
            int numItems,
            FlakyDocumentContainer.FailureConfigs failureConfigs = default)
        {
            PartitionKeyDefinition partitionKeyDefinition = new PartitionKeyDefinition()
            {
                Paths = new System.Collections.ObjectModel.Collection<string>()
                    {
                        "/pk"
                    },
                Kind = PartitionKind.Hash,
                Version = PartitionKeyDefinitionVersion.V2,
            };

            IMonadicDocumentContainer monadicDocumentContainer = new InMemoryContainer(partitionKeyDefinition);
            if (failureConfigs != null)
            {
                monadicDocumentContainer = new FlakyDocumentContainer(monadicDocumentContainer, failureConfigs);
            }

            DocumentContainer documentContainer = new DocumentContainer(monadicDocumentContainer);

            for (int i = 0; i < 3; i++)
            {
                IReadOnlyList<FeedRangeInternal> ranges = await documentContainer.GetFeedRangesAsync(cancellationToken: default);
                foreach (FeedRangeInternal range in ranges)
                {
                    await documentContainer.SplitAsync(range, cancellationToken: default);
                }
            }

            for (int i = 0; i < numItems; i++)
            {
                // Insert an item
                CosmosObject item = CosmosObject.Parse($"{{\"pk\" : {i} }}");
                while (true)
                {
                    TryCatch<Record> monadicCreateRecord = await documentContainer.MonadicCreateItemAsync(item, cancellationToken: default);
                    if (monadicCreateRecord.Succeeded)
                    {
                        break;
                    }
                }
            }

            return documentContainer;
        }
    }
}
