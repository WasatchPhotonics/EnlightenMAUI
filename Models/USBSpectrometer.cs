using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Android.Hardware.Usb;
using CommunityToolkit.Maui.Converters;
using EnlightenMAUI.Common;
using EnlightenMAUI.Platforms;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Telerik.Windows.Documents.Spreadsheet.Expressions.Functions;
using static Android.InputMethodServices.Keyboard;
using static Android.Provider.ContactsContract.CommonDataKinds;
using static Android.Telephony.CarrierConfigManager;

namespace EnlightenMAUI.Models
{
    public class USBSpectrometer : Spectrometer
    {
        public const byte HOST_TO_DEVICE = 0x40;
        public const byte DEVICE_TO_HOST = 0xc0;
        int retries = 0;
        public int acquisitionMaxRetries = 2;
        static USBSpectrometer instance = null;

        internal bool shuttingDown = false;
        static FeatureIdentification featureIdentification { get; set; }
        internal Dictionary<Opcodes, byte> cmd = OpcodeHelper.getInstance().getDict();
        HashSet<Opcodes> armInvertedRetvals = OpcodeHelper.getInstance().getArmInvertedRetvals();

        public bool isStroker { get; protected set; } = false;
        public virtual bool isARM => featureIdentification.boardType == BOARD_TYPES.ARM;
        public bool isSiG => eeprom.model.ToLower().Contains("sig") || eeprom.detectorName.ToLower().Contains("imx") || eeprom.model.ToLower().Contains("xs");
        public virtual bool isInGaAs => (featureIdentification.boardType == BOARD_TYPES.INGAAS_FX2 || eeprom.detectorName.StartsWith("g", StringComparison.CurrentCultureIgnoreCase));

        static UsbDeviceConnection udc;
        static UsbDevice acc;

        static public USBSpectrometer getInstance()
        {
            if (instance is null)
                instance = new USBSpectrometer();
            return instance;
        }

        public static void setInstance(USBSpectrometer inst)
        {
            instance = inst;
        }

        public USBSpectrometer()
        {
            reset();
        }

        public USBSpectrometer(UsbDeviceConnection udcI, UsbDevice accI) : base()
        {
            reset();
            logger.debug("connecting to usb device vid: {0:x4}, pid: {1:x4}", accI.VendorId, accI.ProductId);
            featureIdentification = new FeatureIdentification(accI.VendorId, accI.ProductId);
            battery = new Battery();

            udc = udcI;
            acc = accI;
            connect();
            //instance = new USBSpectrometer(udc, acc);
            //instance.connect();
        }

        public override void disconnect()
        {
            udc.ReleaseInterface(acc.GetInterface(0));
            udc.Close();
            paired = false;
            logger.info("closed usb device");
        }

        public async Task<bool> initAsync()
        {
            raiseConnectionProgress(0.175);
            connect();
            raiseConnectionProgress(0.2);
            var pages = await readEEPROMAsync();
            if (pages is null)
            {
                logger.error("Spectrometer.initAsync: failed to read EEPROM");
                return false;
            }

            logger.debug("Spectrometer.initAsync: parsing EEPROM");
            if (!eeprom.parse(pages))
            {
                logger.error("Spectrometer.initAsync: failed to parse EEPROM");
                return false;
            }
            raiseConnectionProgress(0.925);

            ////////////////////////////////////////////////////////////////////
            // post-process EEPROM
            ////////////////////////////////////////////////////////////////////

            logger.debug("Spectrometer.initAsync: post-processing EEPROM");
            pixels = eeprom.activePixelsHoriz;
            laserExcitationNM = eeprom.laserExcitationWavelengthNMFloat;

            logger.debug("Spectrometer.initAsync: computing wavecal");
            wavelengths = Util.generateWavelengths(pixels, eeprom.wavecalCoeffs);

            if (laserExcitationNM > 0)
            {
                originalWavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
                wavenumbers = new double[originalWavenumbers.Length];
                Array.Copy(originalWavenumbers, wavenumbers, originalWavenumbers.Length);
            }
            else
                wavenumbers = originalWavenumbers = null;

            logger.debug("Spectrometer.initAsync: generating pixel axis");
            generatePixelAxis();
            raiseConnectionProgress(0.95);

            // set this early so battery and other BLE calls can progress
            paired = true;

            ////////////////////////////////////////////////////////////////////
            // finish initializing Spectrometer 
            ////////////////////////////////////////////////////////////////////

            //raiseConnectionProgress(1);

            logger.debug("Spectrometer.initAsync: finishing spectrometer initialization");
            pixels = eeprom.activePixelsHoriz;

            await updateBatteryAsync();
            raiseConnectionProgress(0.975);

            // ignore EEPROM configuration and hardcode int time and gain. Our preferred defaults here
            // are different than those written to EEPROM and since there is strong data binding between the
            // UI and the spectro we have to use static values rather than those in EEPROM 
            // integrationTimeMS = (ushort)(eeprom.startupIntegrationTimeMS > 0 && eeprom.startupIntegrationTimeMS < 5000 ? eeprom.startupIntegrationTimeMS : 400);
            // gainDb = eeprom.detectorGain;
            integrationTimeMS = 400;
            gainDb = 8;

            verticalROIStartLine = eeprom.ROIVertRegionStart[0];
            verticalROIStopLine = eeprom.ROIVertRegionEnd[0];

            logger.info($"initialized {eeprom.serialNumber} {fullModelName}");
            logger.info($"  detector: {eeprom.detectorName}");
            logger.info($"  pixels: {pixels}");
            logger.info($"  integrationTimeMS: {integrationTimeMS}");
            logger.info($"  gainDb: {gainDb}");
            // logger.info($"  verticalROI: ({verticalROIStartLine}, {verticalROIStopLine})");
            logger.info($"  excitation: {laserExcitationNM:f3}nm");
            logger.info($"  wavelengths: ({wavelengths[0]:f2}, {wavelengths[pixels - 1]:f2})");
            if (wavenumbers != null)
                logger.info($"  wavenumbers: ({wavenumbers[0]:f2}, {wavenumbers[pixels - 1]:f2})");

            // I'm honestly not sure where we should initialize location, but it 
            // should probably happen after we've successfully connected to a
            // spectrometer and are ready to take measurements.  Significantly,
            // at this point we know the user has already granted location privs.
            //
            // whereAmI = WhereAmI.getInstance();

            logger.debug("Spectrometer.initAsync: done");
            return true;
        }

        internal override async Task<bool> initializeCollectionParams()
        {
            return true;
        }

        public void connect()
        {
            if (measurement is null)
                measurement = new Measurement();

            if (!paired)
            {
                bool ok = udc.SetConfiguration(acc.GetConfiguration(0));
                if (ok)
                {
                    logger.info("successfully set configuration");
                    ok = udc.ClaimInterface(acc.GetInterface(0), false);
                    if (ok)
                    {
                        logger.info("successfully claimed interface");
                        paired = true;
                    }
                }
            }
        }

        public override void reset()
        {
            logger.debug("Spectrometer.reset: start");
            paired = false;

            // Provide some test defaults so we can play with the chart etc while
            // disconnected.  These will all be overwritten when we read an EEPROM.
            pixels = 1952;
            laserExcitationNM = 785.0f;
            wavelengths = new double[pixels];
            for (int i = 0; i < pixels; i++)
                wavelengths[i] = laserExcitationNM + 15 + i / 10.0;
            originalWavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
            wavenumbers = new double[originalWavenumbers.Length];
            Array.Copy(originalWavenumbers, wavenumbers, originalWavenumbers.Length);
            generatePixelAxis();

            if (measurement is null)
                measurement = new Measurement();
            measurement.reset();
            measurement.reload(this);

            note = "your text here";
            acquiring = false;
            _scansToAverage = 1;

            battery = new Battery();
            logger.debug("Spectrometer.reset: done");
        }

        public override byte scansToAverage
        {
            get => _scansToAverage;
            set
            {
                _scansToAverage = value;
                logger.debug($"Spectrometer.scansToAvg: next = {value}");
                sendCmd2(Opcodes.SET_SCANS_TO_AVERAGE, (ushort)(_scansToAverage = value));
                NotifyPropertyChanged(nameof(scansToAverage));
            }
        }
        byte _scansToAverage = 1;


        public override uint integrationTimeMS
        {
            get => _nextIntegrationTimeMS;
            set
            {
                _nextIntegrationTimeMS = value;
                logger.debug($"Spectrometer.integrationTimeMS: next = {value}");
                uint ms = value;
                ushort lsw = (ushort)(ms & 0xffff);
                ushort msw = (ushort)((ms >> 16) & 0x00ff);

                // logger.debug("setIntegrationTimeMS: {0} ms = lsw {1:x4} msw {2:x4}", ms, lsw, msw);
                byte[] buf = null;
                if (isARM || isStroker)
                    buf = new byte[8];

                sendCmd(Opcodes.SET_INTEGRATION_TIME, lsw, msw, buf: buf);
                _nextIntegrationTimeMS = ms;

                NotifyPropertyChanged();
            }
        }

        public override float gainDb
        {
            get => _nextGainDb;
            set
            {
                if (0 <= value && value <= 72)
                {
                    _nextGainDb = value;
                    logger.debug($"Spectrometer.gainDb: next = {value}");
                    sendCmd(Opcodes.SET_DETECTOR_GAIN, FunkyFloat.fromFloat(_nextGainDb = value));
                    NotifyPropertyChanged();
                }
                else
                {
                    logger.error($"ignoring out-of-range gainDb {value}");
                }
            }
        }

        public override ushort verticalROIStartLine
        {
            get => _nextVerticalROIStartLine;
            set
            {
                if (value > 0 && value < eeprom.activePixelsVert)
                {
                    _nextVerticalROIStartLine = value;
                    logger.debug($"Spectrometer.verticalROIStartLine -> {value}");

                    sendCmd2(Opcodes.SET_DETECTOR_START_LINE, (ushort)(_nextVerticalROIStartLine = value));

                    NotifyPropertyChanged(nameof(verticalROIStartLine));
                }
                else
                {
                    logger.error($"ignoring out-of-range start line {value}");
                }
            }
        }

        public override ushort verticalROIStopLine
        {
            get => _nextVerticalROIStopLine;
            set
            {
                if (value > 0 && value < eeprom.activePixelsVert)
                {
                    _nextVerticalROIStopLine = value;
                    logger.debug($"Spectrometer.verticalROIStopLine -> {value}");
                    sendCmd2(Opcodes.SET_DETECTOR_STOP_LINE, (ushort)(_nextVerticalROIStopLine = value));
                    NotifyPropertyChanged(nameof(verticalROIStopLine));
                }
                else
                {
                    logger.error($"ignoring out-of-range stop line {value}");
                }
            }
        }

        public override byte laserWatchdogSec
        {
            get => laserState.watchdogSec;
            set
            {
                if (laserState.watchdogSec != value)
                {
                    ushort temp = swapBytes(value);
                    sendCmd2(Opcodes.SET_LASER_WATCHDOG_SEC, (ushort)temp);
                    laserState.watchdogSec = value;
                }
                else
                    logger.debug($"Spectrometer.laserWatchdogSec: already {value}");
            }
        }

        public override bool laserEnabled
        {
            get => laserState.enabled;
            set
            {
                logger.debug($"laserEnabled.set: setting {value}");
                if (laserState.enabled != value)
                {
                    var buf = isARM ? new byte[8] : new byte[0];
                    sendCmd(Opcodes.SET_LASER_ENABLE, (ushort)((laserState.enabled = value) ? 1 : 0), buf: buf);
                    laserState.enabled = value;
                    NotifyPropertyChanged();
                }
                else
                    logger.debug($"Spectrometer.laserEnabled: already {value}");
                logger.debug("laserEnabled.set: done");
            }
        }

        public override byte laserWarningDelaySec
        {
            get => _laserWarningDelaySec;
            set
            {
                //ushort temp = swapBytes(value);
                //sendCmd2(Opcodes.SET_LASER_WATCHDOG_SEC, (ushort)temp);
                _laserWarningDelaySec = value;
            }
        }

        public override bool autoDarkEnabled
        {
            get => acquisitionMode == AcquisitionMode.AUTO_DARK;
            set
            {
                if (value && acquisitionMode != AcquisitionMode.AUTO_DARK)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoDark -> {value}");
                    acquisitionMode = AcquisitionMode.AUTO_DARK;
                    laserState.enabled = false;
                    NotifyPropertyChanged(nameof(autoRamanEnabled));
                    NotifyPropertyChanged(nameof(autoDarkEnabled));
                }
                else if (!value && !autoRamanEnabled)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoDark -> {value}");
                    acquisitionMode = AcquisitionMode.STANDARD;
                    NotifyPropertyChanged(nameof(autoRamanEnabled));
                    NotifyPropertyChanged(nameof(autoDarkEnabled));
                }
                else if (value)
                    logger.debug($"Spectrometer.ramanModeEnabled: mode already {AcquisitionMode.AUTO_DARK}");
            }
        }

        public override bool autoRamanEnabled
        {
            get => acquisitionMode == AcquisitionMode.AUTO_RAMAN;
            set
            {
                if (value && acquisitionMode != AcquisitionMode.AUTO_RAMAN)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoRaman -> {value}");
                    acquisitionMode = AcquisitionMode.AUTO_RAMAN;
                    laserState.enabled = false;
                    NotifyPropertyChanged(nameof(autoRamanEnabled));
                    NotifyPropertyChanged(nameof(autoDarkEnabled));
                }
                else if (!value && !autoDarkEnabled)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoRaman -> {value}");
                    acquisitionMode = AcquisitionMode.STANDARD;
                    NotifyPropertyChanged(nameof(autoRamanEnabled));
                    NotifyPropertyChanged(nameof(autoDarkEnabled));
                }
                else if (value)
                    logger.debug($"Spectrometer.ramanModeEnabled: mode already {AcquisitionMode.AUTO_RAMAN}");
            }
        }

        protected override void processGeneric(byte[] data)
        {
            throw new NotImplementedException();
        }

        protected override async Task<List<byte[]>> readEEPROMAsync()
        {
            logger.info("reading EEPROM");

            logger.debug("Spectrometer.readEEPROMAsync: reading EEPROM");
            List<byte[]> pages = new List<byte[]>();
            for (int page = 0; page < 8; page++)
            {
                byte[] buf = new byte[EEPROM.PAGE_LENGTH];
                logger.debug("attempting to read page {0}", page);
                buf = await getCmd2Async(Opcodes.GET_MODEL_CONFIG, 64, wIndex: (ushort)page, fakeBufferLengthARM: 8);
                if (buf == null)
                    logger.debug("null buffer on EEPROM read");
                else if (buf.Length <= 0)
                    logger.debug("buffer too small on EEPROM read");

                logger.hexdump(buf, $"adding page {page}: ");
                pages.Add(buf);
                raiseConnectionProgress(.2 + .7 * (page + 1) / 8);
            }
            logger.debug($"Spectrometer.readEEPROMAsync: done");
            return pages;
        }

        internal override async Task<bool> updateBatteryAsync(bool extendedTimeout = false)
        {
            uint tmp = Unpack.toUint(await getCmd2Async(Opcodes.GET_BATTERY_STATE, 3));
            battery.parse(tmp);

            return true;
            //throw new NotImplementedException();
        }


        public override async Task<bool> takeOneAveragedAsync()
        {
            acquiring = true;
            await updateBatteryAsync();

            double[] spectrum = new double[pixels];
            spectrum = await takeOneAsync(false);

            // Bin2x2
            apply2x2Binning(spectrum);

            // Raman Intensity Correction
            applyRamanIntensityCorrection(spectrum);

            lastRaw = spectrum;
            lastSpectrum = spectrum;

            measurement.reset();
            measurement.reload(this);

            if (PlatformUtil.transformerLoaded && useBackgroundRemoval && (dark != null || autoDarkEnabled || autoRamanEnabled) && wavenumbers != null)
            {
                if (dark != null)
                {
                    logger.info("Performing background removal");
                    for (int i = 0; i < spectrum.Length; ++i)
                    {
                        spectrum[i] -= dark[i];
                    }
                }

                double[] smoothed = PlatformUtil.ProcessBackground(wavenumbers, spectrum, eeprom.serialNumber, eeprom.avgResolution, eeprom.ROIHorizStart);
                measurement.wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
                stretchedDark = new double[smoothed.Length];
                measurement.rawDark = dark;
                measurement.dark = stretchedDark;
                measurement.postProcessed = smoothed;
            }
            else
            {
                if (dark != null)
                {
                    logger.info("Performing background removal");
                    for (int i = 0; i < spectrum.Length; ++i)
                    {
                        spectrum[i] -= dark[i];
                    }
                }

                measurement.wavenumbers = wavenumbers;
                measurement.postProcessed = spectrum;
            }

            ////////////////////////////////////////////////////////////////////////
            // Store Measurement
            ////////////////////////////////////////////////////////////////////////

            logger.debug("Spectrometer.takeOneAveragedAsync: storing lastSpectrum");

            logger.info($"Spectrometer.takeOneAveragedAsync: acquired Measurement {measurement.measurementID}");

            logger.debug($"Spectrometer.takeOneAveragedAsync: at end, spec.measurement.processed is {0}",
                measurement.processed == null ? "null" : "NOT NULL");
            if (measurement.processed != null)
            {
                logger.debug($"Spectrometer.takeOneAveragedAsync: at end, spec.measurement.processed mean is {0:f2}",
                    measurement.processed.Average());
            }

            logger.debug("Spectrometer.takeOneAveragedAsync: done");
            acquiring = false;
            return true;
        }

        DateTime startTime;
        DateTime endTime;
        bool acqDone = false;


        public async Task<bool> monitorAcqProgress()
        {
            logger.debug("monitor auto starting at {0}", startTime.ToString("hh:mm:ss.fff"));
            logger.debug("initial end estimate at {0}", endTime.ToString("hh:mm:ss.fff"));

            while (true)
            {
                DateTime now = DateTime.Now;
                double estimatedMilliseconds = (endTime - startTime).TotalMilliseconds;
                double progress = (now - startTime).TotalMilliseconds / estimatedMilliseconds;

                logger.debug("estimated progress currently at {0:f3}", progress);
                if (progress > 0) 
                    raiseAcquisitionProgress(0.95 * progress);

                if (progress >= 1 || acqDone)
                {
                    logger.debug("exiting acq monitor loop");
                    break;
                }

                await Task.Delay(33);
            }

            return true;
        }


        protected override async Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket, bool extendedTimeout = false)
        {
            startTime = DateTime.Now;
            if (acquisitionMode == AcquisitionMode.STANDARD)
                endTime = DateTime.Now.AddMilliseconds(integrationTimeMS * scansToAverage);
            else if (acquisitionMode == AcquisitionMode.AUTO_RAMAN)
                endTime = DateTime.Now.AddMilliseconds(maxCollectionTimeMS * 3);
            acqDone = false;
            Task monitor = monitorAcqProgress();
            Task<int> transfer = null;

            logger.info("sending spectrum trigger");
            byte[] buf = null;
            if (isARM)
                buf = new byte[8];

            int okI = 0;
            byte[] spectrumBuff = null;

            if (acquisitionMode == AcquisitionMode.STANDARD)
            {
                logger.debug("sending SW trigger");
                await sendCmdAsync(Opcodes.ACQUIRE_SPECTRUM,0, buf: buf); 
                spectrumBuff = new byte[pixels * 2];
                transfer = udc.BulkTransferAsync(acc.GetInterface(0).GetEndpoint(0), spectrumBuff, (int)pixels * 2, (int)(integrationTimeMS * 8 + 500));
            }
            else if (acquisitionMode == AcquisitionMode.AUTO_RAMAN)
            {
                byte[] autoParams = packAutoRamanParameters();
                bool autoOk = await sendCmdAsync(Opcodes.SET_ACQUIRE_AUTO_RAMAN, 0, buf: autoParams);
                if (autoOk)
                {
                    logger.debug("auto raman params and trigger set successfully");
                    int autoTimeout = maxCollectionTimeMS * 10;
                    spectrumBuff = new byte[pixels * 2];
                    transfer = udc.BulkTransferAsync(acc.GetInterface(0).GetEndpoint(0), spectrumBuff, (int)pixels * 2, autoTimeout);
                }
            }

            await Task.WhenAll([transfer, monitor]);
            raiseAcquisitionProgress(0.95);

            if (okI >= 0)
            {
                logger.info("successfully read {0} bytes: [ {1} ]", okI, String.Join(' ', spectrumBuff));
            }
            else
            {
                logger.info("failed to read from USB with code {0}", okI);
            }

            uint[] subspectrum = new uint[pixels];
            for (int i = 0; i < pixels; i++)
                subspectrum[i] = (uint)(spectrumBuff[i * 2] | (spectrumBuff[i * 2 + 1] << 8));  // LSB-MSB

            double[] spec = new double[pixels];

            for (int i = 0; i < pixels; i++)
                spec[i] = subspectrum[i];

            raiseAcquisitionProgress(1);

            return spec;
        }

        ushort swapBytes(ushort raw)
        {
            byte lsb = (byte)(raw & 0xff);
            byte msb = (byte)((raw >> 8) & 0xff);
            return (ushort)((lsb << 8) | msb);
        }

        /// <summary>
        /// Execute a request-response control transaction using the given opcode.
        /// </summary>
        /// <param name="opcode">the opcode of the desired request</param>
        /// <param name="len">the number of needed return bytes</param>
        /// <param name="wIndex">an optional numeric argument used by some opcodes</param>
        /// <param name="fullLen">the actual number of expected return bytes (not all needed)</param>
        /// <remarks>not sure fullLen is actually required...testing</remarks>
        /// <returns>the array of returned bytes (null on error)</returns>
        internal async Task<byte[]> getCmdAsync(Opcodes opcode, int len, ushort wIndex = 0, int fullLen = 0)
        {
            if (shuttingDown)
                return null;

            int bytesToRead = Math.Max(len, fullLen);
            if (isARM || isStroker) // ARM should always read at least 8 bytes
                bytesToRead = Math.Min(8, bytesToRead);
            byte[] buf = new byte[bytesToRead];

            int okI = await udc.ControlTransferAsync((UsbAddressing)DEVICE_TO_HOST, cmd[opcode], 0, wIndex, buf, bytesToRead, 100);

            if (logger.debugEnabled())
                logger.hexdump(buf, String.Format("getCmd: {0} (0x{1:x2}) index 0x{2:x4} ->", opcode.ToString(), cmd[opcode], wIndex));

            // extract just the bytes we really needed
            return await Task.Run(() => Util.truncateArray(buf, len));
        }

        /// <summary>
        /// Execute a request-response transfer with a "second-tier" request.
        /// </summary>
        /// <param name="opcode">the wValue to send along with the "second-tier" command</param>
        /// <param name="len">how many bytes of response are expected</param>
        /// <returns>array of returned bytes (null on error)</returns>
        /// 
        internal async Task<byte[]> getCmd2Async(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0)
        {
            if (shuttingDown)
                return null;

            int bytesToRead = len;
            if (isARM || isStroker)
                bytesToRead = Math.Max(bytesToRead, fakeBufferLengthARM);

            byte[] buf = new byte[bytesToRead];

            bool expectedSuccessResult = true;
            if (isARM && armInvertedRetvals.Contains(opcode))
                expectedSuccessResult = !expectedSuccessResult;

            bool result = false;

            int okI = await udc.ControlTransferAsync((UsbAddressing)DEVICE_TO_HOST, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, bytesToRead, 100);

            if (result != expectedSuccessResult && okI < len)
            {
                logger.error("getCmd2: failed to get SECOND_TIER_COMMAND {0} (0x{1:x4}) via DEVICE_TO_HOST ({2} of {3} bytes read, expected {4} got {5})",
                    opcode.ToString(), cmd[opcode], okI, len, expectedSuccessResult, result);
                logger.hexdump(buf, $"{opcode} result");
                //return null;
            }

            logger.hexdump(buf, String.Format("getCmd2: {0} (0x{1:x2}) index 0x{2:x4} (result {3}, expected {4}) ->",
                    opcode.ToString(), cmd[opcode], wIndex, result, expectedSuccessResult));

            // extract just the bytes we really needed
            return Util.truncateArray(buf, len);
        }
        internal byte[] getCmd2(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0)
        {
            if (shuttingDown)
                return null;

            int bytesToRead = len;
            if (isARM || isStroker)
                bytesToRead = Math.Max(bytesToRead, fakeBufferLengthARM);

            byte[] buf = new byte[bytesToRead];

            bool expectedSuccessResult = true;
            if (isARM && armInvertedRetvals.Contains(opcode))
                expectedSuccessResult = !expectedSuccessResult;

            bool result = false;

            int okI = udc.ControlTransfer((UsbAddressing)DEVICE_TO_HOST, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, bytesToRead, 100);

            if (result != expectedSuccessResult || okI < len)
            {
                logger.error("getCmd2: failed to get SECOND_TIER_COMMAND {0} (0x{1:x4}) via DEVICE_TO_HOST ({2} of {3} bytes read, expected {4} got {5})",
                    opcode.ToString(), cmd[opcode], okI, len, expectedSuccessResult, result);
                logger.hexdump(buf, $"{opcode} result");
                return null;
            }

            if (logger.debugEnabled())
                logger.hexdump(buf, String.Format("getCmd2: {0} (0x{1:x2}) index 0x{2:x4} (result {3}, expected {4}) ->",
                    opcode.ToString(), cmd[opcode], wIndex, result, expectedSuccessResult));

            // extract just the bytes we really needed
            return Util.truncateArray(buf, len);
        }

        /// <summary>
        /// send a single control transfer command (response not checked)
        /// </summary>
        /// <param name="opcode">the desired command</param>
        /// <param name="wValue">an optional secondary argument used by most commands</param>
        /// <param name="wIndex">an optional tertiary argument used by some commands</param>
        /// <param name="buf">a data buffer used by some commands</param>
        /// <returns>true on success, false on error</returns>
        /// <todo>should support return code checking...most cmd opcodes return a success/failure byte</todo>
        /// 
        internal async Task<bool> sendCmdAsync(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            if ((isARM || isStroker) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            bool? expectedSuccessResult = true;
            if (isARM)
            {
                if (opcode != Opcodes.SECOND_TIER_COMMAND)
                    expectedSuccessResult = armInvertedRetvals.Contains(opcode);
                else
                    expectedSuccessResult = null; // no easy way to know, as we don't pass wValue as enum (MZ: whut?)
            }

            int okI = await udc.ControlTransferAsync((UsbAddressing)HOST_TO_DEVICE, cmd[opcode], wValue, wIndex, buf, wLength, 100);

            if (opcode == Opcodes.ACQUIRE_SPECTRUM)
            {
                logger.info("sendCmd: failed to send {0} (0x{1:x2}) (wValue 0x{2:x4}, wIndex 0x{3:x4}, wLength 0x{4:x4}) (received {5}, expected {6})",
                    opcode.ToString(), cmd[opcode], wValue, wIndex, wLength, okI, expectedSuccessResult);
            }

            if (expectedSuccessResult != null && okI < 0)
            {
                logger.error("sendCmd: failed to send {0} (0x{1:x2}) (wValue 0x{2:x4}, wIndex 0x{3:x4}, wLength 0x{4:x4}) (received {5}, expected {6})",
                    opcode.ToString(), cmd[opcode], wValue, wIndex, wLength, okI, expectedSuccessResult);
                return false;
            }

            return true;
        }
        internal bool sendCmd(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            if ((isARM || isStroker) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            bool? expectedSuccessResult = true;
            if (isARM)
            {
                if (opcode != Opcodes.SECOND_TIER_COMMAND)
                    expectedSuccessResult = armInvertedRetvals.Contains(opcode);
                else
                    expectedSuccessResult = null; // no easy way to know, as we don't pass wValue as enum (MZ: whut?)
            }

            int okI = udc.ControlTransfer((UsbAddressing)HOST_TO_DEVICE, cmd[opcode], wValue, wIndex, buf, wLength, 100);

            if (expectedSuccessResult != null && okI < 0)
            {
                logger.error("sendCmd: failed to send {0} (0x{1:x2}) (wValue 0x{2:x4}, wIndex 0x{3:x4}, wLength 0x{4:x4}) (received {5}, expected {6})",
                    opcode.ToString(), cmd[opcode], wValue, wIndex, wLength, okI, expectedSuccessResult);
                return false;
            }

            return true;
        }

        /// <summary>
        /// send a single 2nd-tier control transfer command (response not checked)
        /// </summary>
        /// <param name="opcode">the desired command</param>
        /// <param name="wIndex">an optional secondary argument used by some 2nd-tier commands</param>
        /// <param name="buf">a data buffer used by some commands</param>
        /// <returns>true on success, false on error</returns>
        /// <todo>should support return code checking...most cmd opcodes return a success/failure byte</todo>
        internal async Task<bool> sendCmd2Async(Opcodes opcode, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            if ((isARM || isStroker) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            int okI = await udc.ControlTransferAsync((UsbAddressing)HOST_TO_DEVICE, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, wLength, 100);

            return true;
        }
        internal bool sendCmd2(Opcodes opcode, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            if ((isARM || isStroker) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            int okI = udc.ControlTransfer((UsbAddressing)HOST_TO_DEVICE, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, wLength, 100);

            return true;
        }
    }
}
