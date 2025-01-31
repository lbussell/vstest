// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TestPlatform.CrossPlatEngine.UnitTests;

using System;

using Microsoft.VisualStudio.TestPlatform.CrossPlatEngine;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

[TestClass]
public class TestHostManagerFactoryTests
{
    private readonly TestHostManagerFactory _testHostManagerFactory;
    private readonly Mock<IRequestData> _mockRequestData;
    private readonly Mock<IMetricsCollection> _mockMetricsCollection;

    public TestHostManagerFactoryTests()
    {
        _mockMetricsCollection = new Mock<IMetricsCollection>();
        _mockRequestData = new Mock<IRequestData>();
        _mockRequestData.Setup(rd => rd.MetricsCollection).Returns(_mockMetricsCollection.Object);
        _testHostManagerFactory = new TestHostManagerFactory(_mockRequestData.Object);
    }

    [TestMethod]
    public void ConstructorShouldThrowIfRequestDataIsNull()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new TestHostManagerFactory(null));
    }

    [TestMethod]
    public void GetDiscoveryManagerShouldReturnADiscoveryManagerInstance()
    {
        Assert.IsNotNull(_testHostManagerFactory.GetDiscoveryManager());
    }

    [TestMethod]
    public void GetDiscoveryManagerShouldCacheTheDiscoveryManagerInstance()
    {
        Assert.AreEqual(_testHostManagerFactory.GetDiscoveryManager(), _testHostManagerFactory.GetDiscoveryManager());
    }

    [TestMethod]
    public void GetDiscoveryManagerShouldReturnAnExecutionManagerInstance()
    {
        Assert.IsNotNull(_testHostManagerFactory.GetExecutionManager());
    }

    [TestMethod]
    public void GetDiscoveryManagerShouldCacheTheExecutionManagerInstance()
    {
        Assert.AreEqual(_testHostManagerFactory.GetExecutionManager(), _testHostManagerFactory.GetExecutionManager());
    }
}