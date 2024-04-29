using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
// using Xamarin.Essentials;

namespace EnlightenMAUI.Models;

// Mostly corresponds to ENLIGHTEN and WasatchNET's Measurement classes, but
// currently we're re-using a "singleton" Measurement for memory reasons.
public class Measurement
{
    public double[] raw = null;
    public double[] dark = null;
    public double[] reference = null;
    public double[] processed = null;

    Spectrometer spec;

    public DateTime timestamp = DateTime.Now;
    public string filename;
    public string measurementID;
    public Location location;

    Logger logger = Logger.getInstance();

    public void reset()
    {
        logger.debug("Measurement.reset: nulling everything");
        raw = dark = reference = processed = null;
        filename = measurementID = null;
        spec = null;
    }

    public Measurement()
    {
        reset();
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

        processed = (double[]) raw.Clone(); // MZ: needed?

        dark = spec.dark;
        applyDark();
       
        var serialNumber = spec is null ? "sim" : spec.eeprom.serialNumber;
        measurementID = string.Format("enlighten-{0}-{1}", 
            timestamp.ToString("yyyyMMdd-HHmmss-ffffff"), 
            serialNumber);
        filename = measurementID + ".csv";

        // location = WhereAmI.getInstance().location;
    }

    public double max => processed is null ? 0 : processed.Max();

    void applyDark()
    {
        if (dark is null || raw is null || dark.Length != raw.Length)
            return;

        for (int i = 0; i < raw.Length; i++)
            processed[i] -= dark[i];
    }

    /// <returns>true on success</returns>
    /// <todo>
    /// - support full ENLIGHTEN metadata
    /// - support SaveOptions (selectable output fields)
    /// </todo>
    public bool save()
    {
        logger.debug("Measurement.save: starting");

        if (processed is null || raw is null || spec is null)
        {
            logger.error("saveAsync: nothing to save");
            return false;
        }

        Settings settings = Settings.getInstance();
        string savePath = settings.getSavePath();
        if (savePath is null)
        {
            logger.error("saveAsync: can't get savePath");
            return false;
        }

        string pathname = string.Format($"{savePath}/{filename}");
        logger.debug($"Measurement.saveAsync: creating {pathname}");

        using (StreamWriter sw = new StreamWriter(pathname))  
        {  
            writeMetadata(sw);
            sw.WriteLine();
            writeSpectra(sw);
        }

        return true;
    }

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
        sw.WriteLine("Laser Enable, {0}", spec.laserEnabled || spec.ramanModeEnabled);
        sw.WriteLine("Laser Wavelength, {0}", spec.eeprom.laserExcitationWavelengthNMFloat);
        sw.WriteLine("Timestamp, {0}", timestamp.ToString());
        sw.WriteLine("Note, {0}", spec.note);
        sw.WriteLine("Pixel Count, {0}", spec.eeprom.activePixelsHoriz);

        ////////////////////////////////////////////////////////////////////
        // a few that ENLIGHTEN doesn't have...
        ////////////////////////////////////////////////////////////////////

        sw.WriteLine("QR Scan, {0}", spec.qrValue);    
        sw.WriteLine("Host Description, {0}", settings.hostDescription);
        if (location != null)
            sw.WriteLine("Location, lat {0}, lon {1}", location.Latitude, location.Longitude);
    }

    string render(double[] a, int index, string format="f2")
    {
       if (a is null || index >= a.Length)
            return "";

       var fmt = "{0:" + format + "}";
       return string.Format(fmt, a[index]);
    }

    void writeSpectra(StreamWriter sw)
    { 
        logger.debug("writeSpectra: starting");
        Settings settings = Settings.getInstance();

        List<string> headers = new List<string>();

        if (settings.savePixel     ) headers.Add("Pixel");
        if (settings.saveWavelength) headers.Add("Wavelength");
        if (settings.saveWavenumber) headers.Add("Wavenumber");
                                     headers.Add("Processed");
        if (settings.saveRaw       ) headers.Add("Raw");
        if (settings.saveDark      ) headers.Add("Dark");
        if (settings.saveReference ) headers.Add("Reference");

        // reference-based techniques should output higher precision
        string fmt = reference is null ? "f2" : "f5";

        sw.WriteLine(string.Join(", ", headers));

        for (int i = 0; i < processed.Length; i++)
        {
            List<string> values = new List<string>();

            if (settings.savePixel     ) values.Add(i.ToString());
            if (settings.saveWavelength) values.Add(render(spec.wavelengths, i));
            if (settings.saveWavenumber) values.Add(render(spec.wavenumbers, i));
                                            values.Add(render(processed, i, fmt));
            if (settings.saveRaw       ) values.Add(render(raw, i));
            if (settings.saveDark      ) values.Add(render(dark, i));
            if (settings.saveReference ) values.Add(render(reference, i));

            sw.WriteLine(string.Join(", ", values));
        }
        logger.debug("writeSpectra: done");
    }
}
