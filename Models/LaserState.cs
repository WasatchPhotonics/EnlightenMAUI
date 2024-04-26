using System;
namespace EnlightenMAUI.Models;

public enum LaserMode { MANUAL=0, RAMAN=1, MAX_LASER_MODES=2 };
public enum LaserType { NORMAL=0, MAX_LASER_TYPES=1 } // add others if/when implemented in FW

public class LaserState
{
    public LaserType type = LaserType.NORMAL;
    public LaserMode mode = LaserMode.MANUAL;
    public bool enabled;
    public byte watchdogSec = 5;
    public ushort laserDelayMS = 500;

    // While we're working out various timing and stabilization issues in FW,
    // we're just going to implment Raman Mode in SW.  However, the FW version
    // will likely come back at some point, so right now we're just kludging
    // around it.  
    //
    // Nevertheless, laserState.mode is still used for internal state; we
    // just don't send that mode to the FW, or read it back from the FW.
    // But we do still use it for internal decision-making.
    public const bool SW_RAMAN_MODE = true;

    Logger logger = Logger.getInstance();

    public void dump()
    {
        logger.debug("LaserState:");
        logger.debug($"  type = {type}");
        logger.debug($"  mode = {mode}");
        logger.debug($"  enabled = {enabled}");
        logger.debug($"  watchdogSec = {watchdogSec}");
        logger.debug($"  laserDelayMS = {laserDelayMS} ms");
    }

    public LaserState(byte[] data = null)
    {
        reset();
        if (data != null)
        {
            if (!parse(data))
            { 
                logger.error("LaserState instantiated with invalid data; reverting to default values");
                reset();
            }
        }
    }

    // reset to the presumed Peripheral defaults
    void reset()
    {
        type = LaserType.NORMAL;
        mode = LaserMode.MANUAL;
        enabled = false;
        watchdogSec = 10;
        laserDelayMS = 500;
        dump();
    }

    // Generate a 6-byte payload to be sent from Central to Peripheral.  
    //
    // We enforce some cross-field logic here, so that we're not actually 
    // overwrite values in the Spectrometer or LaserState models, so that
    // when logic constraints are removed, the configured "model" values are 
    // immediately restored.  I am actually not sure where the best place to
    // override these is.
    public byte[] serialize()
    {
        byte[] data = new byte[6];

        data[0] = (byte)type;
        data[1] = (byte)mode;
        data[2] = (byte)(enabled ? 1 : 0);
        data[3] = watchdogSec;
        data[4] = (byte)((laserDelayMS >> 8) & 0xff);
        data[5] = (byte)( laserDelayMS       & 0xff);

        if (mode == LaserMode.RAMAN)
        {
            if (SW_RAMAN_MODE)
            {
                // If we're in SW Raman Mode, tell the device we're in 
                // Manual Mode
                data[1] = (byte)LaserMode.MANUAL;

                // ignore laserDelayMS, as we'll do it in SW
                data[4] = 0;
                data[5] = 0;
            }
            /*
            else
            {
                // If We're in HW Raman Mode, auto-enable the laser and 
                // disable the watchdog
                data[2] = 1; // laserEnable = true
                data[3] = 0; // watchdogSec = 0
            }
            */
        }

        return data;
    }

    // Parse and validate a 6-byte payload received from Peripheral by Central.
    //
    // If any part of the payload does not pass validation, the entire payload
    // is rejected and application state is unchanged.
    public bool parse(byte[] data)
    {
        if (data.Length != 6)
        {
            logger.error($"rejecting LaserState with invalid payload length {data.Length}");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // Laser Type
        ////////////////////////////////////////////////////////////////////

        LaserType newType = LaserType.NORMAL;
        byte value = data[0];
        if (value < (byte)LaserType.MAX_LASER_TYPES)
        {
            newType = (LaserType)value;
        }
        else
        {
            logger.error($"rejecting LaserState with invalid LaserType {value}");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // Laser Mode
        ////////////////////////////////////////////////////////////////////

        LaserMode newMode = LaserMode.MANUAL;
        value = data[1];
        if (value < (byte)LaserMode.MAX_LASER_MODES)
        {
            newMode = (LaserMode)value;
        }
        else
        {
            logger.error($"rejecting LaserState with invalid LaserMode 0x{value:x2}");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // Laser Enabled
        ////////////////////////////////////////////////////////////////////

        bool newEnabled = false;
        value = data[2];
        if (value < 0x02)
        {
            newEnabled = value == 0x01;
        }
        else
        {
            logger.error($"rejecting LaserState with invalid LaserEnabled 0x{value:x2}");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // Laser Watchdog
        ////////////////////////////////////////////////////////////////////

        byte newWatchdog = 0;
        value = data[3];
        if (value < 0xff)
        {
            newWatchdog = value;
        }
        else
        {
            logger.error($"rejecting LaserState with invalid LaserWatchdog 0x{value:x2}");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // Laser Delay
        ////////////////////////////////////////////////////////////////////


        ushort newLaserDelayMS = (ushort)((data[4] << 8) | data[5]);

        ////////////////////////////////////////////////////////////////////
        // all fields validated, accept new values
        ////////////////////////////////////////////////////////////////////

        type = newType;
        enabled = newEnabled;
        watchdogSec = newWatchdog;
        laserDelayMS = newLaserDelayMS;

        // if (!SW_RAMAN_MODE)
        //     mode = newMode;

        dump();

        return true;
    }
}
