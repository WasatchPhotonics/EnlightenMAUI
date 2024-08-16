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

    public void parse(uint response)
    {
        uint lsb = (byte)(response & 0xff);
        uint msb = (byte)((response >> 8) & 0xff);
        uint chg = (byte)((response >> 16) & 0xff);
        uint raw = (lsb << 16) | (msb << 8) | chg;

        byte lsbyte = (byte)((raw >> 16) & 0xff);
        byte msbyte = (byte)((raw >> 8) & 0xff);
        level = ((float)(1.0 * msb)) + ((float)(1.0 * lsb / 256.0));
        charging = 0 != (raw & 0xff);

        lastChecked = DateTime.Now;
        initialized = true;
    }

    override public string ToString()
    {
        if (!initialized)
            return "???";

        logger.debug($"Battery: raw 0x{raw:x4} (lvl {rawLevel}, st 0x{rawState:x2}) = {level:f2}");

        int intLevel = (int)Math.Round(level);
        return charging ? $"{intLevel}%>" : $"<{intLevel}%";
    }
}
