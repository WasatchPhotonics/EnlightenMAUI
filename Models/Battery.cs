using System;

using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models;

// encapsulate battery processing
public class Battery 
{
    ushort raw;
    byte rawLevel;
    byte rawState;
    public bool initialized = false;

    // valid range should be (0, 100)
    public double level { get; private set; }
    
    bool charging;
    DateTime? lastChecked;

    Logger logger = Logger.getInstance();

    public bool isExpired
    {
        get
        {
            if (!initialized)
                return true;
            return (DateTime.Now - lastChecked.Value).TotalSeconds >= 60;
        }
    }

    public void parse(byte[] response)
    {
        if (response is null)
        {
            logger.error("Battery: no response");
            return;
        }

        if (response.Length != 2)
        {
            logger.error("Battery: invalid response");
            return;
        }

        ushort raw = ParseData.toUInt16(response, 0);
        this.raw = raw; // store for debugging, as toString() outputs this

        // reversed from SiG-290?
        rawLevel = (byte)((raw & 0xff00) >> 8);
        rawState = (byte)(raw & 0xff);

        level = (double)rawLevel;
        
        charging = (rawState & 1) == 1;

        lastChecked = DateTime.Now;
        initialized = true;

        logger.debug($"Battery.parse: {this}");
    }

    override public string ToString()
    {
        if (!initialized)
            return "???";

        logger.debug("Battery: raw 0x{0:x4} (lvl {1}, st 0x{2:x2}) = {3:f2}", raw, rawLevel, rawState, level);

        int intLevel = (int)Math.Round(level);
        return charging ? $"{intLevel}%>" : $"<{intLevel}%";
    }
}
