namespace EnlightenMAUI.Models;

// This class provides the bridge for displaying tuples from the EEPROM's 
// Model to the the DeviceView's ListView.
public class ViewableSetting
{
    public string name  { get; set; }
    public string value { get; set; }

    public ViewableSetting(string name, string value = "unknown") 
    {
        this.name = name;
        this.value = value;
    }
}
