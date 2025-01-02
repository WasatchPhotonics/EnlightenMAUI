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

    internal const int MAX_PAGES = 8;
    internal const int SUBPAGE_COUNT = 1;
    internal const int API6_SUBPAGE_COUNT = 4;
    internal const int PAGE_LENGTH = 64;

    const byte FORMAT = 15;

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
    public short startupDetectorTemperatureDegC { get; set; }
    public byte startupTriggeringMode { get; set; }
    public float detectorGain { get; set; }
    public short  detectorOffset { get; set; }
    public float  detectorGainOdd { get; set; }
    public short  detectorOffsetOdd { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 1
    /////////////////////////////////////////////////////////////////////////

    public float[] wavecalCoeffs { get; set; }
    public float[] intensityCorrectionCoeffs { get; set; }
    public byte intensityCorrectionOrder { get; set; }
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

    public float maxLaserPowerMW { get; set; }
    public float minLaserPowerMW { get; set; }
    public float laserExcitationWavelengthNMFloat { get; set; }
    public float[] laserPowerCoeffs { get; set; }
    public float avgResolution { get; set; }

    /////////////////////////////////////////////////////////////////////////
    // Page 4
    /////////////////////////////////////////////////////////////////////////

    public byte[] userData { get; set; }
    public string userText
    {
        get => ParseData.toString(userData);
        set
        {
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

    /////////////////////////////////////////////////////////////////////////
    // Page 6
    /////////////////////////////////////////////////////////////////////////


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
        if (pages.Count < MAX_PAGES)
        {
            logger.error($"EEPROM.parse: didn't receive {MAX_PAGES} pages");
            return false;
        }

        format = pages[0][63];

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
