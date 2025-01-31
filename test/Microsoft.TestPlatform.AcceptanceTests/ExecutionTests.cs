﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.TestPlatform.AcceptanceTests;

using System;
using System.IO;

using global::TestPlatform.TestUtilities;

using TestUtilities;

using VisualStudio.TestTools.UnitTesting;

[TestClass]
public class ExecutionTests : AcceptanceTestBase
{
    [TestMethod]
    [NetFullTargetFrameworkDataSource(inIsolation: true, inProcess: true)]
    [NetCoreTargetFrameworkDataSource]
    public void RunMultipleTestAssemblies(RunnerInfo runnerInfo)
    {
        SetTestEnvironment(_testEnvironment, runnerInfo);

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject.dll", "SimpleTestProject2.dll").Trim('\"');

        InvokeVsTestForExecution(assemblyPaths, GetTestAdapterPath(), FrameworkArgValue, string.Empty);

        ValidateSummaryStatus(2, 2, 2);
        ExitCodeEquals(1); // failing tests
    }

    [TestMethod]
    [NetFullTargetFrameworkDataSource(inIsolation: true, inProcess: true)]
    [NetCoreTargetFrameworkDataSource]
    public void RunMultipleTestAssembliesWithoutTestAdapterPath(RunnerInfo runnerInfo)
    {
        SetTestEnvironment(_testEnvironment, runnerInfo);

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject.dll").Trim('\"');
        var xunitAssemblyPath = _testEnvironment.TargetFramework.Equals("net451") ?
            _testEnvironment.GetTestAsset("XUTestProject.dll", "net46") :
            _testEnvironment.GetTestAsset("XUTestProject.dll");

        assemblyPaths = string.Concat(assemblyPaths, "\" \"", xunitAssemblyPath);
        InvokeVsTestForExecution(assemblyPaths, string.Empty, FrameworkArgValue, string.Empty);

        ValidateSummaryStatus(2, 2, 1);
        ExitCodeEquals(1); // failing tests
    }

    // We cannot run this test on Mac/Linux because we're trying to switch the arch between x64 and x86
    // and after --arch feature implementation we won't find correct muxer on CI.
    [TestCategory("Windows")]
    [TestMethod]
    [NetFullTargetFrameworkDataSource]
    [NetCoreTargetFrameworkDataSource]
    public void RunMultipleTestAssembliesInParallel(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject.dll", "SimpleTestProject2.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);
        arguments = string.Concat(arguments, " /Parallel");
        arguments = string.Concat(arguments, " /Platform:x86");
        arguments += GetDiagArg(tempDir.Path);

        // for the desktop we will run testhost.x86 in two copies, but for core
        // we will run a combination of testhost.x86 and dotnet, where the dotnet will be
        // the test console, and sometimes it will be the test host (e.g dotnet, dotnet, testhost.x86, or dotnet, testhost.x86, testhost.x86)
        // based on the target framework
        int expectedNumOfProcessCreated = 2;
        var testHostProcessNames = IsDesktopTargetFramework()
            ? new[] { "testhost.x86", }
            : new[] { "testhost.x86", "testhost", };

        InvokeVsTest(arguments);

        AssertExpectedNumberOfHostProcesses(expectedNumOfProcessCreated, tempDir.Path, testHostProcessNames);
        ValidateSummaryStatus(2, 2, 2);
        ExitCodeEquals(1); // failing tests
    }

    [TestMethod]
    [NetFullTargetFrameworkDataSource(inIsolation: true, inProcess: true)]
    [NetCoreTargetFrameworkDataSource]
    public void TestSessionTimeOutTests(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths =
            BuildMultipleAssemblyPath("SimpleTestProject3.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);
        arguments = string.Concat(arguments, " /TestCaseFilter:TestSessionTimeoutTest");

        // set TestSessionTimeOut = 7 sec
        arguments = string.Concat(arguments, " -- RunConfiguration.TestSessionTimeout=7000");
        InvokeVsTest(arguments);

        ExitCodeEquals(1);
        StdErrorContains("Test Run Aborted.");
        StdErrorContains("Aborting test run: test run timeout of 7000 milliseconds exceeded.");
        StdOutputDoesNotContains("Total tests: 6");
    }

    [TestMethod]
    [NetCoreTargetFrameworkDataSource]
    public void TestPlatformShouldBeCompatibleWithOldTestHost(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SampleProjectWithOldTestHost.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        InvokeVsTest(arguments);

        ValidateSummaryStatus(1, 0, 0);
        ExitCodeEquals(0);
    }

    [TestMethod]
    [NetFullTargetFrameworkDataSource(inIsolation: true, inProcess: true)]
    [NetCoreTargetFrameworkDataSource]
    public void WorkingDirectoryIsSourceDirectory(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject3.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);
        arguments = string.Concat(arguments, " /tests:WorkingDirectoryTest");

        InvokeVsTest(arguments);

        ValidateSummaryStatus(1, 0, 0);
        ExitCodeEquals(0);
    }

    [TestMethod]
    [NetFullTargetFrameworkDataSource]
    [NetCoreTargetFrameworkDataSource]
    public void StackOverflowExceptionShouldBeLoggedToConsoleAndDiagLogFile(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        if (IntegrationTestEnvironment.BuildConfiguration.Equals("release", StringComparison.OrdinalIgnoreCase))
        {
            // On release, x64 builds, recursive calls may be replaced with loops (tail call optimization)
            Assert.Inconclusive("On StackOverflowException testhost not exited in release configuration.");
            return;
        }

        var diagLogFilePath = Path.Combine(tempDir.Path, $"std_error_log_{Guid.NewGuid()}.txt");
        File.Delete(diagLogFilePath);

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject3.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);
        arguments = string.Concat(arguments, " /testcasefilter:ExitWithStackoverFlow");
        arguments = string.Concat(arguments, $" /diag:{diagLogFilePath}");

        InvokeVsTest(arguments);

        var errorMessage = "Process is terminated due to StackOverflowException.";
        if (runnerInfo.TargetFramework.StartsWith("netcoreapp2."))
        {
            errorMessage = "Process is terminating due to StackOverflowException.";
        }

        ExitCodeEquals(1);
        FileAssert.Contains(diagLogFilePath, errorMessage);
        StdErrorContains(errorMessage);
        File.Delete(diagLogFilePath);
    }

    [TestMethod]
    [NetFullTargetFrameworkDataSource]
    [NetCoreTargetFrameworkDataSource]
    public void UnhandleExceptionExceptionShouldBeLoggedToDiagLogFile(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var diagLogFilePath = Path.Combine(tempDir.Path, $"std_error_log_{Guid.NewGuid()}.txt");
        File.Delete(diagLogFilePath);

        var assemblyPaths =
            BuildMultipleAssemblyPath("SimpleTestProject3.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);
        arguments = string.Concat(arguments, " /testcasefilter:ExitwithUnhandleException");
        arguments = string.Concat(arguments, $" /diag:{diagLogFilePath}");

        InvokeVsTest(arguments);

        var errorFirstLine = "Test host standard error line: Unhandled Exception: System.InvalidOperationException: Operation is not valid due to the current state of the object.";
        FileAssert.Contains(diagLogFilePath, errorFirstLine);
        File.Delete(diagLogFilePath);
    }

    [TestMethod]
    [TestCategory("Windows-Review")]
    [NetFullTargetFrameworkDataSource]
    public void IncompatibleSourcesWarningShouldBeDisplayedInTheConsole(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var expectedWarningContains = @"Following DLL(s) do not match current settings, which are .NETFramework,Version=v4.5.1 framework and X86 platform. SimpleTestProject3.dll is built for Framework .NETFramework,Version=v4.5.1 and Platform X64";
        var assemblyPaths =
            BuildMultipleAssemblyPath("SimpleTestProject3.dll", "SimpleTestProjectx86.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);
        arguments = string.Concat(arguments, " /testcasefilter:PassingTestx86");

        InvokeVsTest(arguments);

        ValidateSummaryStatus(1, 0, 0);
        ExitCodeEquals(0);

        // When both x64 & x86 DLL is passed x64 dll will be ignored.
        StdOutputContains(expectedWarningContains);
    }

    [TestMethod]
    [TestCategory("Windows-Review")]
    [NetFullTargetFrameworkDataSource]
    public void NoIncompatibleSourcesWarningShouldBeDisplayedInTheConsole(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var expectedWarningContains = @"Following DLL(s) do not match current settings, which are .NETFramework,Version=v4.5.1 framework and X86 platform. SimpleTestProjectx86 is built for Framework .NETFramework,Version=v4.5.1 and Platform X86";
        var assemblyPaths =
            BuildMultipleAssemblyPath("SimpleTestProjectx86.dll");
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        InvokeVsTest(arguments);

        ValidateSummaryStatus(1, 0, 0);
        ExitCodeEquals(0);

        StdOutputDoesNotContains(expectedWarningContains);
    }

    [TestMethod]
    [TestCategory("Windows-Review")]
    [NetFullTargetFrameworkDataSource]
    public void IncompatibleSourcesWarningShouldBeDisplayedInTheConsoleOnlyWhenRunningIn32BitOS(RunnerInfo runnerInfo)
    {
        AcceptanceTestBase.SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var expectedWarningContains = @"Following DLL(s) do not match current settings, which are .NETFramework,Version=v4.5.1 framework and X86 platform. SimpleTestProject2.dll is built for Framework .NETFramework,Version=v4.5.1 and Platform X64";
        var assemblyPaths =
            BuildMultipleAssemblyPath("SimpleTestProject2.dll");
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        InvokeVsTest(arguments);

        ValidateSummaryStatus(1, 1, 1);
        ExitCodeEquals(1);

        // If we are running this test on 64 bit OS, it should not output any warning
        if (Environment.Is64BitOperatingSystem)
        {
            StdOutputDoesNotContains(expectedWarningContains);
        }
        // If we are running this test on 32 bit OS, it should output warning message
        else
        {
            StdOutputContains(expectedWarningContains);
        }
    }

    [TestMethod]
    [TestCategory("Windows-Review")]
    [NetFullTargetFrameworkDataSource]
    public void ExitCodeShouldReturnOneWhenTreatNoTestsAsErrorParameterSetToTrueAndNoTestMatchesFilter(RunnerInfo runnerInfo)
    {
        SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject2.dll").Trim('\"');

        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        // Setting /TestCaseFilter to the test name, which does not exists in the assembly, so we will have 0 tests executed
        arguments = string.Concat(arguments, " /TestCaseFilter:TestNameThatMatchesNoTestInTheAssembly");

        arguments = string.Concat(arguments, " -- RunConfiguration.TreatNoTestsAsError=true");
        InvokeVsTest(arguments);

        ExitCodeEquals(1);
    }

    [TestMethod]
    [TestCategory("Windows-Review")]
    [NetFullTargetFrameworkDataSource]
    public void ExitCodeShouldReturnZeroWhenTreatNoTestsAsErrorParameterSetToFalseAndNoTestMatchesFilter(RunnerInfo runnerInfo)
    {
        SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject2.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        // Setting /TestCaseFilter to the test name, which does not exists in the assembly, so we will have 0 tests executed
        arguments = string.Concat(arguments, " /TestCaseFilter:TestNameThatMatchesNoTestInTheAssembly");

        arguments = string.Concat(arguments, " -- RunConfiguration.TreatNoTestsAsError=false");
        InvokeVsTest(arguments);

        ExitCodeEquals(0);
    }

    [TestMethod]
    [TestCategory("Windows")]
    [NetFullTargetFrameworkDataSource]
    public void ExitCodeShouldNotDependOnTreatNoTestsAsErrorTrueValueWhenThereAreAnyTestsToRun(RunnerInfo runnerInfo)
    {
        SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject2.dll").Trim('\"');

        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        arguments = string.Concat(arguments, " -- RunConfiguration.TreatNoTestsAsError=true");
        InvokeVsTest(arguments);

        // Returning 1 because of failing test in test assembly (SimpleTestProject2.dll)
        ExitCodeEquals(1);
    }

    [TestMethod]
    [TestCategory("Windows")]
    [NetFullTargetFrameworkDataSource]
    public void ExitCodeShouldNotDependOnFailTreatNoTestsAsErrorFalseValueWhenThereAreAnyTestsToRun(RunnerInfo runnerInfo)
    {
        SetTestEnvironment(_testEnvironment, runnerInfo);
        using var tempDir = new TempDirectory();

        var assemblyPaths = BuildMultipleAssemblyPath("SimpleTestProject2.dll").Trim('\"');
        var arguments = PrepareArguments(assemblyPaths, GetTestAdapterPath(), string.Empty, FrameworkArgValue, runnerInfo.InIsolationValue, resultsDirectory: tempDir.Path);

        arguments = string.Concat(arguments, " -- RunConfiguration.TreatNoTestsAsError=false");
        InvokeVsTest(arguments);

        // Returning 1 because of failing test in test assembly (SimpleTestProject2.dll)
        ExitCodeEquals(1);
    }
}
