﻿namespace Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.Implementation.StandardPerformanceCollector
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;

    internal class StandardPerformanceCollector : IPerformanceCollector
    {
        private readonly List<Tuple<PerformanceCounterData, PerformanceCounter>> performanceCounters = new List<Tuple<PerformanceCounterData, PerformanceCounter>>();

        private IEnumerable<string> win32Instances;
        private IEnumerable<string> clrInstances;
        private bool dependendentInstancesLoaded = false;

        /// <summary>
        /// Gets a collection of registered performance counters.
        /// </summary>
        public IEnumerable<PerformanceCounterData> PerformanceCounters
        {
            get { return this.performanceCounters.Select(t => t.Item1).ToList(); }
        }

        /// <summary>
        /// Performs collection for all registered counters.
        /// </summary>
        /// <param name="onReadingFailure">Invoked when an individual counter fails to be read.</param>
        public IEnumerable<Tuple<PerformanceCounterData, double>> Collect(
            Action<string, Exception> onReadingFailure = null)
        {
            return this.performanceCounters.Where(pc => !pc.Item1.IsInBadState).SelectMany(
                pc =>
                    {
                        double value;

                        try
                        {
                            value = CollectCounter(pc.Item2);
                        }
                        catch (InvalidOperationException e)
                        {
                            if (onReadingFailure != null)
                            {
                                onReadingFailure(
                                    PerformanceCounterUtility.FormatPerformanceCounter(pc.Item2),
                                    e);
                            }

                            return new Tuple<PerformanceCounterData, double>[] { };
                        }

                        return new[] { Tuple.Create(pc.Item1, value) };
                    });
        }

        /// <summary>
        /// Refreshes counters.
        /// </summary>
        public void RefreshCounters()
        {
            this.LoadDependentInstances();

            // We need to refresh counters in bad state and counters with placeholders in instance names
            var countersToRefresh =
                this.PerformanceCounters.Where(pc => pc.IsInBadState || pc.UsesInstanceNamePlaceholder)
                    .ToList();

            countersToRefresh.ForEach(pcd => this.RefreshCounter(pcd));

            PerformanceCollectorEventSource.Log.CountersRefreshedEvent(countersToRefresh.Count.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Registers a counter using the counter name and reportAs value to the total list of counters.
        /// </summary>
        /// <param name="perfCounterName">Name of the performance counter.</param>
        /// <param name="reportAs">Report as name for the performance counter.</param>
        /// <param name="isCustomCounter">Boolean to check if the performance counter is custom defined.</param>
        /// <param name="error">Captures the error logged.</param>
        /// <param name="blockCounterWithInstancePlaceHolder">Boolean that controls the registry of the counter based on the availability of instance place holder.</param>
        public void RegisterCounter(
            string perfCounterName,
            string reportAs,
            bool isCustomCounter,
            out string error,
            bool blockCounterWithInstancePlaceHolder = false)
        {
            bool usesInstanceNamePlaceholder;

            if (!this.dependendentInstancesLoaded)
            {
                this.LoadDependentInstances();
                this.dependendentInstancesLoaded = true;
            }

            var pc = PerformanceCounterUtility.CreateAndValidateCounter(
                perfCounterName,
                this.win32Instances,
                this.clrInstances,
                out usesInstanceNamePlaceholder,
                out error);

            // If blockCounterWithInstancePlaceHolder is true, then we register the counter only if usesInstanceNamePlaceHolder is true.
            if (pc != null && !(blockCounterWithInstancePlaceHolder && usesInstanceNamePlaceholder))
            {
                this.RegisterCounter(perfCounterName, reportAs, pc, isCustomCounter, usesInstanceNamePlaceholder, out error);
            }
        }

        /// <summary>
        /// Collects a value for a single counter.
        /// </summary>
        private static double CollectCounter(PerformanceCounter pc)
        {
            try
            {
                return pc.NextValue();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.PerformanceCounterReadFailed,
                        PerformanceCounterUtility.FormatPerformanceCounter(pc)),
                    e);
            }
        }

        /// <summary>
        /// Rebinds performance counters to Windows resources.
        /// </summary>
        private void RefreshPerformanceCounter(PerformanceCounterData pcd)
        {
            Tuple<PerformanceCounterData, PerformanceCounter> tupleToRemove = this.performanceCounters.FirstOrDefault(t => t.Item1 == pcd);
            if (tupleToRemove != null)
            {
                this.performanceCounters.Remove(tupleToRemove);
            }

            this.RegisterPerformanceCounter(
                pcd.OriginalString,
                pcd.ReportAs,
                pcd.CategoryName,
                pcd.CounterName,
                pcd.InstanceName,
                pcd.UsesInstanceNamePlaceholder,
                pcd.IsCustomCounter);
        }

        /// <summary>
        /// Loads instances that are used in performance counter computation.
        /// </summary>
        private void LoadDependentInstances()
        {
            this.win32Instances = PerformanceCounterUtility.GetWin32ProcessInstances();
            this.clrInstances = PerformanceCounterUtility.GetClrProcessInstances();
        }

        /// <summary>
        /// Refreshes the counter associated with a specific performance counter data.
        /// </summary>
        /// <param name="pcd">Target performance counter data to refresh.</param>
        private void RefreshCounter(PerformanceCounterData pcd)
        {
            string dummy;

            bool usesInstanceNamePlaceholder;
            var pc = PerformanceCounterUtility.CreateAndValidateCounter(
                pcd.OriginalString,
                this.win32Instances,
                this.clrInstances,
                out usesInstanceNamePlaceholder,
                out dummy);

            try
            {
                this.RefreshPerformanceCounter(pcd);

                PerformanceCollectorEventSource.Log.CounterRegisteredEvent(
                        PerformanceCounterUtility.FormatPerformanceCounter(pc));
            }
            catch (InvalidOperationException e)
            {
                PerformanceCollectorEventSource.Log.CounterRegistrationFailedEvent(
                    e.Message,
                    PerformanceCounterUtility.FormatPerformanceCounter(pc));
            }
        }

        /// <summary>
        /// Registers the counter to the existing list of counters.
        /// </summary>
        /// <param name="originalString">Counter original string.</param>
        /// <param name="reportAs">Counter report as.</param>
        /// <param name="pc">Performance counter.</param>
        /// <param name="isCustomCounter">Boolean to check if it is a custom counter.</param>
        /// <param name="usesInstanceNamePlaceholder">Uses Instance Name Place holder boolean.</param>
        /// <param name="error">Error message.</param>
        private void RegisterCounter(
            string originalString,
            string reportAs,
            PerformanceCounter pc,
            bool isCustomCounter,
            bool usesInstanceNamePlaceholder,
            out string error)
        {
            error = null;

            try
            {
                this.RegisterPerformanceCounter(
                    originalString,
                    reportAs,
                    pc.CategoryName,
                    pc.CounterName,
                    pc.InstanceName,
                    usesInstanceNamePlaceholder,
                    isCustomCounter);

                PerformanceCollectorEventSource.Log.CounterRegisteredEvent(
                    PerformanceCounterUtility.FormatPerformanceCounter(pc));
            }
            catch (InvalidOperationException e)
            {
                PerformanceCollectorEventSource.Log.CounterRegistrationFailedEvent(
                    e.Message,
                    PerformanceCounterUtility.FormatPerformanceCounter(pc));
                error = e.Message;
            }
        }

        /// <summary>
        /// Register a performance counter for collection.
        /// </summary>
        /// <param name="originalString">Original string definition of the counter.</param>
        /// <param name="reportAs">Alias to report the counter as.</param>
        /// <param name="categoryName">Category name.</param>
        /// <param name="counterName">Counter name.</param>
        /// <param name="instanceName">Instance name.</param>
        /// <param name="usesInstanceNamePlaceholder">Indicates whether the counter uses a placeholder in the instance name.</param>
        /// <param name="isCustomCounter">Indicates whether the counter is a custom counter.</param>
        private void RegisterPerformanceCounter(string originalString, string reportAs, string categoryName, string counterName, string instanceName, bool usesInstanceNamePlaceholder, bool isCustomCounter)
        {
            PerformanceCounter performanceCounter = null;

            try
            {
                performanceCounter = new PerformanceCounter(categoryName, counterName, instanceName, true);
            }
            catch (Exception e)
            {
                // we want to have another crack at it if instance placeholder is used,
                // notably due to the fact that CLR process ID counter only starts returning values after the first garbage collection
                if (!usesInstanceNamePlaceholder)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.PerformanceCounterRegistrationFailed,
                            categoryName,
                            counterName,
                            instanceName),
                        e);
                }
            }

            bool firstReadOk = false;
            try
            {
                // perform the first read. For many counters the first read will always return 0
                // since a single sample is not enough to calculate a value
                performanceCounter.NextValue();

                firstReadOk = true;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.PerformanceCounterFirstReadFailed,
                        categoryName,
                        counterName,
                        instanceName),
                    e);
            }
            finally
            {
                PerformanceCounterData perfData = new PerformanceCounterData(
                        originalString,
                        reportAs,
                        usesInstanceNamePlaceholder,
                        isCustomCounter,
                        !firstReadOk,
                        categoryName,
                        counterName,
                        instanceName);

                this.performanceCounters.Add(new Tuple<PerformanceCounterData, PerformanceCounter>(perfData, performanceCounter));
            }
        }
    }
}
