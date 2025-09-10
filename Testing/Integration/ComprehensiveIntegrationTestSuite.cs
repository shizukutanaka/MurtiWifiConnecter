using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MurtiWifiConnecter.Services;
using MurtiWifiConnecter.Infrastructure;
using MurtiWifiConnecter.Automation;
using MurtiWifiConnecter.Testing;

namespace MurtiWifiConnecter.Testing.Integration
{
    /// <summary>
    /// 統合テストスイート
    /// </summary>
    [TestClass(Description = "Comprehensive integration tests for WiFi connection functionality")]
    public class ComprehensiveIntegrationTestSuite
    {
        private IWifiService _wifiService;
        private INotificationService _notificationService;
        private ITelemetryService _telemetryService;
        private ITaskScheduler _taskScheduler;
        private IWorkflowEngine _workflowEngine;

        public void Setup()
        {
            _wifiService = new WifiService();
            _notificationService = new WindowsNotificationService();
            _telemetryService = new TelemetryService();
            _taskScheduler = new TaskScheduler();
            _workflowEngine = new WorkflowEngine();
        }

        public void Teardown()
        {
            _taskScheduler?.Dispose();
            _notificationService?.Dispose();
        }

        [TestMethod(Description = "Test WiFi scanning and network discovery")]
        public async Task TestWifiScanningIntegration()
        {
            // Arrange
            _telemetryService.StartOperation("WifiScan");

            try
            {
                // Act
                var networks = await _wifiService.ScanNetworksAsync();

                // Assert
                Assert.IsNotNull(networks, "Networks collection should not be null");
                Assert.IsTrue(networks.Any(), "Should discover at least one network");

                // Verify network properties
                foreach (var network in networks)
                {
                    Assert.IsNotNull(network.SSID, "SSID should not be null");
                    Assert.IsTrue(network.SignalStrength >= 0 && network.SignalStrength <= 100, 
                        "Signal strength should be between 0 and 100");
                }

                _telemetryService.StopOperation("WifiScan", true);
                _telemetryService.TrackEvent("WifiScanCompleted", 
                    new Dictionary<string, string> { { "NetworkCount", networks.Count().ToString() } });
            }
            catch (Exception ex)
            {
                _telemetryService.StopOperation("WifiScan", false);
                _telemetryService.TrackException(ex);
                throw;
            }
        }

        [TestMethod(Description = "Test connection workflow end-to-end")]
        public async Task TestConnectionWorkflowIntegration()
        {
            // Arrange
            var workflow = CreateConnectionWorkflow("TestNetwork", "TestPassword");
            
            // Act
            var result = await _workflowEngine.ExecuteWorkflowAsync(workflow);

            // Assert
            Assert.IsNotNull(result, "Workflow result should not be null");
            Assert.IsTrue(result.ActionResults.Any(), "Should have executed at least one action");
            
            // Verify each action was executed
            var scanAction = result.ActionResults.FirstOrDefault(r => r.ActionName == "ScanNetworks");
            Assert.IsNotNull(scanAction, "Scan action should be executed");

            var connectAction = result.ActionResults.FirstOrDefault(r => r.ActionName == "ConnectToNetwork");
            Assert.IsNotNull(connectAction, "Connect action should be executed");

            _telemetryService.TrackEvent("WorkflowCompleted", 
                new Dictionary<string, string> 
                { 
                    { "WorkflowId", workflow.Id },
                    { "Success", result.Success.ToString() },
                    { "ActionsExecuted", result.ActionResults.Count.ToString() }
                });
        }

        [TestMethod(Description = "Test notification system integration")]
        public async Task TestNotificationSystemIntegration()
        {
            // Arrange
            var testMessage = "Integration test notification";
            var testTitle = "Test Title";

            // Act & Assert - Test different notification types
            try
            {
                _notificationService.ShowToast(testTitle, testMessage, ToastDuration.Short);
                await Task.Delay(100); // Allow notification to process

                _notificationService.ShowBalloon(testTitle, testMessage, BalloonIcon.Info);
                await Task.Delay(100);

                // Test confirmation dialog (would show in real scenario)
                // var confirmResult = await _notificationService.ShowConfirmationAsync("Test confirmation?");

                _telemetryService.TrackEvent("NotificationTest", 
                    new Dictionary<string, string> { { "Type", "Integration" } });

                Assert.IsTrue(true, "Notifications executed without exceptions");
            }
            catch (Exception ex)
            {
                _telemetryService.TrackException(ex);
                Assert.IsTrue(false, $"Notification test failed: {ex.Message}");
            }
        }

        [TestMethod(Description = "Test task scheduling integration")]
        public async Task TestTaskSchedulingIntegration()
        {
            // Arrange
            var executedTasks = new List<string>();
            var testTask = new ScheduledTask
            {
                Id = "test-task-1",
                Name = "Test Scheduled Task",
                Action = async () =>
                {
                    executedTasks.Add("test-task-1");
                    await Task.Delay(10);
                },
                Schedule = new TaskSchedule
                {
                    Type = ScheduleType.Once,
                    ExecutionTime = DateTime.Now.AddMilliseconds(100)
                }
            };

            // Act
            _taskScheduler.ScheduleTask(testTask);
            _taskScheduler.StartScheduler();

            // Wait for task execution
            await Task.Delay(500);

            // Assert
            Assert.IsTrue(executedTasks.Contains("test-task-1"), "Scheduled task should be executed");
            Assert.AreEqual(TaskStatus.Completed, testTask.Status, "Task status should be completed");

            _taskScheduler.StopScheduler();
            _telemetryService.TrackEvent("TaskSchedulingTest", 
                new Dictionary<string, string> { { "TasksExecuted", executedTasks.Count.ToString() } });
        }

        [TestMethod(Description = "Test telemetry data collection")]
        public async Task TestTelemetryIntegration()
        {
            // Arrange
            var startTime = DateTime.Now.AddMinutes(-1);
            var endTime = DateTime.Now;

            // Act - Generate various telemetry data
            _telemetryService.TrackEvent("TestEvent", 
                new Dictionary<string, string> { { "Source", "Integration" } },
                new Dictionary<string, double> { { "Duration", 123.45 } });

            _telemetryService.TrackMetric("TestMetric", 42.0, 
                new Dictionary<string, string> { { "Unit", "Seconds" } });

            try
            {
                throw new InvalidOperationException("Test exception for telemetry");
            }
            catch (Exception ex)
            {
                _telemetryService.TrackException(ex, 
                    new Dictionary<string, string> { { "TestContext", "Integration" } });
            }

            _telemetryService.TrackDependency("TestDependency", "HTTP", "GET /api/test", 
                DateTime.Now.AddSeconds(-2), TimeSpan.FromSeconds(1), true);

            // Generate report
            var report = await _telemetryService.GenerateReportAsync(startTime, endTime);

            // Assert
            Assert.IsNotNull(report, "Telemetry report should not be null");
            Assert.IsTrue(report.Events.Any(), "Should have recorded events");
            Assert.IsTrue(report.Metrics.Any(), "Should have recorded metrics");
            Assert.IsTrue(report.Exceptions.Any(), "Should have recorded exceptions");
            Assert.IsTrue(report.Dependencies.Any(), "Should have recorded dependencies");
            Assert.IsNotNull(report.Summary, "Report summary should not be null");
        }

        [TestMethod(Description = "Test error handling and recovery")]
        public async Task TestErrorHandlingIntegration()
        {
            // Arrange
            var workflow = CreateFailingWorkflow();

            // Act
            var result = await _workflowEngine.ExecuteWorkflowAsync(workflow);

            // Assert
            Assert.IsNotNull(result, "Result should not be null even when workflow fails");
            Assert.IsFalse(result.Success, "Workflow should fail as expected");
            Assert.IsTrue(result.ActionResults.Any(r => !r.Success), "Should have failed actions");

            // Verify error is properly tracked
            var failedAction = result.ActionResults.FirstOrDefault(r => !r.Success);
            Assert.IsNotNull(failedAction, "Should have at least one failed action");
            Assert.IsNotNull(failedAction.ErrorMessage, "Failed action should have error message");

            _telemetryService.TrackEvent("ErrorHandlingTest", 
                new Dictionary<string, string> 
                { 
                    { "ExpectedFailure", "true" },
                    { "FailedActions", result.ActionResults.Count(r => !r.Success).ToString() }
                });
        }

        [TestMethod(Description = "Test performance under load")]
        public async Task TestPerformanceIntegration()
        {
            // Arrange
            const int concurrentOperations = 10;
            var tasks = new List<Task>();
            var startTime = DateTime.Now;

            // Act - Simulate concurrent operations
            for (int i = 0; i < concurrentOperations; i++)
            {
                int taskId = i;
                tasks.Add(Task.Run(async () =>
                {
                    _telemetryService.StartOperation($"ConcurrentOp_{taskId}");
                    try
                    {
                        // Simulate work
                        await Task.Delay(100);
                        var networks = await _wifiService.ScanNetworksAsync();
                        _telemetryService.StopOperation($"ConcurrentOp_{taskId}", true);
                    }
                    catch (Exception ex)
                    {
                        _telemetryService.StopOperation($"ConcurrentOp_{taskId}", false);
                        _telemetryService.TrackException(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            var totalDuration = DateTime.Now - startTime;

            // Assert
            Assert.IsTrue(totalDuration.TotalSeconds < 10, "Operations should complete within reasonable time");

            _telemetryService.TrackMetric("PerformanceTest_Duration", totalDuration.TotalMilliseconds);
            _telemetryService.TrackMetric("PerformanceTest_ConcurrentOps", concurrentOperations);
        }

        /// <summary>
        /// 接続ワークフローを作成
        /// </summary>
        private Workflow CreateConnectionWorkflow(string ssid, string password)
        {
            return new Workflow
            {
                Name = "WiFi Connection Workflow",
                Description = "Complete workflow for connecting to WiFi network",
                Actions = new List<WorkflowAction>
                {
                    new WorkflowAction
                    {
                        Name = "ScanNetworks",
                        Type = "WifiScan",
                        Order = 1,
                        Parameters = new Dictionary<string, object>()
                    },
                    new WorkflowAction
                    {
                        Name = "ConnectToNetwork",
                        Type = "WifiConnect",
                        Order = 2,
                        Parameters = new Dictionary<string, object>
                        {
                            { "SSID", ssid },
                            { "Password", password }
                        }
                    },
                    new WorkflowAction
                    {
                        Name = "NotifySuccess",
                        Type = "Notification",
                        Order = 3,
                        Parameters = new Dictionary<string, object>
                        {
                            { "Message", $"Successfully connected to {ssid}" }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 失敗するワークフローを作成（エラーハンドリングテスト用）
        /// </summary>
        private Workflow CreateFailingWorkflow()
        {
            return new Workflow
            {
                Name = "Failing Workflow",
                Description = "Workflow designed to fail for testing error handling",
                Actions = new List<WorkflowAction>
                {
                    new WorkflowAction
                    {
                        Name = "SuccessfulAction",
                        Type = "Wait",
                        Order = 1,
                        Parameters = new Dictionary<string, object> { { "Duration", 10 } }
                    },
                    new WorkflowAction
                    {
                        Name = "FailingAction",
                        Type = "NonExistentActionType",
                        Order = 2,
                        IsRequired = true
                    }
                }
            };
        }
    }

    /// <summary>
    /// WiFiサービス統合テスト
    /// </summary>
    [TestClass(Description = "WiFi service specific integration tests")]
    public class WifiServiceIntegrationTests
    {
        private IWifiService _wifiService;

        public void Setup()
        {
            _wifiService = new WifiService();
        }

        [TestMethod(Description = "Test network scanning with various parameters")]
        public async Task TestNetworkScanningVariations()
        {
            // Test different scanning scenarios
            var standardScan = await _wifiService.ScanNetworksAsync();
            Assert.IsNotNull(standardScan, "Standard scan should return results");

            // Verify network data integrity
            foreach (var network in standardScan)
            {
                Assert.IsNotNull(network.SSID, "Each network should have an SSID");
                Assert.IsTrue(network.SignalStrength >= 0, "Signal strength should be non-negative");
            }
        }

        [TestMethod(Description = "Test connection state management")]
        public async Task TestConnectionStateManagement()
        {
            // Get current connection status
            var initialStatus = await _wifiService.GetConnectionStatusAsync();
            Assert.IsNotNull(initialStatus, "Should be able to get connection status");

            // Test status properties
            Assert.IsNotNull(initialStatus.SSID, "SSID should be available (may be empty if disconnected)");
        }
    }
}