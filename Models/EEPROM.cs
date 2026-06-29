using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models;

// simplified from WasatchNET
public class EEPROM
{
    /////////////////////////////////////////////////////////////////////////
    // Singleton
    /////////////////////////////////////////////////////////////////////////

    public static EEPROM instance = null;

    public static EEPROM getInstance()
    {
        if (instance is null)
            instance = new EEPROM();
        return instance;
    }

    /////////////////////////////////////////////////////////////////////////
    // private attributes
    /////////////////////////////////////////////////////////////////////////

    internal const int MAX_PAGES = 138;
    internal const int INITIAL_PAGES = 9;
    internal const int SUBPAGE_COUNT = 1;
    internal const int API6_SUBPAGE_COUNT = 4;
    internal const int PAGE_LENGTH = 64;

    public enum PAGE_SUBFORMAT { USER_DATA, INTENSITY_CALIBRATION, WAVECAL_SPLINES, UNTETHERED_DEVICE, DETECTOR_REGIONS, PIXEL_CALIBRATION };
    public enum LIGHT_SOURCE_TYPE { UNDEFINED, THREE_B_SINGLE_MODE, THREE_B_MULTI_MODE, NONE = 254 };
    public enum HORIZONTAL_BINNING_METHOD { BIN_2X2, CORRECT_SSC, CORRECT_SSC_BIN2X2, BIN_4X2, BIN_4X2_INTERP, BIN_4X2_AVG };
    public enum PIXEL_CALIBRATION_TYPE { NONE, USER_DATA, ETALON_CORRECTION, EVEN_ODD, IRRADIANCE };

    const byte FORMAT = 19;

    Logger logger = Logger.getInstance();

    public List<byte[]> pages { get; private set; }

    public List<ViewableSetting> viewableSettings = new List<ViewableSetting>();

    /////////////////////////////////////////////////////////////////////////
    //
    // public attributes
    //
    /////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////
    // Collections
    /////////////////////////////////////////////////////////////////////////

    public FeatureMask featureMask = new FeatureMask();

    /////////////////////////////////////////////////////////////////////////
    // Page 0
    /////////////////////////////////////////////////////////////////////////

    public byte format { get; set; }
    public string model { get; set; }
    public string serialNumber { get; set; }
    public uint baudRate { get; set; }
    public bool hasCooling { get; set; }
    public bool hasBattery { get; set; }
    public bool hasLaser { get; set; }
    public ushort slitSizeUM { get; set; }
    public ushort startupIntegrationTimeMS { get; set; }
    public short startupDetectorTemperatureDegC
    {
        get { return TECSetpoint; }
        set
        {
            TECSetpoint = value;
        }
    }

    //
    // this field used to be exclusively be used for the above for which it is now aliased (startup temp)
    // however it briefly was given a second functionality to set the laser TEC setpoint. As of format 16 this
    // secondary use case is deprecated by laserTECSetpoint. There are not currently any spectrometers that use
    // both laser tec setpoint and non-ambient detectors, but format 16 would allow us to use both via laserTECSetpoint
    //
    public short TECSetpoint
    {
        get { return _TECSetpoint; }
        set
        {
            _TECSetpoint = value;
        }
    }
    short _TECSetpoint;
    public byte startupTriggeringMode { get; set; }
    public float detectorGain { get; set; }
    public short  detectorOffset { get; set; }
    public float  detectorGainOdd { get; set; }
    public short  detectorOffsetOdd { get; set; }
    public UInt16 laserTECSetpoint { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 1
    /////////////////////////////////////////////////////////////////////////
    ///
    public float[] wavecalCoeffs { get; set; }
    public float[] degCToDACCoeffs { get; set; }
    public short detectorTempMin { get; set; }
    public short detectorTempMax { get; set; }
    public float[] adcToDegCCoeffs { get; set; }
    public short thermistorResistanceAt298K { get; set; }
    public short thermistorBeta { get; set; }
    public string calibrationDate { get; set; }
    public string calibrationBy { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 2
    /////////////////////////////////////////////////////////////////////////

    public string detectorName { get; set; }
    public ushort activePixelsHoriz { get; set; }
    public ushort activePixelsVert { get; set; }
    public uint minIntegrationTimeMS { get; set; }
    public uint maxIntegrationTimeMS { get; set; }
    public ushort actualPixelsHoriz { get; set; }
    public ushort ROIHorizStart { get; set; }
    public ushort ROIHorizEnd { get; set; }
    public ushort[] ROIVertRegionStart { get; set; }
    public ushort[] ROIVertRegionEnd { get; set; }
    public float[] linearityCoeffs { get; set; }
    public byte laserWarmupSec { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 3
    /////////////////////////////////////////////////////////////////////////
    
    public byte maxLaserTempDegC { get; set; }
    public float maxLaserPowerMW { get; set; }
    public float minLaserPowerMW { get; set; }
    public float laserExcitationWavelengthNMFloat { get; set; }
    public float[] laserPowerCoeffs { get; set; }
    public float avgResolution { get; set; }
    public UInt16 laserWatchdogTimer { get; set; }
    public LIGHT_SOURCE_TYPE lightSourceType { get; set; }
    public UInt16 powerWatchdogTimer { get; set; }
    public UInt16 detectorTimeout { get; set; }
    public HORIZONTAL_BINNING_METHOD horizontalBinningMethod { get; set; }
    public byte laserDacAttenuation { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 4
    /////////////////////////////////////////////////////////////////////////

    public byte[] userData { get; set; }
    public string userText
    {
        get => ParseData.toString(userData);
        set
        {
            userData = new byte[value.Length + 1];
            for (int i = 0; i < userData.Length; i++)
                userData[i] = (byte)(i < value.Length ? value[i] : 0);
        }
    }

    /////////////////////////////////////////////////////////////////////////
    // Page 5
    /////////////////////////////////////////////////////////////////////////

    public short[] badPixels { get; set; }
    public List<short> badPixelList { get; private set; }
    public SortedSet<short> badPixelSet { get; private set; }
    public string productConfiguration { get; set; }
    public byte[] assemblyRevision { get; set; }
    public PAGE_SUBFORMAT subformat { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 6
    /////////////////////////////////////////////////////////////////////////

    public float[] intensityCorrectionCoeffs { get; set; }
    public byte intensityCorrectionOrder { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 7
    /////////////////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////////////////////////
    // Page 8
    /////////////////////////////////////////////////////////////////////////

    public string laserPassword { get; set; }
    public FeatureMaskXS featureMaskXS = new FeatureMaskXS();
    public PIXEL_CALIBRATION_TYPE pixelCalibrationType { get; set; }
    public UInt16 pixelCalibrationStart { get; set; }
    public UInt16 pixelCalibrationCount { get; set; }
    public string usbMfgName { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Pages 10-137 (subformat PIXEL_CALIBRATION)
    /////////////////////////////////////////////////////////////////////////

    public List<float> pixelCalibrationFactors { get; set; }

    public string fullModel
    {
        get { return model + productConfiguration; }
        set
        {
            if (value.Length > 16)
            {
                model = value.Substring(0, 16);
                productConfiguration = value.Substring(16);
            }

            else
                model = value;
        }
    }

    /////////////////////////////////////////////////////////////////////////
    // private methods
    /////////////////////////////////////////////////////////////////////////

    private EEPROM()
    {
        wavecalCoeffs = new float[5];
        degCToDACCoeffs = new float[3];
        adcToDegCCoeffs = new float[3];
        ROIVertRegionStart = new ushort[3];
        ROIVertRegionEnd = new ushort[3];
        badPixels = new short[15];
        linearityCoeffs = new float[5];
        laserPowerCoeffs = new float[4];
        intensityCorrectionCoeffs = new float[12];

        badPixelList = new List<short>();
        badPixelSet = new SortedSet<short>();
    }

    public EEPROM(EEPROMJSON json)
    {
        wavecalCoeffs = new float[5];
        degCToDACCoeffs = new float[3];
        adcToDegCCoeffs = new float[3];
        ROIVertRegionStart = new ushort[3];
        ROIVertRegionEnd = new ushort[3];
        badPixels = new short[15];
        linearityCoeffs = new float[5];
        laserPowerCoeffs = new float[4];
        intensityCorrectionCoeffs = new float[12];

        badPixelList = new List<short>();
        badPixelSet = new SortedSet<short>();

        pages = new List<byte[]>();
        for (ushort page = 0; page < MAX_PAGES; page++)
        {
            pages.Add(new byte[64]);
        }

        setFromJSON(json);
    }

    public void setFromJSON(EEPROMJSON json)
    {
        if (json.Serial != null)
            serialNumber = json.Serial;
        if (json.Model != null)
            fullModel = json.Model;
        slitSizeUM = (ushort)json.SlitWidth;
        baudRate = (uint)json.BaudRate;
        hasBattery = json.IncBattery;
        hasCooling = json.IncCooling;
        hasLaser = json.IncLaser;

        startupIntegrationTimeMS = (ushort)json.StartupIntTimeMS;
        TECSetpoint = (short)json.StartupTempC;
        startupTriggeringMode = (byte)json.StartupTriggerMode;
        detectorGain = (float)json.DetectorGain;
        detectorGainOdd = (float)json.DetectorGainOdd;
        detectorOffset = (Int16)json.DetectorOffset;
        detectorOffsetOdd = (Int16)json.DetectorOffsetOdd;
        laserTECSetpoint = (ushort)json.LaserTECSetpoint;

        featureMask.bin2x2 = json.Bin2x2;
        featureMask.invertXAxis = json.FlipXAxis;
        featureMask.gen15 = json.Gen15;
        featureMask.cutoffInstalled = json.CutoffFilter;
        featureMask.evenOddHardwareCorrected = json.EvenOddHardwareCorrected;
        featureMask.sigLaserTEC = json.SigLaserTEC;
        featureMask.hasInterlockFeedback = json.HasInterlockFeedback;
        featureMask.hasShutter = json.HasShutter;
        featureMask.disableBLEPower = json.DisableBLEPower;
        featureMask.disableLaserArmedIndication = json.DisableLaserArmedIndication;
        featureMask.interlockExcluded = json.InterlockExcluded;
        featureMask.laserTimeoutInCounts = json.LaserTimeoutInCounts;
        featureMask.isOEM = json.IsOEM;
        featureMaskXS.BLEDoorSensor = json.BLEDoorSensor;

        wavecalCoeffs[0] = (float)json.WavecalCoeffs[0];
        wavecalCoeffs[1] = (float)json.WavecalCoeffs[1];
        wavecalCoeffs[2] = (float)json.WavecalCoeffs[2];
        wavecalCoeffs[3] = (float)json.WavecalCoeffs[3];
        wavecalCoeffs[4] = (float)json.WavecalCoeffs[4];
        degCToDACCoeffs[0] = (float)json.TempToDACCoeffs[0];
        degCToDACCoeffs[1] = (float)json.TempToDACCoeffs[1];
        degCToDACCoeffs[2] = (float)json.TempToDACCoeffs[2];
        adcToDegCCoeffs[0] = (float)json.ADCToTempCoeffs[0];
        adcToDegCCoeffs[1] = (float)json.ADCToTempCoeffs[1];
        adcToDegCCoeffs[2] = (float)json.ADCToTempCoeffs[2];
        detectorTempMax = (Int16)json.DetectorTempMax;
        detectorTempMin = (Int16)json.DetectorTempMin;
        thermistorBeta = (Int16)json.ThermistorBeta;
        thermistorResistanceAt298K = (Int16)json.ThermistorResAt298K;

        if (json.CalibrationDate != null)
            calibrationDate = json.CalibrationDate;
        if (json.CalibrationBy != null)
            calibrationBy = json.CalibrationBy;

        if (json.DetectorName != null)
            detectorName = json.DetectorName;
        actualPixelsHoriz = (ushort)json.ActualPixelsHoriz;
        activePixelsHoriz = (UInt16)json.ActivePixelsHoriz;
        activePixelsVert = (UInt16)json.ActivePixelsVert;
        minIntegrationTimeMS = (UInt32)json.MinIntegrationTimeMS;
        maxIntegrationTimeMS = (UInt32)json.MaxIntegrationTimeMS;
        laserWatchdogTimer = (ushort)json.LaserWatchdogTimer;
        powerWatchdogTimer = (ushort)json.PowerWatchdogTimer;
        detectorTimeout = (ushort)json.DetectorTimeout;
        lightSourceType = (LIGHT_SOURCE_TYPE)json.LightSourceType;
        horizontalBinningMethod = (HORIZONTAL_BINNING_METHOD)json.HorizontalBinningMethod;
        ROIHorizStart = (UInt16)json.ROIHorizStart;
        ROIHorizEnd = (UInt16)json.ROIHorizEnd;
        ROIVertRegionStart[0] = (UInt16)json.ROIVertRegionStarts[0];
        ROIVertRegionEnd[0] = (UInt16)json.ROIVertRegionEnds[0];
        ROIVertRegionStart[1] = (UInt16)json.ROIVertRegionStarts[1];
        ROIVertRegionEnd[1] = (UInt16)json.ROIVertRegionEnds[1];
        ROIVertRegionStart[2] = (UInt16)json.ROIVertRegionStarts[2];
        ROIVertRegionEnd[2] = (UInt16)json.ROIVertRegionEnds[2];
        linearityCoeffs[0] = (float)json.LinearityCoeffs[0];
        linearityCoeffs[1] = (float)json.LinearityCoeffs[1];
        linearityCoeffs[2] = (float)json.LinearityCoeffs[2];
        linearityCoeffs[3] = (float)json.LinearityCoeffs[3];
        linearityCoeffs[4] = (float)json.LinearityCoeffs[4];

        maxLaserTempDegC = json.MaxLaserTempDegC;
        maxLaserPowerMW = (float)json.MaxLaserPowerMW;
        minLaserPowerMW = (float)json.MinLaserPowerMW;
        laserWarmupSec = json.LaserWarmupS;
        laserExcitationWavelengthNMFloat = (float)json.ExcitationWavelengthNM;
        laserDacAttenuation = json.LaserDACAttenuation;
        avgResolution = (float)json.AvgResolution;

        if (json.LaserPowerCoeffs != null)
        {
            laserPowerCoeffs[0] = (float)json.LaserPowerCoeffs[0];
            laserPowerCoeffs[1] = (float)json.LaserPowerCoeffs[1];
            laserPowerCoeffs[2] = (float)json.LaserPowerCoeffs[2];
            laserPowerCoeffs[3] = (float)json.LaserPowerCoeffs[3];
        }
        else
        {
            laserPowerCoeffs = new float[4];
        }

        if (json.UserText != null)
            userText = json.UserText;

        badPixels[0] = (Int16)json.BadPixels[0];
        badPixels[1] = (Int16)json.BadPixels[1];
        badPixels[2] = (Int16)json.BadPixels[2];
        badPixels[3] = (Int16)json.BadPixels[3];
        badPixels[4] = (Int16)json.BadPixels[4];
        badPixels[5] = (Int16)json.BadPixels[5];
        badPixels[6] = (Int16)json.BadPixels[6];
        badPixels[7] = (Int16)json.BadPixels[7];
        badPixels[8] = (Int16)json.BadPixels[8];
        badPixels[9] = (Int16)json.BadPixels[9];
        badPixels[10] = (Int16)json.BadPixels[10];
        badPixels[11] = (Int16)json.BadPixels[11];
        badPixels[12] = (Int16)json.BadPixels[12];
        badPixels[13] = (Int16)json.BadPixels[13];
        badPixels[14] = (Int16)json.BadPixels[14];
        if (json.AssemblyRevision != null)
        {
            assemblyRevision = new byte[json.AssemblyRevision.Length];
            Array.Copy(json.AssemblyRevision, assemblyRevision, json.AssemblyRevision.Length);
        }

        if (json.ProductConfig != null)
            productConfiguration = json.ProductConfig;

        PAGE_SUBFORMAT jsonSubformat = (PAGE_SUBFORMAT)json.Subformat;
        subformat = jsonSubformat;

        if (jsonSubformat == PAGE_SUBFORMAT.INTENSITY_CALIBRATION || jsonSubformat == PAGE_SUBFORMAT.UNTETHERED_DEVICE || jsonSubformat == PAGE_SUBFORMAT.PIXEL_CALIBRATION)
        {
            intensityCorrectionOrder = (byte)json.RelIntCorrOrder;
            if (json.RelIntCorrOrder > 0)
            {
                intensityCorrectionCoeffs = new float[intensityCorrectionOrder + 1];
                subformat = PAGE_SUBFORMAT.INTENSITY_CALIBRATION;

                for (int i = 0; i < intensityCorrectionCoeffs.Length; ++i)
                    intensityCorrectionCoeffs[i] = (float)json.RelIntCorrCoeffs[i];
            }
        }

        laserPassword = json.LaserPassword;
        usbMfgName = json.USBMfgName;

        if (jsonSubformat == PAGE_SUBFORMAT.PIXEL_CALIBRATION)
        {
            if (json.PixelCalibrationFactors != null)
                pixelCalibrationFactors = new List<float>(json.PixelCalibrationFactors);
            pixelCalibrationType = (PIXEL_CALIBRATION_TYPE)json.PixelCalibrationType;
            pixelCalibrationStart = json.PixelCalibrationStart;
            pixelCalibrationCount = json.PixelCalibrationCount;
        }
    }


    public EEPROMJSON toJSON()
    {
        EEPROMJSON json = new EEPROMJSON();

        json.Serial = serialNumber;
        json.Model = fullModel;
        json.SlitWidth = slitSizeUM;
        json.BaudRate = baudRate;
        json.IncBattery = hasBattery;
        json.IncCooling = hasCooling;
        json.IncLaser = hasLaser;
        json.StartupIntTimeMS = startupIntegrationTimeMS;
        json.StartupTempC = TECSetpoint;
        json.StartupTriggerMode = startupTriggeringMode;
        json.DetectorGain = detectorGain;
        json.DetectorGainOdd = detectorGainOdd;
        json.DetectorOffset = detectorOffset;
        json.DetectorOffsetOdd = detectorOffsetOdd;
        json.LaserTECSetpoint = laserTECSetpoint;
        json.WavecalCoeffs = new double[5];
        if (wavecalCoeffs != null)
        {
            json.WavecalCoeffs[0] = wavecalCoeffs[0];
            json.WavecalCoeffs[1] = wavecalCoeffs[1];
            json.WavecalCoeffs[2] = wavecalCoeffs[2];
            json.WavecalCoeffs[3] = wavecalCoeffs[3];
            json.WavecalCoeffs[4] = wavecalCoeffs[4];
        }
        json.TempToDACCoeffs = new double[3];
        if (degCToDACCoeffs != null)
        {
            json.TempToDACCoeffs[0] = degCToDACCoeffs[0];
            json.TempToDACCoeffs[1] = degCToDACCoeffs[1];
            json.TempToDACCoeffs[2] = degCToDACCoeffs[2];
        }
        json.ADCToTempCoeffs = new double[3];
        if (adcToDegCCoeffs != null)
        {
            json.ADCToTempCoeffs[0] = adcToDegCCoeffs[0];
            json.ADCToTempCoeffs[1] = adcToDegCCoeffs[1];
            json.ADCToTempCoeffs[2] = adcToDegCCoeffs[2];
        }
        json.LinearityCoeffs = new double[5];
        if (linearityCoeffs != null)
        {
            json.LinearityCoeffs[0] = linearityCoeffs[0];
            json.LinearityCoeffs[1] = linearityCoeffs[1];
            json.LinearityCoeffs[2] = linearityCoeffs[2];
            json.LinearityCoeffs[3] = linearityCoeffs[3];
            json.LinearityCoeffs[4] = linearityCoeffs[4];
        }
        json.DetectorTempMax = detectorTempMax;
        json.DetectorTempMin = detectorTempMin;
        json.ThermistorBeta = thermistorBeta;
        json.ThermistorResAt298K = thermistorResistanceAt298K;
        json.CalibrationDate = calibrationDate;
        json.CalibrationBy = calibrationBy;
        json.DetectorName = detectorName;
        json.ActualPixelsHoriz = actualPixelsHoriz;
        json.ActivePixelsHoriz = activePixelsHoriz;
        json.ActivePixelsVert = activePixelsVert;
        json.MinIntegrationTimeMS = (int)minIntegrationTimeMS;
        json.MaxIntegrationTimeMS = (int)maxIntegrationTimeMS;
        json.LaserWatchdogTimer = laserWatchdogTimer;
        json.PowerWatchdogTimer = powerWatchdogTimer;
        json.DetectorTimeout = detectorTimeout;
        json.LightSourceType = (byte)lightSourceType;
        json.HorizontalBinningMethod = (byte)horizontalBinningMethod;
        json.ROIHorizStart = ROIHorizStart;
        json.ROIHorizEnd = ROIHorizEnd;
        json.ROIVertRegionStarts = new int[3];
        if (ROIVertRegionStart != null)
        {
            json.ROIVertRegionStarts[0] = ROIVertRegionStart[0];
            json.ROIVertRegionStarts[1] = ROIVertRegionStart[1];
            json.ROIVertRegionStarts[2] = ROIVertRegionStart[2];
        }
        json.ROIVertRegionEnds = new int[3];
        if (ROIVertRegionEnd != null)
        {
            json.ROIVertRegionEnds[0] = ROIVertRegionEnd[0];
            json.ROIVertRegionEnds[1] = ROIVertRegionEnd[1];
            json.ROIVertRegionEnds[2] = ROIVertRegionEnd[2];
        }
        json.LaserPowerCoeffs = new double[4];
        if (laserPowerCoeffs != null)
        {
            json.LaserPowerCoeffs[0] = laserPowerCoeffs[0];
            json.LaserPowerCoeffs[1] = laserPowerCoeffs[1];
            json.LaserPowerCoeffs[2] = laserPowerCoeffs[2];
            json.LaserPowerCoeffs[3] = laserPowerCoeffs[3];
        }
        json.MaxLaserTempDegC = maxLaserTempDegC;
        json.MaxLaserPowerMW = maxLaserPowerMW;
        json.MinLaserPowerMW = minLaserPowerMW;
        json.ExcitationWavelengthNM = laserExcitationWavelengthNMFloat;
        json.LaserDACAttenuation = laserDacAttenuation;
        json.AvgResolution = avgResolution;
        json.BadPixels = new int[15];
        if (badPixels != null)
        {
            json.BadPixels[0] = badPixels[0];
            json.BadPixels[1] = badPixels[1];
            json.BadPixels[2] = badPixels[2];
            json.BadPixels[3] = badPixels[3];
            json.BadPixels[4] = badPixels[4];
            json.BadPixels[5] = badPixels[5];
            json.BadPixels[6] = badPixels[6];
            json.BadPixels[7] = badPixels[7];
            json.BadPixels[8] = badPixels[8];
            json.BadPixels[9] = badPixels[9];
            json.BadPixels[10] = badPixels[10];
            json.BadPixels[11] = badPixels[11];
            json.BadPixels[12] = badPixels[12];
            json.BadPixels[13] = badPixels[13];
            json.BadPixels[14] = badPixels[14];
        }
        if (assemblyRevision != null)
        {
            json.AssemblyRevision = new byte[assemblyRevision.Length];
            Array.Copy(assemblyRevision, json.AssemblyRevision, assemblyRevision.Length);
        }

        json.UserText = userText;
        json.ProductConfig = productConfiguration;
        if (subformat == PAGE_SUBFORMAT.INTENSITY_CALIBRATION || subformat == PAGE_SUBFORMAT.UNTETHERED_DEVICE || subformat == PAGE_SUBFORMAT.PIXEL_CALIBRATION)
        {
            json.RelIntCorrOrder = intensityCorrectionOrder;
            if (json.RelIntCorrOrder > 0 && intensityCorrectionCoeffs != null)
            {
                json.RelIntCorrCoeffs = new double[intensityCorrectionCoeffs.Length];
                for (int i = 0; i <= json.RelIntCorrOrder; ++i)
                {
                    if (i < intensityCorrectionCoeffs.Length)
                    {
                        json.RelIntCorrCoeffs[i] = intensityCorrectionCoeffs[i];
                    }
                }
            }
        }

        if (featureMask != null)
        {
            json.Bin2x2 = featureMask.bin2x2;
            json.FlipXAxis = featureMask.invertXAxis;
            json.Gen15 = featureMask.gen15;
            json.CutoffFilter = featureMask.cutoffInstalled;
            json.EvenOddHardwareCorrected = featureMask.evenOddHardwareCorrected;
            json.SigLaserTEC = featureMask.sigLaserTEC;
            json.HasInterlockFeedback = featureMask.hasInterlockFeedback;
            json.HasInterlockFeedback = featureMask.hasShutter;
            json.DisableBLEPower = featureMask.disableBLEPower;
            json.DisableLaserArmedIndication = featureMask.disableLaserArmedIndication;
            json.InterlockExcluded = featureMask.interlockExcluded;
            json.LaserTimeoutInCounts = featureMask.laserTimeoutInCounts;
            json.IsOEM = featureMask.isOEM;
        }
        if (featureMaskXS != null)
        {
            json.BLEDoorSensor = featureMaskXS.BLEDoorSensor;
        }

        json.LaserWarmupS = laserWarmupSec;
        json.Subformat = (byte)subformat;

        json.LaserPassword = laserPassword;
        json.USBMfgName = usbMfgName;
        json.PixelCalibrationFactors = pixelCalibrationFactors?.ToArray();
        json.PixelCalibrationType = (byte)pixelCalibrationType;
        json.PixelCalibrationStart = pixelCalibrationStart;
        json.PixelCalibrationCount = pixelCalibrationCount;
        json.FeatureMask = featureMask.ToString();

        return json;
    }


    bool isCorruptedPage(byte[] data)
    {
        var allZero = true;
        var allHigh = true;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0x00) allZero = false;
            if (data[i] != 0xff) allHigh = false;
            if (!allHigh && !allZero)
                return false;
        }
        return true;
    }

    public bool parse(List<byte[]> pages_in)
    {
        logger.debug("EEPROM.parse: start");
        if (pages_in is null)
            return false;

        pages = pages_in;
        logger.debug($"EEPROM.parse: received {pages.Count} pages");
        if (pages.Count < MAX_PAGES && pages.Count != INITIAL_PAGES)
        {
            logger.error($"EEPROM.parse: didn't receive {MAX_PAGES} pages");
            return false;
        }

        format = pages[0][63];
        if (format >= 8)
            subformat = (PAGE_SUBFORMAT)ParseData.toUInt8(pages[5], 63);
        else if (format >= 6)
            subformat = PAGE_SUBFORMAT.INTENSITY_CALIBRATION;
        else
            subformat = PAGE_SUBFORMAT.USER_DATA;

        // corrupted EEPROM test (comms, battery, unprogrammed)
        if (isCorruptedPage(pages[0]))
        {
            logger.error("EEPROM page 0 is corrupted or unprogrammed");
            return false;
        }

        try
        {
            model = ParseData.toString(pages[0], 0, 16);
            serialNumber = ParseData.toString(pages[0], 16, 16);
            baudRate = ParseData.toUInt32(pages[0], 32);
            hasCooling = ParseData.toBool(pages[0], 36);
            hasBattery = ParseData.toBool(pages[0], 37);
            hasLaser = ParseData.toBool(pages[0], 38);
            // excitationNM = ParseData.toUInt16(pages[0], 39); // changed to FeatureMask
            slitSizeUM = ParseData.toUInt16(pages[0], 41);

            startupIntegrationTimeMS = ParseData.toUInt16(pages[0], 43);
            startupDetectorTemperatureDegC = ParseData.toInt16(pages[0], 45);
            startupTriggeringMode = ParseData.toUInt8(pages[0], 47);
            detectorGain = ParseData.toFloat(pages[0], 48); // "even pixels" for InGaAs
            detectorOffset = ParseData.toInt16(pages[0], 52); // "even pixels" for InGaAs
            detectorGainOdd = ParseData.toFloat(pages[0], 54); // InGaAs-only
            detectorOffsetOdd = ParseData.toInt16(pages[0], 58); // InGaAs-only

            wavecalCoeffs[0] = ParseData.toFloat(pages[1], 0);
            wavecalCoeffs[1] = ParseData.toFloat(pages[1], 4);
            wavecalCoeffs[2] = ParseData.toFloat(pages[1], 8);
            wavecalCoeffs[3] = ParseData.toFloat(pages[1], 12);
            degCToDACCoeffs[0] = ParseData.toFloat(pages[1], 16);
            degCToDACCoeffs[1] = ParseData.toFloat(pages[1], 20);
            degCToDACCoeffs[2] = ParseData.toFloat(pages[1], 24);
            detectorTempMax = ParseData.toInt16(pages[1], 28);
            detectorTempMin = ParseData.toInt16(pages[1], 30);
            adcToDegCCoeffs[0] = ParseData.toFloat(pages[1], 32);
            adcToDegCCoeffs[1] = ParseData.toFloat(pages[1], 36);
            adcToDegCCoeffs[2] = ParseData.toFloat(pages[1], 40);
            thermistorResistanceAt298K = ParseData.toInt16(pages[1], 44);
            thermistorBeta = ParseData.toInt16(pages[1], 46);
            calibrationDate = ParseData.toString(pages[1], 48, 12);
            calibrationBy = ParseData.toString(pages[1], 60, 3);

            detectorName = ParseData.toString(pages[2], 0, 16);
            activePixelsHoriz = ParseData.toUInt16(pages[2], 16); // note: byte 18 unused
            activePixelsVert = ParseData.toUInt16(pages[2], 19);
            minIntegrationTimeMS = ParseData.toUInt16(pages[2], 21); // will overwrite if
            maxIntegrationTimeMS = ParseData.toUInt16(pages[2], 23); //   format >= 5
            actualPixelsHoriz = ParseData.toUInt16(pages[2], 25);
            ROIHorizStart = ParseData.toUInt16(pages[2], 27);
            ROIHorizEnd = ParseData.toUInt16(pages[2], 29);
            ROIVertRegionStart[0] = ParseData.toUInt16(pages[2], 31);
            ROIVertRegionEnd[0] = ParseData.toUInt16(pages[2], 33);
            ROIVertRegionStart[1] = ParseData.toUInt16(pages[2], 35);
            ROIVertRegionEnd[1] = ParseData.toUInt16(pages[2], 37);
            ROIVertRegionStart[2] = ParseData.toUInt16(pages[2], 39);
            ROIVertRegionEnd[2] = ParseData.toUInt16(pages[2], 41);
            linearityCoeffs[0] = ParseData.toFloat(pages[2], 43);
            linearityCoeffs[1] = ParseData.toFloat(pages[2], 47);
            linearityCoeffs[2] = ParseData.toFloat(pages[2], 51);
            linearityCoeffs[3] = ParseData.toFloat(pages[2], 55);
            linearityCoeffs[4] = ParseData.toFloat(pages[2], 59);

            // deviceLifetimeOperationMinutes = ParseData.toInt32(pages[3], 0);
            // laserLifetimeOperationMinutes = ParseData.toInt32(pages[3], 4);
            // laserTemperatureMax  = ParseData.toInt16(pages[3], 8);
            // laserTemperatureMin  = ParseData.toInt16(pages[3], 10);

            laserPowerCoeffs[0] = ParseData.toFloat(pages[3], 12);
            laserPowerCoeffs[1] = ParseData.toFloat(pages[3], 16);
            laserPowerCoeffs[2] = ParseData.toFloat(pages[3], 20);
            laserPowerCoeffs[3] = ParseData.toFloat(pages[3], 24);
            maxLaserPowerMW = ParseData.toFloat(pages[3], 28);
            minLaserPowerMW = ParseData.toFloat(pages[3], 32);
            laserExcitationWavelengthNMFloat = ParseData.toFloat(pages[3], 36);
            if (format >= 5)
            {
                minIntegrationTimeMS = ParseData.toUInt32(pages[3], 40);
                maxIntegrationTimeMS = ParseData.toUInt32(pages[3], 44);
            }

            userData = format < 4 ? new byte[PAGE_LENGTH-1] : new byte[PAGE_LENGTH];
            Array.Copy(pages[4], userData, userData.Length);

            badPixelSet = new SortedSet<short>();
            for (int i = 0; i < 15; i++)
            {
                short pixel = ParseData.toInt16(pages[5], i * 2);
                badPixels[i] = pixel;
                if (pixel >= 0)
                    badPixelSet.Add(pixel);
            }
            badPixelList = new List<short>(badPixelSet);

            if (format >= 5)
                productConfiguration = ParseData.toString(pages[5], 30, 16);
            else
                productConfiguration = "";

            if (format >= 6)
            {
                intensityCorrectionOrder = ParseData.toUInt8(pages[6], 0);
                uint numCoeffs = (uint)intensityCorrectionOrder + 1;

                if (numCoeffs > 8)
                    numCoeffs = 0;

                intensityCorrectionCoeffs = numCoeffs > 0 ? new float[numCoeffs] : null;

                for (int i = 0; i < numCoeffs; ++i)
                    intensityCorrectionCoeffs[i] = ParseData.toFloat(pages[6], 1 + 4 * i);
            }
            else
                intensityCorrectionOrder = 0; 

            if (format >= 7)
                avgResolution = ParseData.toFloat(pages[3], 48);
            else
                avgResolution = 0.0f;

            if (format >= 8)
                wavecalCoeffs[4] = ParseData.toFloat(pages[2], 21);

            if (format >= 9)
                featureMask = new FeatureMask(ParseData.toUInt16(pages[0], 39));

            if (format >= 10)
                laserWarmupSec = pages[2][18];
            else
                laserWarmupSec = 20;

            if (format >= 18)
            {
                laserPassword = ParseData.toString(pages[8], 0, 16);
                featureMaskXS = new FeatureMaskXS(ParseData.toUInt32(pages[8], 16));
            }
            else
                laserPassword = "";

            if (format >= 19)
            {
                usbMfgName = ParseData.toString(pages[8], 40, 20);

                if (subformat == PAGE_SUBFORMAT.PIXEL_CALIBRATION)
                {
                    pixelCalibrationType = (PIXEL_CALIBRATION_TYPE)ParseData.toUInt8(pages[8], 39);
                    pixelCalibrationStart = ParseData.toUInt16(pages[8], 40);
                    pixelCalibrationCount = ParseData.toUInt16(pages[8], 42);
                }

                if (pages.Count > INITIAL_PAGES)
                {
                    if (subformat == PAGE_SUBFORMAT.PIXEL_CALIBRATION)
                    {
                        if (pixelCalibrationCount > 0 && pixelCalibrationType == PIXEL_CALIBRATION_TYPE.ETALON_CORRECTION)
                        {
                            int factorPage = 10;
                            int offset = 0;
                            pixelCalibrationFactors = new List<float>();

                            for (int i = 0; i < pixelCalibrationCount; i++)
                            {
                                pixelCalibrationFactors.Add(ParseData.toFloat(pages[factorPage], offset));

                                offset += 4;
                                if (offset >= 63)
                                {
                                    offset = 0;
                                    ++factorPage;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.error($"EEPROM: caught exception: {ex.Message}");
            return false;
        }

        logger.debug("EEPROM.parse: enforcing reasonable defaults");
        enforceReasonableDefaults();

        logger.debug("EEPROM.parse: registering all");
        registerAll();

        logger.debug("EEPROM.parse: done");
        return true;
    }

    public bool hasLaserPowerCalibration()
    {
        if (maxLaserPowerMW <= 0)
            return false;

        if (laserPowerCoeffs == null || laserPowerCoeffs.Length < 4)
            return false;

        foreach (double d in laserPowerCoeffs)
            if (Double.IsNaN(d))
                return false;

        return true;
    }

    void enforceReasonableDefaults()
    {
        bool defaultWavecal = false;
        for (int i = 0; i < 4; i++)
            if (Double.IsNaN(wavecalCoeffs[i]))
                defaultWavecal = true;
        if (defaultWavecal)
        {
            logger.error("No wavecal found (pixel space)");
            wavecalCoeffs[0] = 0;
            wavecalCoeffs[1] = 1;
            wavecalCoeffs[2] = 0;
            wavecalCoeffs[3] = 0;
        }

        if (minIntegrationTimeMS < 1)
        {
            logger.error($"invalid minIntegrationTimeMS found ({minIntegrationTimeMS}), defaulting to 1");
            minIntegrationTimeMS = 1;
        }

        if (detectorGain < 0 || detectorGain >= 32)
        {
            logger.error($"invalid gain found ({detectorGain}), defaulting to 8");
            detectorGain = 8;
        }

        if (activePixelsHoriz <= 0)
        {
            logger.error($"invalid active_pixels_horizontal ({activePixelsHoriz}), defaulting to 1952");
            activePixelsHoriz = 1952;
        }
    }

    void registerAll()
    {
        logger.debug("EEPROM.registerAll: start");

        logger.debug("EEPROM Contents:");
        register("Model", model);
        register("serialNumber", serialNumber);
        register("baudRate", baudRate);
        register("hasCooling", hasCooling);
        register("hasBattery", hasBattery);
        register("hasLaser", hasLaser);
        register("featureMask", $"0x{featureMask.toUInt16():x4}");
        register("invertXAxis", featureMask.invertXAxis);
        register("bin2x2", featureMask.bin2x2);
        register("slitSizeUM", slitSizeUM);
        register("startupIntegrationTimeMS", startupIntegrationTimeMS);
        register("startupDetectorTempDegC", startupDetectorTemperatureDegC);
        register("startupTriggeringMode", startupTriggeringMode);
        register("detectorGain", string.Format($"{detectorGain:f2}"));
        register("detectorOffset", detectorOffset);
        register("detectorGainOdd", string.Format($"{detectorGainOdd:f2}"));
        register("detectorOffsetOdd", detectorOffsetOdd);
        for (int i = 0; i < wavecalCoeffs.Length; i++)
            register($"wavecalCoeffs[{i}]", wavecalCoeffs[i]);
        for (int i = 0; i < degCToDACCoeffs.Length; i++)
            register($"degCToDACCoeffs[{i}]", degCToDACCoeffs[i]);
        register("detectorTempMin", detectorTempMin);
        register("detectorTempMax", detectorTempMax);
        for (int i = 0; i < adcToDegCCoeffs.Length; i++)
            register($"adcToDegCCoeffs[{i}]", adcToDegCCoeffs[i]);
        register("thermistorResistanceAt298K", thermistorResistanceAt298K);
        register("thermistorBeta", thermistorBeta);
        register("calibrationDate", calibrationDate);
        register("calibrationBy", calibrationBy);

        register("detectorName", detectorName);
        register("activePixelsHoriz", activePixelsHoriz);
        register("activePixelsVert", activePixelsVert);
        register("minIntegrationTimeMS", minIntegrationTimeMS);
        register("maxIntegrationTimeMS", maxIntegrationTimeMS);
        register("actualPixelsHoriz", actualPixelsHoriz);
        register("ROIHorizStart", ROIHorizStart);
        register("ROIHorizEnd", ROIHorizEnd);
        for (int i = 0; i < ROIVertRegionStart.Length; i++)
            register($"ROIVertRegionStart[{i}]", ROIVertRegionStart[i]);
        for (int i = 0; i < ROIVertRegionEnd.Length; i++)
            register($"ROIVertRegionEnd[{i}]", ROIVertRegionEnd[i]);
        for (int i = 0; i < linearityCoeffs.Length; i++)
            register($"linearityCoeffs[{i}]", linearityCoeffs[i]);

        for (int i = 0; i < laserPowerCoeffs.Length; i++)
            register($"laserPowerCoeffs[{i}]", laserPowerCoeffs[i]);
        register("maxLaserPowerMW", maxLaserPowerMW);
        register("minLaserPowerMW", minLaserPowerMW);
        register("laserExcitationNMFloat", laserExcitationWavelengthNMFloat);

        register("userText", userText);

        for (int i = 0; i < badPixels.Length; i++)
            register($"badPixels[{i}]", badPixels[i]);

        register("productConfiguration", productConfiguration);

        register("intensityCorrectionOrder", intensityCorrectionOrder);
        for (int i = 0; i < intensityCorrectionCoeffs.Length; i++)
            register($"intensityCorrectionCoeffs[{i}]", intensityCorrectionCoeffs[i]);

        register("laserWarmupSec", laserWarmupSec);

        logger.debug("EEPROM.registerAll: done");
    }

    ////////////////////////////////////////////////////////////////////////
    // viewableSettings
    ////////////////////////////////////////////////////////////////////////

    void register(string name, bool   value) => register(name, value.ToString());
    void register(string name, float  value) => register(name, value.ToString());
    void register(string name, string value)
    {
        logger.debug($"EEPROM.register: {name,21} = {value}");
        viewableSettings.Add(new ViewableSetting(name, value));
    }
}
