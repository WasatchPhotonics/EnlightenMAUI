using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models;

// simplified from WasatchNET
public class EEPROM
{
    /////////////////////////////////////////////////////////////////////////
    // Singleton
    /////////////////////////////////////////////////////////////////////////

    // This wouldn't normally be a Singleton; it should really be an
    // attribute of Spectrometer. When we fix this, consider a cleaner way
    // to flow EEPROM fields up into HardwareViewModel.

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
    internal const int SUBPAGE_COUNT = 4;
    internal const int PAGE_LENGTH = 64;

    const byte FORMAT = 15;

    Logger logger = Logger.getInstance();

    public List<byte[]> pages { get; private set; }

    // Host the ObservableCollection here in the Model. I initially tried to
    // store it in the ViewModel, but ViewModels are meant to TRANSFORM data,
    // not STORE data -- that actually is the proper purview of a Model.
    //
    // @todo: consider changing this to a SortedDictionary and work out how to
    //        flatten that into the ObservableCollection in HardwareViewModel
    public List<ViewableSetting> viewableSettings = new List<ViewableSetting>();

    public event EventHandler EEPROMChanged; // not used

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

    public byte format
    {
        get { return _format; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _format = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    byte _format;

    /// <summary>spectrometer model</summary>
    public string model
    {
        get { return _model; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _model = value;
            handler?.Invoke(this, new EventArgs());

        }
    }
    string _model;

    /// <summary>spectrometer serialNumber</summary>
    public string serialNumber
    {
        get { return _serialNumber; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _serialNumber = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    string _serialNumber;

    /// <summary>baud rate (bits/sec) for serial communications</summary>
    public uint baudRate
    {
        get { return _baudRate; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _baudRate = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    uint _baudRate;

    /// <summary>whether the spectrometer has an on-board TEC for cooling the detector</summary>
    public bool hasCooling
    {
        get { return _hasCooling; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _hasCooling = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    bool _hasCooling;

    /// <summary>whether the spectrometer has an on-board battery</summary>
    public bool hasBattery
    {
        get { return _hasBattery; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _hasBattery = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    bool _hasBattery;

    /// <summary>whether the spectrometer has an integrated laser</summary>
    public bool hasLaser
    {
        get { return _hasLaser; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _hasLaser = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    bool _hasLaser;

    /// <summary>the slit width in µm</summary>
    public ushort slitSizeUM
    {
        get { return _slitSizeUM; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _slitSizeUM = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _slitSizeUM;

    // these will come with ENG-0034 Rev 4
    public ushort startupIntegrationTimeMS
    {
        get { return _startupIntegrationTimeMS; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _startupIntegrationTimeMS = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _startupIntegrationTimeMS;

    public short  startupDetectorTemperatureDegC
    {
        get { return _startupDetectorTemperatureDegC; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _startupDetectorTemperatureDegC = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short _startupDetectorTemperatureDegC;

    public byte   startupTriggeringMode
    {
        get { return _startupTriggeringMode; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _startupTriggeringMode = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    byte _startupTriggeringMode;

    public float  detectorGain
    {
        get { return _detectorGain; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorGain = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float _detectorGain;

    public short  detectorOffset
    {
        get { return _detectorOffset; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorOffset = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short _detectorOffset;

    public float  detectorGainOdd
    {
        get { return _detectorGainOdd; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorGainOdd = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float _detectorGainOdd;

    public short  detectorOffsetOdd
    {
        get { return _detectorOffsetOdd; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorOffsetOdd = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short _detectorOffsetOdd;

    /////////////////////////////////////////////////////////////////////////
    // Page 1
    /////////////////////////////////////////////////////////////////////////

    /// <summary>coefficients of a 3rd-order polynomial representing the configured wavelength calibration</summary>
    /// <remarks>
    /// These are automatically expanded into an accessible array in
    /// Spectrometer.wavelengths.  Also see Util.generateWavelengths() for
    /// the process of expanding the polynomial.
    ///
    /// user-writable
    /// </remarks>
    /// <see cref="Spectrometer.wavelengths"/>
    /// <see cref="Util.generateWavelengths(uint, float[])"/>
    public float[] wavecalCoeffs
    {
        get { return _wavecalCoeffs; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _wavecalCoeffs = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float[] _wavecalCoeffs;

    public float[] intensityCorrectionCoeffs
    {
        get { return _intensityCorrectionCoeffs; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _intensityCorrectionCoeffs = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float[] _intensityCorrectionCoeffs;

    public byte intensityCorrectionOrder
    {
        get { return _intensityCorrectionOrder; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _intensityCorrectionOrder = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    byte _intensityCorrectionOrder;

    /// <summary>
    /// These are used to convert the user's desired setpoint in degrees
    /// Celsius to raw 12-bit DAC inputs for passing to the detector's
    /// Thermo-Electric Cooler (TEC).
    /// </summary>
    /// <remarks>
    /// These correspond to the fields "Temp to TEC Cal" in Wasatch Model Configuration GUI.
    ///
    /// Use these when setting the TEC setpoint.
    ///
    /// Note that the TEC is a "write-only" device: you can tell it what temperature you
    /// WANT, but you can't read what temperature it IS.  (For that, use the thermistor.)
    /// </remarks>
    public float[] degCToDACCoeffs
    {
        get { return _degCToDACCoeffs; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _degCToDACCoeffs = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float[] _degCToDACCoeffs;

    public short detectorTempMin
    {
        get { return _detectorTempMin; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorTempMin = value;
            handler?.Invoke(this, new EventArgs());

        }
    }
    short _detectorTempMin;

    public short detectorTempMax
    {
        get { return _detectorTempMax; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorTempMax = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short _detectorTempMax;

    /// <summary>
    /// These are used to convert 12-bit raw ADC temperature readings from the detector
    /// thermistor into degrees Celsius.
    /// </summary>
    /// <remarks>
    /// These correspond to the fields "Therm to Temp Cal" in Wasatch Model Configuration GUI.
    ///
    /// Use these when reading the detector temperature.
    ///
    /// Note that the detector thermistor is a read-only device: you can read what temperature
    /// it IS, but you can't tell it what temperature you WANT.  (For that, use the TEC.)
    ///
    /// Note that there is also a thermistor on the laser.  These calibrated coefficients
    /// are for the detector thermistor; the laser thermistor uses hard-coded coefficients
    /// which aren't calibrated or stored on the EEPROM.
    /// </remarks>
    public float[] adcToDegCCoeffs
    {
        get { return _adcToDegCCoeffs; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _adcToDegCCoeffs = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float[] _adcToDegCCoeffs;

    public short thermistorResistanceAt298K
    {
        get { return _thermistorResistanceAt298K; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _thermistorResistanceAt298K = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short _thermistorResistanceAt298K;

    public short thermistorBeta
    {
        get { return _thermistorBeta; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _thermistorBeta = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short _thermistorBeta;

    /// <summary>when the unit was last calibrated (unstructured 12-char field)</summary>
    /// <remarks>user-writable</remarks>
    public string calibrationDate
    {
        get { return _calibrationDate; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _calibrationDate = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    string _calibrationDate;

    /// <summary>whom the unit was last calibrated by (unstructured 3-char field)</summary>
    /// <remarks>user-writable</remarks>
    public string calibrationBy
    {
        get { return _calibrationBy; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _calibrationBy = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    string _calibrationBy;

    /////////////////////////////////////////////////////////////////////////
    // Page 2
    /////////////////////////////////////////////////////////////////////////

    public string detectorName
    {
        get { return _detectorName; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _detectorName = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    string _detectorName;

    public ushort activePixelsHoriz
    {
        get { return _activePixelsHoriz; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _activePixelsHoriz = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _activePixelsHoriz;

    public ushort activePixelsVert
    {
        get { return _activePixelsVert; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _activePixelsVert = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _activePixelsVert;

    public uint minIntegrationTimeMS
    {
        get { return _minIntegrationTimeMS; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _minIntegrationTimeMS = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    uint _minIntegrationTimeMS;

    public uint maxIntegrationTimeMS
    {
        get { return _maxIntegrationTimeMS; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _maxIntegrationTimeMS = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    uint _maxIntegrationTimeMS;

    public ushort actualPixelsHoriz
    {
        get { return _actualPixelsHoriz; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _actualPixelsHoriz = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _actualPixelsHoriz;

    // writable
    public ushort ROIHorizStart
    {
        get { return _ROIHorizStart; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _ROIHorizStart = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _ROIHorizStart;

    public ushort ROIHorizEnd
    {
        get { return _ROIHorizEnd; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _ROIHorizEnd = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort _ROIHorizEnd;

    public ushort[] ROIVertRegionStart
    {
        get { return _ROIVertRegionStart; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _ROIVertRegionStart = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort[] _ROIVertRegionStart;

    public ushort[] ROIVertRegionEnd
    {
        get { return _ROIVertRegionEnd; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _ROIVertRegionEnd = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    ushort[] _ROIVertRegionEnd;

    /// <summary>
    /// These are reserved for a non-linearity calibration,
    /// but may be harnessed by users for other purposes.
    /// </summary>
    /// <remarks>user-writable</remarks>
    public float[] linearityCoeffs
    {
        get { return _linearityCoeffs; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _linearityCoeffs = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float[] _linearityCoeffs;

    /////////////////////////////////////////////////////////////////////////
    // Page 3
    /////////////////////////////////////////////////////////////////////////

    // public int deviceLifetimeOperationMinutes { get; private set; }
    // public int laserLifetimeOperationMinutes { get; private set; }
    // public short laserTemperatureMax { get; private set; }
    // public short laserTemperatureMin { get; private set; }
    public float maxLaserPowerMW
    {
        get { return _maxLaserPowerMW; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _maxLaserPowerMW = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float _maxLaserPowerMW;

    public float minLaserPowerMW
    {
        get { return _minLaserPowerMW; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _minLaserPowerMW = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float _minLaserPowerMW;

    public float laserExcitationWavelengthNMFloat
    {
        get { return _laserExcitationWavelengthNMFloat; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _laserExcitationWavelengthNMFloat = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float _laserExcitationWavelengthNMFloat;

    public float[] laserPowerCoeffs
    {
        get { return _laserPowerCoeffs; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _laserPowerCoeffs = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float[] _laserPowerCoeffs;

    public float avgResolution
    {
        get { return _avgResolution; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _avgResolution = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    float _avgResolution;

    /////////////////////////////////////////////////////////////////////////
    // Page 4
    /////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// 64 bytes of unstructured space which the user is free to use however
    /// they see fit.
    /// </summary>
    /// <remarks>
    /// For convenience, the same raw storage space is also accessible as a
    /// null-terminated string via userText.
    ///
    /// EEPROM versions prior to 4 only had 63 bytes of user data.
    /// </remarks>
    public byte[] userData
    {
        get { return _userData; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _userData = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    byte[] _userData;

    /// <summary>
    /// a stringified version of the 64-byte raw data block provided by userData
    /// </summary>
    /// <remarks>accessible as a null-terminated string via userText</remarks>
    public string userText
    {
        get
        {
            return ParseData.toString(userData);
        }

        set
        {
            EventHandler handler = EEPROMChanged;
            for (int i = 0; i < userData.Length; i++)
                if (i < value.Length)
                    userData[i] = (byte) value[i];
                else
                    userData[i] = 0;
            handler?.Invoke(this, new EventArgs());
        }
    }

    /////////////////////////////////////////////////////////////////////////
    // Page 5
    /////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// array of up to 15 "bad" (hot or dead) pixels which software may wish
    /// to skip or "average over" during spectral post-processing.
    /// </summary>
    /// <remarks>bad pixels are identified by pixel number; empty slots are indicated by -1</remarks>
    public short[] badPixels
    {
        get { return _badPixels; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _badPixels = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    short[] _badPixels;

    // read-only containers for expedited processing
    public List<short> badPixelList { get; private set; }
    public SortedSet<short> badPixelSet { get; private set; }

    public string productConfiguration
    {
        get { return _productConfiguration; }
        set
        {
            EventHandler handler = EEPROMChanged;
            _productConfiguration = value;
            handler?.Invoke(this, new EventArgs());
        }
    }
    string _productConfiguration;

    /////////////////////////////////////////////////////////////////////////
    // private methods
    /////////////////////////////////////////////////////////////////////////

    private EEPROM()
    {
        wavecalCoeffs = new float[4];      // MZ: update to 5
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

    bool corruptedPage(byte[] data)
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

    /// <summary>
    /// Parse EEPROM fields.
    /// </summary>
    /// <remarks>
    /// Why earlier EEPROM formats (<15)? Because it is possible that someone may
    /// flash a new spectrometer using the old ModelConfigurator, an old version
    /// of ENLIGHTEN / WPSC, old Python scripts etc.
    /// </remarks>
    /// <param name="pages_in"></param>
    /// <returns>true on success</returns>
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
        if (corruptedPage(pages[0]))
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

            if (format >= 9)
                featureMask = new FeatureMask(ParseData.toUInt16(pages[0], 39));
        }
        catch (Exception ex)
        {
            logger.error("EEPROM: caught exception: {0}", ex.Message);
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
            logger.error("invalid minIntegrationTimeMS found ({0}), defaulting to 1", minIntegrationTimeMS);
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
