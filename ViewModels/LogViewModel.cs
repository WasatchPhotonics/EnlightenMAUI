using System.Text;
using System.ComponentModel;

namespace EnlightenMAUI.ViewModels;

// I'm probably making this more complicated than it needs to be, explicitly
// passing a delegate down into the Logger.  There's probably a much simpler
// way to use notification events to automatically trigger GUI updates.  If
// you can simplify this, please show me how :-)
public class LogViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Logger logger = Logger.getInstance();

    public LogViewModel()
    {
        logger.debug("LogViewModel.ctor: start");

        saveCmd = new Command(() => { doSave(); });
        logger.PropertyChanged += Logger_PropertyChanged;
    }

    private void Logger_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name == "history")
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(logText)));
    }

    public string title
    {
        get => "Event Log";
    }

    public string logText { get => logger.history.ToString(); }

    public bool verbose 
    { 
        get => logger.level == LogLevel.DEBUG;
        set => logger.level = value ? LogLevel.DEBUG : LogLevel.INFO;
    }

    public bool debugBLE
    { 
        get => logger.loggingBLE;
        set => logger.loggingBLE = value;
    }

    public Command saveCmd { get; }

    void doSave()
    {
        logger.save();
    }
}
