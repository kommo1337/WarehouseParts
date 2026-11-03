using System.Windows.Threading;

namespace WarehouseParts.Ui;

public static class AppFeedback
{
    public static event Action<string>? Message;

    public static void Send(string message)
    {
        Message?.Invoke(message);
    }
}

public sealed class FeedbackSink
{
    private readonly Action<string> _show;
    private readonly Dispatcher _dispatcher;

    public FeedbackSink(Action<string> show, Dispatcher dispatcher)
    {
        _show = show;
        _dispatcher = dispatcher;
        AppFeedback.Message += OnMessage;
    }

    private void OnMessage(string message)
    {
        _dispatcher.BeginInvoke(() => _show(message));
    }

    public void Dispose()
    {
        AppFeedback.Message -= OnMessage;
    }
}
