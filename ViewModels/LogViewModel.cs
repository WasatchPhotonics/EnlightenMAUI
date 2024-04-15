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
        // pass the StringBuilder into the Logger to acrue messages
        logger.history = history;

        // give the Logger a callback to let the ViewModel know when the
        // StringBuilder has been updated (probably easier way to do this)
        logger.logChangedDelegate = RaisePropertyChanged;

        saveCmd = new Command(() => { doSave(); });
    } 

    public string title
    {
        get => "Event Log";
    }

    // when the GUI receives a notification to redraw the LogView, it will
    // call this to render the StringBuilder
    public string logText
    {
        get => history.ToString();
        set 
        {
            history.Append(value);
        }
    }

    // this is where Logger text is actually acrued (whether notifications
    // are sent to the GUI or not)
    StringBuilder history = new StringBuilder("Log data");

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

    // this gets called by the Logger via its logChangedDelegate handle
    protected void RaisePropertyChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(logText)));
    }
}
