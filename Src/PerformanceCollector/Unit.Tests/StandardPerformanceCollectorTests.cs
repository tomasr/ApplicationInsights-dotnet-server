﻿namespace Unit.Tests
{
    using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.Implementation.StandardPerformanceCollector;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// StandardPerformanceCollector tests.
    /// </summary>
    [TestClass]
    public class StandardPerformanceCollectorTests : PerformanceCollectorTestBase
    {
        [TestMethod]
        [TestCategory("RequiresPerformanceCounters")]
        public void PerformanceCollectorSanityTest()
        {
           this.PerformanceCollectorSanityTest(new StandardPerformanceCollector());
        }

        [TestMethod]
        [TestCategory("RequiresPerformanceCounters")]
        public void PerformanceCollectorRefreshCountersTest()
        {
            this.PerformanceCollectorRefreshCountersTest(new StandardPerformanceCollector());
        }

        [TestMethod]
        [TestCategory("RequiresPerformanceCounters")]
        public void PerformanceCollectorBadStateTest()
        {
            this.PerformanceCollectorBadStateTest(new StandardPerformanceCollector());
        }
    }
}