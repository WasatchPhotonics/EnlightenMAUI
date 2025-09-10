// using Bumptech.Glide.Util;
using EnlightenMAUI.Platforms;
using System.ComponentModel;
using System.Text;
using static Android.Widget.GridLayout;

namespace EnlightenMAUI.Models;


public class spectrumJSON
{
    public string id;
    public string timestamp;
    public uint scansToAvg;
    public uint boxcarHalfWidth;
    public uint roiStart;
    public uint roiEnd;
    public int pixels;
    public double tempC;
    public double? laserTempC;
    public short offset;
    public float gain;
    public short offsetOdd;
    public float gainOdd;
    public bool highGainEnabled;
    public bool? laserEnabled;
    public float? laserModPct;
    public float? batteryPct;
    public double? laserPowerMW;
    public double excitationNM;
    public float[] wavecalCoeffs;
    public int integrationTime = 0;
    public string spectrometer;
    public string deviceID;
    public string fwVersion;
    public string fpgaVersion;
    public string model;
    public string serialNumber;
    public string detector;
    public string slitWidth;
    public double[] wavelengths;
    public double[] wavenumbers;
    public double[] raw;
    public double[] processed;
    public double[] postProcessed;
    public double[] dark;
    public double[] reference;
    public double[] absorbance;
    public double[] transmission;
    public string[] declaredMatch;
    public double? declaredScore;
    public string technique;
    public string baselineCorrection;
    public string tag;
    public bool cropped;
    public bool interpolated;
    public bool deconvoluted;
    public bool electroDarkCorrected;
    public double? wavenumberCorrection;
    public bool intensityCorrected;
    public int? region;
}

// Mostly corresponds to ENLIGHTEN and WasatchNET's Measurement classes, but
// currently we're re-using a "singleton" Measurement for memory reasons.
public class Measurement : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    ////////////////////////////////////////////////////////////////////////
    // Attributes 
    ////////////////////////////////////////////////////////////////////////

    //spectrometer settings and state
    public string measurementID { get; set; }
    public uint pixels { get; private set; }
    public uint integrationTimeMS { get; set; }
    public uint scansToAverage { get; set; }
    public uint boxcarHalfWidth { get; set; }
    public double detectorTemperatureDegC { get; set; }
    public double? laserTemperatureDegC { get; set; }
    public short detectorOffset { get; set; }
    public float detectorGain { get; set; }
    public short detectorOffsetOdd { get; set; }
    public float detectorGainOdd { get; set; }
    public uint roiStart { get; set; }
    public uint roiEnd { get; set; }
    public bool highGainModeEnabled { get; set; }
    public bool? laserEnabled { get; set; }
    public float? laserModulationPct { get; set; }
    public float? batteryPct { get; set; }
    public string deviceID { get; set; }
    public string fwVersion { get; set; }
    public string fpgaVersion { get; set; }
    public string notes { get; set; }

    //spectrometer and processing metadata
    public string model { get; set; }
    public string detector { get; set; }
    public string slitWidth { get; set; }
    public string[] declaredMatch { get; set; }
    public double? declaredScore { get; set; }
    public string technique { get; set; }
    public string baselineCorrection { get; set; }
    public string tag { get; set; }
    public bool cropped { get; set; }
    public bool interpolated { get; set; }
    public double? wavenumberCorrection { get; set; }
    public bool intensityCorrected { get; set; }
    public bool electricalDarkCorrection { get; set; }
    public bool deconvoluted { get; set; }
    public int? region { get; set; }
    public string libraryUsed { get; set; } = null;

    public DateTime timestamp { get; set; }

    Spectrometer spec;
    Logger logger = Logger.getInstance();
    public string filename { get; set; }
    public string pathname { get; set; }
    
    public Location location;

    static HttpClient httpClient = new HttpClient();

    const string UPLOAD_URL = "https://wasatchphotonics.com/save-spectra.php";


    ////////////////////////////////////////////////////////////////////////
    // Complex properties
    ////////////////////////////////////////////////////////////////////////

    public ushort[] frame { get; set; }

    //With OCT spectrometers we want to store a transformed (Fourier + other processing) spectrum for display purposes. We call
    // this transformed spectrum an A-Line
    public ushort[] aline { get; set; }
    public ushort[] darkFrame { get; set; }
    public ushort[] octFrame { get; set; }

    public double[] raw
    {
        get { return raw_; }
        set
        {
            raw_ = value;
            if (raw_ != null)
                pixels = (uint)raw_.Length;
            postProcess();
        }
    }
    double[] raw_;

    public double[] dark
    {
        get { return dark_; }
        set
        {
            dark_ = value;
            postProcess();
        }
    }
    double[] dark_;
    
    public double[] rawDark
    {
        get { return rawDark_; }
        set
        {
            rawDark_ = value;
            postProcess();
        }
    }
    double[] rawDark_;

    // reference spectra should be dark-subtracted
    public double[] reference
    {
        get { return reference_; }
        set
        {
            reference_ = value;
            postProcess();
        }

    }
    double[] reference_;

    public double[] transmission
    {
        get { return transmission_; }
        private set
        {
            transmission_ = value;
        }
    }
    double[] transmission_;

    public double[] absorbance
    {
        get { return absorbance_; }
        private set
        {
            absorbance_ = value;
        }
    }
    double[] absorbance_;

    public double[] frequencies
    {
        get { return frequencies_; }
        set
        {
            frequencies_ = value;
            postProcess();
        }
    }
    double[] frequencies_;

    public double[] processed { get; private set; }
    public double[] postProcessed
    {
        get { return postProcessed_; }
        set
        {
            postProcessed_ = value;
        }
    }
    double[] postProcessed_;

    public double[] wavelengths { get; set; }
    public double[] rawWavenumbers { get; set; }
    public double[] wavenumbers { get; set; }
    public string spectrometer { get; set; }
    public double excitationNM { get; set; }
    public float[] wavecalCoeffs { get; set; }
    public double? laserPower { get; set; }

    ////////////////////////////////////////////////////////////////////////
    // Methods
    ////////////////////////////////////////////////////////////////////////

    public Measurement()
    {
        this.timestamp = DateTime.Now;
        reset();
    }

    public Measurement(spectrumJSON json)
    {
        if (json.timestamp != null)
            this.timestamp = DateTime.Parse(json.timestamp);
        this.raw = json.raw;
        if (json.dark != null)
            this.dark = json.dark;
        if (json.reference != null)
            this.reference = json.reference;
        if (json.postProcessed != null)
            this.postProcessed = json.postProcessed;

        this.integrationTimeMS = (uint)json.integrationTime;
        this.scansToAverage = json.scansToAvg;
        this.boxcarHalfWidth = json.boxcarHalfWidth;
        this.detectorTemperatureDegC = json.tempC;
        this.detectorOffset = json.offset;
        this.detectorGain = json.gain;
        this.excitationNM = json.excitationNM;
        this.wavecalCoeffs = json.wavecalCoeffs;
        this.laserPower = json.laserPowerMW;
        this.laserTemperatureDegC = json.laserTempC;
        this.detectorOffsetOdd = json.offsetOdd;
        this.detectorGainOdd = json.gainOdd;
        this.roiStart = json.roiStart;
        this.roiEnd = json.roiEnd;
        this.highGainModeEnabled = json.highGainEnabled;
        this.laserEnabled = json.laserEnabled;
        this.laserModulationPct = json.laserModPct;
        this.batteryPct = json.batteryPct;
        this.deviceID = json.deviceID;
        this.fwVersion = json.fwVersion;
        this.fpgaVersion = json.fpgaVersion;
        this.model = json.model;
        this.detector = json.detector;
        this.slitWidth = json.slitWidth;
        this.declaredMatch = json.declaredMatch;
        this.declaredScore = json.declaredScore;
        this.technique = json.technique;
        this.baselineCorrection = json.baselineCorrection;
        this.tag = json.tag;
        this.cropped = json.cropped;
        this.interpolated = json.interpolated;
        this.wavenumberCorrection = json.wavenumberCorrection;
        this.intensityCorrected = json.intensityCorrected;
        this.deconvoluted = json.deconvoluted;
        this.electricalDarkCorrection = json.electroDarkCorrected;
        this.region = json.region;

        if (json.spectrometer != null && json.spectrometer.Length > 0)
            this.spectrometer = json.spectrometer;
        else if (json.serialNumber != null && json.serialNumber.Length > 0)
            this.spectrometer = json.serialNumber;
        else
            this.spectrometer = "";

        if (json.id != null)
            this.measurementID = json.id;
        else
            this.measurementID = string.Format("{0}-{1}", this.timestamp.ToString("yyyyMMddHHmmss"), this.spectrometer);

        if (json.wavecalCoeffs == null)
        {
            this.wavelengths = json.wavelengths;
            this.wavenumbers = json.wavenumbers;
        }
        else if (json.pixels > 0 || this.pixels > 0)
        {
            Wavecal wavecal = new Wavecal((uint)json.pixels);
            if (wavecal.pixels <= 0)
                wavecal = new Wavecal(this.pixels);
            wavecal.coeffs = this.wavecalCoeffs;
            wavecal.excitationNM = this.excitationNM;
            this.wavelengths = wavecal.wavelengths;
            this.wavenumbers = wavecal.wavenumbers;
        }
    }

    public Measurement copy()
    {
        Measurement temp = new Measurement();

        double[] local = new double[pixels];

        if (raw != null)
        {
            local = new double[raw.Length];
            raw.CopyTo(local, 0);
            temp.raw = local;
        }

        if (wavelengths != null)
        {
            local = new double[wavelengths.Length];
            wavelengths.CopyTo(local, 0);
            temp.wavelengths = local;
        }

        if (wavenumbers != null)
        {
            local = new double[wavenumbers.Length];
            wavenumbers.CopyTo(local, 0);
            temp.wavenumbers = local;
        }

        if (dark != null)
        {
            local = new double[dark.Length];
            dark.CopyTo(local, 0);
            temp.dark = local;
        }

        if (transmission != null)
        {
            local = new double[transmission.Length];
            transmission.CopyTo(local, 0);
            temp.transmission = local;
        }
        
        if (absorbance != null)
        {
            local = new double[absorbance.Length];
            absorbance.CopyTo(local, 0);
            temp.absorbance = local;
        }

        if (reference != null)
        {
            local = new double[reference.Length];
            reference.CopyTo(local, 0);
            temp.reference = local;
        }

        ushort[] intLocal = new ushort[pixels];

        if (darkFrame != null)
        {
            intLocal = new ushort[darkFrame.Length];
            darkFrame.CopyTo(intLocal, 0);
            temp.darkFrame = intLocal;
        }

        if (frame != null)
        {
            intLocal = new ushort[frame.Length];
            frame.CopyTo(intLocal, 0);
            temp.frame = intLocal;
        }

        if (octFrame != null)
        {
            intLocal = new ushort[octFrame.Length];
            octFrame.CopyTo(intLocal, 0);
            temp.octFrame = intLocal;
        }

        if (wavecalCoeffs != null)
        {
            float[] fLocal = new float[wavecalCoeffs.Length];
            wavecalCoeffs.CopyTo(fLocal, 0);
            temp.wavecalCoeffs = fLocal;
        }

        temp.measurementID = measurementID;
        temp.spectrometer = spectrometer;
        temp.excitationNM = excitationNM;
        temp.aline = aline;
        temp.boxcarHalfWidth = boxcarHalfWidth;
        temp.detectorTemperatureDegC = detectorTemperatureDegC;
        temp.integrationTimeMS = integrationTimeMS;
        temp.notes = notes;
        temp.scansToAverage = scansToAverage;
        temp.detectorOffset = detectorOffset;
        temp.detectorGain = detectorGain;
        temp.laserPower = laserPower;
        temp.laserTemperatureDegC = laserTemperatureDegC;
        temp.detectorOffsetOdd = detectorOffsetOdd;
        temp.detectorGainOdd = detectorGainOdd;
        temp.roiStart = roiStart;
        temp.roiEnd = roiEnd;
        temp.highGainModeEnabled = highGainModeEnabled;
        temp.laserEnabled = laserEnabled;
        temp.laserModulationPct = laserModulationPct;
        temp.batteryPct = batteryPct;
        temp.deviceID = deviceID;
        temp.fwVersion = fwVersion;
        temp.fpgaVersion = fpgaVersion;
        temp.model = model;
        temp.detector = detector;
        temp.slitWidth = slitWidth;
        temp.declaredMatch = declaredMatch;
        temp.declaredScore = declaredScore;
        temp.technique = technique;
        temp.baselineCorrection = baselineCorrection;
        temp.tag = tag;
        temp.cropped = cropped;
        temp.interpolated = interpolated;
        temp.wavenumberCorrection = wavenumberCorrection;
        temp.intensityCorrected = intensityCorrected;
        temp.deconvoluted = deconvoluted;
        temp.electricalDarkCorrection = electricalDarkCorrection;
        temp.region = region;

        return temp;
    }

    public void reset()
    {
        //logger.debug("Measurement.reset: nulling everything");
        raw = dark = reference = processed = null;
        filename = pathname = measurementID = null;
        spec = null;
    }

    public void reload(Spectrometer spec)
    {
        this.spec = spec;

        if (spec.lastSpectrum is null)
        {
            logger.debug("Measurement.reload: zeroing spectrum as lastSpectrum null");
            // default measurement is zeroed out
            // double halfMax = 50000.0 / 2.0;
            raw = new double[spec.pixels];
            for (int x = 0; x < raw.Length; x++)
                raw[x] = 0;
        }
        else
        {
            logger.debug("Measurement.reload: re-using lastSpectrum");
            raw = spec.lastSpectrum;
        }

        processed = (double[])raw.Clone(); // MZ: needed?
        timestamp = DateTime.Now;

        if (spec.stretchedDark != null)
            dark = spec.stretchedDark;
        else
            dark = spec.dark;

        if (spec.reference != null && spec.referenceDark != null)
        {
            reference = new double[spec.reference.Length];
            for (int i = 0; i < reference.Length; i++)
            {
                reference[i] = spec.reference[i];
                logger.debug("setting reference  to {0} bright", spec.reference[i]);
            }

        }
        else
        {
            reference = null;
        }
        postProcess();

        rawDark = spec.dark;
        wavelengths = spec.wavelengths;
        rawWavenumbers = wavenumbers = spec.wavenumbers;
        roiStart = spec is null ? 0 : (uint)spec.eeprom.ROIHorizStart;
        roiEnd = spec is null ? pixels - 1 : (uint)spec.eeprom.ROIHorizEnd;

        var serialNumber = spec is null ? "sim" : spec.eeprom.serialNumber;
        measurementID = string.Format("enlighten-{0}-{1}",
            timestamp.ToString("yyyyMMdd-HHmmss-ffffff"),
            serialNumber);
        filename = string.Format("enlighten-{0}-{1}.csv",
            timestamp.ToString("yyyyMMdd-HHmmss"),
            serialNumber);
        // location = WhereAmI.getInstance().location;
    }

    public void zero(Spectrometer spec)
    {
        logger.debug("Measurement.reload: zeroing spectrum");
        // default measurement is zeroed out
        // double halfMax = 50000.0 / 2.0;
        raw = new double[spec.pixels];
        for (int x = 0; x < raw.Length; x++)
            raw[x] = 0;

        processed = (double[])raw.Clone(); // MZ: needed?
        timestamp = DateTime.Now;

        dark = null;

        postProcess();
    }

    ////////////////////////////////////////////////////////////////////////
    // Post-Processing
    ////////////////////////////////////////////////////////////////////////

    public double max => processed is null ? 0 : processed.Max();

    const double MAX_AU = 6.0;

    public void postProcess()
    {
        if (raw_ == null)
            return;

        if ((dark != null && raw_.Length != dark.Length) || (reference != null && raw_.Length != reference.Length))
            return;

        processed = new double[pixels];
        postProcessed = new double[pixels];
        transmission = new double[pixels];
        absorbance = new double[pixels];
        if (dark != null && reference != null)
            for (int i = 0; i < pixels; i++)
            {
                if (reference[i] == 0)
                    transmission[i] = 100;
                else
                    transmission[i] = 100 * ((raw_[i] - dark[i]) / (reference[i]));

                if (transmission[i] < -1000) 
                    logger.debug("anomalous transmission detected {0} raw {1} dark {2} reference", raw_[i], dark[i], reference[i]);


                if (transmission[i] > 0)
                    absorbance[i] = -1.0 * Math.Log10(transmission[i] / 100);
                else
                    absorbance[i] = MAX_AU;
            }
        else if (dark != null)
        {
            for (int i = 0; i < pixels; i++)
                processed[i] = raw_[i] - dark[i];
            absorbance = null;
        }
        else
        {
            Array.Copy(raw_, processed, pixels);
            absorbance = null;
        }

        Array.Copy(processed, postProcessed_, pixels);
    }

    /// <returns>true on success</returns>
    /// <todo>
    /// - support full ENLIGHTEN metadata
    /// - support SaveOptions (selectable output fields)
    /// </todo>
    public async Task<bool> saveAsync(bool librarySave = false, bool autoSave = false)
    {
        logger.debug("Measurement.saveAsync: starting");

        Settings settings = Settings.getInstance();
        string savePath = settings.getSavePath();
        if (librarySave)
            savePath = settings.getUserLibraryPath();
        if (autoSave)
            savePath = settings.getAutoSavePath();

        if (savePath == null)
        {
            logger.error("saveAsync: can't get savePath");
            return false;
        }

        string tempPath = Path.Join(savePath, filename);

        if (pathname != null && tempPath == pathname)
        {
            logger.debug($"Measurement.saveAsync: already saved ({pathname})");
            return true;
        }

        if (processed is null || raw is null || spec is null)
        {
            logger.error("saveAsync: nothing to save");
            return false;
        }

        pathname = Path.Join(savePath, filename);
        logger.debug($"Measurement.saveAsync: creating {pathname}");

        using (StreamWriter sw = new StreamWriter(pathname))
        {
            writeMetadata(sw);
            sw.WriteLine();
            writeSpectra(sw, librarySave);
        }

        logger.debug($"Measurement.saveAsync: done");
        return true;
    }

    public async Task<bool> uploadAsync()
    {
        if (!await saveAsync())
        {
            logger.error($"uploadAsync: can't upload (failed save, pathname {pathname}");
            return false;
        }

        logger.debug($"uploadAsync: loading {pathname}");
        string text = await Common.Util.readAllTextFromFile(pathname);

        var encoded_content = new StringContent(text, Encoding.UTF8, "text/html");
        MultipartFormDataContent form = new MultipartFormDataContent
        {
            { encoded_content, "file", filename }
        };

        logger.debug($"uploadAsync: posting to {UPLOAD_URL}");
        HttpResponseMessage response = await httpClient.PostAsync(UPLOAD_URL, form);
        response.EnsureSuccessStatusCode();
        string result = response.Content.ReadAsStringAsync().Result;

        if (!response.IsSuccessStatusCode)
        {
            logger.error($"upload failed somehow: response {response}");
            return false;
        }

        logger.debug($"successfully uploaded {filename} to {UPLOAD_URL}");
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Serialization
    ////////////////////////////////////////////////////////////////////////
    void writeMetadata(StreamWriter sw)
    {
        var settings = Settings.getInstance();

        // not the full ENLIGHTEN set, but the key ones for now
        sw.WriteLine("ENLIGHTEN Version, MAUI {0} for {1}", settings.version, settings.os);
        sw.WriteLine("Measurement ID, {0}", measurementID);
        sw.WriteLine("Serial Number, {0}", spec.eeprom.serialNumber);
        sw.WriteLine("Model, {0}", spec.eeprom.model);
        sw.WriteLine("Integration Time, {0}", spec.integrationTimeMS);
        sw.WriteLine("Detector Gain, {0}", spec.gainDb);
        sw.WriteLine("Scan Averaging, {0}", spec.scansToAverage);
        sw.WriteLine("Wavenumber Offset, {0}", spec.wavenumberOffset);
        sw.WriteLine("Laser Enable, {0}", spec.laserEnabled || spec.autoDarkEnabled);
        sw.WriteLine("Laser Wavelength, {0}", spec.eeprom.laserExcitationWavelengthNMFloat);
        sw.WriteLine("Timestamp, {0}", timestamp.ToString("dd/MM/yyyy HH:mm:ss.fff"));
        sw.WriteLine("Library Used, {0}", libraryUsed);
        if (spec.measurement.declaredScore.HasValue)
        {
            if (spec.measurement.declaredMatch.Length > 0)
                sw.WriteLine("Compound Matches, {0}", String.Join(",", spec.measurement.declaredMatch));
            else
                sw.WriteLine("Compound Match, {0}", spec.measurement.declaredMatch);
            sw.WriteLine("Match Score, {0:f2}", spec.measurement.declaredScore.Value);
        }
        else
        {
            sw.WriteLine("Compound Match, No Match");
        }

        sw.WriteLine("Note, {0}", spec.measurement.notes);
        sw.WriteLine("Pixel Count, {0}", spec.eeprom.activePixelsHoriz);

        ////////////////////////////////////////////////////////////////////
        // a few that ENLIGHTEN doesn't have...
        ////////////////////////////////////////////////////////////////////

        sw.WriteLine("QR Scan, {0}", spec.qrValue);
        sw.WriteLine("Host Description, {0}", settings.hostDescription);
        if (location != null)
            sw.WriteLine("Location, lat {0}, lon {1}", location.Latitude, location.Longitude);
    }

    string render(double[] a, int index, string format = "f2")
    {
        if (a is null || index >= a.Length)
            return "";

        var fmt = "{0:" + format + "}";
        return string.Format(fmt, a[index]);
    }
    void writeSpectra(StreamWriter sw, bool librarySpectrum)
    {
        logger.debug("writeSpectra: starting");
        Settings settings = Settings.getInstance();

        List<string> headers = new List<string>();

        if (librarySpectrum)
        {
            headers.Add("Wavenumber");
            headers.Add("Intensity");

            // reference-based techniques should output higher precision
            string fmt = reference is null ? "f2" : "f5";

            sw.WriteLine(string.Join(", ", headers));

            for (int i = 0; i < postProcessed.Length; i++)
            {
                List<string> values = new List<string>();
                values.Add(render(wavenumbers, i));
                values.Add(render(postProcessed, i));
                sw.WriteLine(string.Join(", ", values));
            }
        }
        else
        {

            if (settings.savePixel) headers.Add("Pixel");
            if (settings.saveWavelength) headers.Add("Wavelength");
            if (settings.saveWavenumber) headers.Add("Wavenumber");
            headers.Add("Spectrum");
            if (settings.saveRaw) headers.Add("Raw");
            if (settings.saveDark) headers.Add("Dark");
            if (settings.saveReference) headers.Add("Reference");
            if (rawWavenumbers[0] != wavenumbers[0])
            {
                headers.Add("");
                if (settings.savePixel) headers.Add("Processed Data Point");
                if (settings.saveWavenumber) headers.Add("Processed Wavenumber");
                headers.Add("Processed Spectrum");
            }
            // reference-based techniques should output higher precision
            string fmt = reference is null ? "f2" : "f5";

            sw.WriteLine(string.Join(", ", headers));

            for (int i = 0; i < Math.Max(postProcessed.Length, processed.Length); i++)
            {
                List<string> values = new List<string>();

                if (settings.savePixel && i < processed.Length) values.Add(i.ToString());
                else if (settings.savePixel) values.Add("");
                if (settings.saveWavelength) values.Add(render(wavelengths, i));
                if (settings.saveWavenumber) values.Add(render(rawWavenumbers, i));
                values.Add(render(processed, i, fmt));
                if (settings.saveRaw) values.Add(render(raw, i));
                if (settings.saveDark) values.Add(render(rawDark, i));
                if (settings.saveReference) values.Add(render(reference, i));
                if (rawWavenumbers[0] != wavenumbers[0])
                {
                    values.Add(render(null, i));
                    if (settings.savePixel && i < postProcessed.Length) values.Add(i.ToString());
                    else if (settings.savePixel) values.Add("");
                    if (settings.saveWavenumber) values.Add(render(wavenumbers, i));
                    values.Add(render(postProcessed, i));
                }

                sw.WriteLine(string.Join(", ", values));
            }
        }

        logger.debug("writeSpectra: done");
    }
    public string asCSV(string operatorName)
    {
        StringBuilder sb = new StringBuilder();

        Wavecal wavecal = new Wavecal(pixels);
        wavecal.coeffs = wavecalCoeffs;
        wavecal.excitationNM = excitationNM;

        if (excitationNM != 0)
        {
            sb.AppendLine("pixel,wavelength,wavenumber,corrected,raw,dark");
            for (int i = 0; i < pixels; i++)
                sb.AppendLine(String.Format("{0},{1:f2},{2:f2},{3:f4},{4:f4},{5:f4}",
                    i,
                    wavecal.looksValid() ? wavecal.getWavelength(i) : 0,
                    wavenumbers == null ? 0 : wavecal.getWavenumber(i),
                    processed == null ? 0 : processed[i],
                    raw == null ? 0 : raw[i],
                    dark == null ? 0 : dark[i]));
        }

        else
        {
            sb.AppendLine("pixel,wavelength,corrected,raw,dark");
            for (int i = 0; i < pixels; i++)
                sb.AppendLine(String.Format("{0},{1:f2},{2:f4},{3:f4},{4:f4}",
                    i,
                    wavecal.looksValid() ? wavecal.getWavelength(i) : 0,
                    processed == null ? 0 : processed[i],
                    raw == null ? 0 : raw[i],
                    dark == null ? 0 : dark[i]));
        }

        sb.AppendLine();
        sb.AppendLine("[Resolution]");

        sb.AppendLine();
        sb.AppendLine("[Metadata]");
        sb.AppendLine(String.Format("Date,{0}", timestamp.ToString("yyyy-MM-dd")));
        sb.AppendLine(String.Format("Time,{0}", timestamp.ToString("HH:mm:ss")));
        sb.AppendLine(String.Format("Serial,{0}", spectrometer));
        sb.AppendLine(String.Format("Integration Time MS,{0}", integrationTimeMS));
        sb.AppendLine(String.Format("Detector Temperature Deg C,{0:f2}", detectorTemperatureDegC));
        sb.AppendLine(String.Format("Boxcar Half-Width,{0}", boxcarHalfWidth));
        sb.AppendLine(String.Format("Scan Averaging,{0}", scansToAverage));
        sb.AppendLine(String.Format("Notes,{0}", notes));
        sb.AppendLine(String.Format("Operator,{0}", operatorName));

        return sb.ToString();
    }
    public ushort[] asTiff(int width, int height, int magnificationY = 1)
    {
        int localHeight = height;

        if (frame == null && width == raw.Length)
        {
            frame = new ushort[width * height];
            for (int i = 0; i < width; ++i)
            {
                for (int j = 0; j < height; ++j)
                    frame[i + j * width] = (ushort)raw[i];
            }
        }
        else if (frame == null && raw.Length != width)
        {
            frame = new ushort[raw.Length * magnificationY];
            localHeight = (int)(raw.Length / width * magnificationY);

            for (int i = 0; i < processed.Length; ++i)
            {
                for (int j = 0; j < magnificationY; ++j)
                {
                    int jump = (int)(magnificationY * width * (i / width));
                    int rowSpace = (int)(i % width);

                    frame[rowSpace + (j * width) + jump] = (ushort)processed[i];
                }
            }

        }

        return frame;
    }
}
