using System;
namespace EnlightenMAUI.Models;

public enum LaserMode { MANUAL=0, AUTO_DARK=1, MAX_LASER_MODES=2 };
public enum LaserType { NORMAL=0, MAX_LASER_TYPES=1 } // add others if/when implemented in FW

public enum BYTE_6_FLAGS
{
    INTERLOCK_CLOSED = 0x01,
    LASER_ACTIVE = 0x02
}

public class LaserState
{
    public LaserType type = LaserType.NORMAL;
    public LaserMode mode = LaserMode.MANUAL;
    public bool enabled;
    public ushort watchdogSec 
    { 
        get; 
        set; 
    } = 5;
    public ushort laserDelayMS { get; set; } = 0;
    public bool interlockClosed = false;
    public bool laserActive = false;

    public int payloadLength = 7;

    // While we're working out various timing and stabilization issues in FW,
    // we're just going to implment Raman Mode in SW.  However, the FW version
    // will likely come back at some point, so right now we're just kludging
    // around it.  
    //
    // Nevertheless, laserState.mode is still used for internal state; we
    // just don't send that mode to the FW, or read it back from the FW.
    // But we do still use it for internal decision-making.
    public const bool SW_RAMAN_MODE = false;

    Logger logger = Logger.getInstance();

    public void dump()
    {
        logger.debug("LaserState:");
        logger.debug($"  type = {type}");
        logger.debug($"  mode = {mode}");
        logger.debug($"  enabled = {enabled}");
        logger.debug($"  watchdogSec = {watchdogSec}");
        logger.debug($"  laserDelayMS = {laserDelayMS} ms");
        logger.debug($"  interlockClosed = {interlockClosed}");
        logger.debug($"  laserActive = {laserActive}");
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
        interlockClosed = false;
        laserActive = false;
        dump();
    }

    // Generate a 4-byte payload to be sent from Central to Peripheral.  
    //
    // We enforce some cross-field logic here, so that we're not actually 
    // overwrite values in the Spectrometer or LaserState models, so that
    // when logic constraints are removed, the configured "model" values are 
    // immediately restored.  I am actually not sure where the best place to
    // override these is.
    public byte[] serialize()
    {
        if (payloadLength == 8)
        {
            byte[] data = new byte[7];
            data[1] = (byte)type;
            data[0] = (byte)mode;
            data[2] = (byte)(enabled ? 1 : 0);
            data[3] = (byte)((watchdogSec >> 8) & 0xff);
            data[4] = (byte)((watchdogSec) & 0xff);
            data[5] = 0xff;
            data[6] = 0xff;

            return data;
        }
        else
        {
            byte[] data = new byte[4];
            data[1] = (byte)type;
            data[0] = (byte)mode;
            data[2] = (byte)(enabled ? 1 : 0);
            data[3] = (byte)watchdogSec;

            if (mode == LaserMode.AUTO_DARK)
            {
                if (SW_RAMAN_MODE)
                {
                    data[0] = (byte)LaserMode.MANUAL;
                }
            }

            return data;
        }
    }

    // Parse and validate a 6-9 byte payload received from Peripheral by Central.
    //
    // If any part of the payload does not pass validation, the entire payload
    // is rejected and application state is unchanged.
    public bool parse(byte[] data, bool setValues = true)
    {
        if (data.Length < 7)
        {
            logger.error($"rejecting LaserState with invalid payload length {data.Length}");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // Laser Type
        ////////////////////////////////////////////////////////////////////

        LaserType newType = LaserType.NORMAL;
        byte value = data[1];
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
        value = data[0];
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

        ushort newWatchdog = 0;
        ushort newLaserDelayMS = 0;

        if (data.Length == 8)
        {
            
            ////////////////////////////////////////////////////////////////////
            // Laser Watchdog
            ////////////////////////////////////////////////////////////////////

            newWatchdog = 0;
            value = data[3];
            if (value < 0xff)
            {
                newWatchdog = (ushort)((data[3] << 8) | data[4]);
            }
            else
            {
                logger.error($"rejecting LaserState with invalid LaserWatchdog 0x{value:x2}");
                return false;
            }

            ////////////////////////////////////////////////////////////////////
            // Laser Delay
            ////////////////////////////////////////////////////////////////////


            newLaserDelayMS = (ushort)((data[5] << 8) | data[6]);

            ////////////////////////////////////////////////////////////////////
            // Bitmask
            ////////////////////////////////////////////////////////////////////

            if (setValues)
            {
                interlockClosed = (data[7] & (byte)BYTE_6_FLAGS.INTERLOCK_CLOSED) != 0;
                laserActive = (data[7] & (byte)BYTE_6_FLAGS.LASER_ACTIVE) != 0;
            }

            if (!laserActive)
                newEnabled = false;

            ////////////////////////////////////////////////////////////////////
            // all fields validated, accept new values
            ////////////////////////////////////////////////////////////////////
        }

        else
        {
            ////////////////////////////////////////////////////////////////////
            // Laser Watchdog
            ////////////////////////////////////////////////////////////////////

            newWatchdog = 0;
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


            newLaserDelayMS = (ushort)((data[4] << 8) | data[5]);

            ////////////////////////////////////////////////////////////////////
            // Bitmask
            ////////////////////////////////////////////////////////////////////

            if (setValues)
            {
                interlockClosed = (data[6] & (byte)BYTE_6_FLAGS.INTERLOCK_CLOSED) != 0;
                laserActive = (data[6] & (byte)BYTE_6_FLAGS.LASER_ACTIVE) != 0;
            }

            if (!laserActive)
                newEnabled = false;

            ////////////////////////////////////////////////////////////////////
            // all fields validated, accept new values
            ////////////////////////////////////////////////////////////////////
        }


        if (setValues)
        {
            type = newType;
            enabled = newEnabled;
            watchdogSec = newWatchdog;
            laserDelayMS = newLaserDelayMS;
        }

        if (!setValues)
            payloadLength = data.Length;

        dump();

        return true;
    }
}
