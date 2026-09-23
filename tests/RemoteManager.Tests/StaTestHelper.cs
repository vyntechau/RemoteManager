namespace RemoteManager.Tests;

public static class StaTestHelper
{
    public static void Run(Action action, int timeoutMs = 5000)
    {
        Exception? capturedException = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(timeoutMs);
        Xunit.Assert.True(finished, "STA thread timed out.");
        if (capturedException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturedException).Throw();
        }
    }

    public static T Run<T>(Func<T> func, int timeoutMs = 5000)
    {
        T result = default!;
        Run(() => { result = func(); }, timeoutMs);
        return result;
    }
}
