using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    public static class ProjectRealmTestRunner
    {
        private static TestRunnerApi testRunnerApi;
        private static ProjectRealmTestCallbacks callbacks;

        [MenuItem("Project Realm/Tests/Run All EditMode Tests")]
        public static void RunAllEditModeTests()
        {
            Run(TestMode.EditMode);
        }

        [MenuItem("Project Realm/Tests/Run All PlayMode Tests")]
        public static void RunAllPlayModeTests()
        {
            Run(TestMode.PlayMode);
        }

        private static void Run(TestMode mode)
        {
            testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            callbacks = new ProjectRealmTestCallbacks();
            testRunnerApi.RegisterCallbacks(callbacks);
            testRunnerApi.Execute(new ExecutionSettings(new Filter
            {
                testMode = mode
            }));
        }

        private sealed class ProjectRealmTestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log($"Project Realm test run started: {testsToRun.TestCaseCount} test(s).");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log(
                    $"Project Realm tests finished: {result.PassCount} passed, " +
                    $"{result.FailCount} failed, {result.SkipCount} skipped, " +
                    $"{result.InconclusiveCount} inconclusive.");
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.FailCount > 0)
                {
                    Debug.LogError($"Project Realm test failed: {result.FullName}\n{result.Message}\n{result.StackTrace}");
                }
            }
        }
    }
}
