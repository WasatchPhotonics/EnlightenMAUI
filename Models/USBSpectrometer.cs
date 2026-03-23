using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Converters;
using EnlightenMAUI.Common;
using EnlightenMAUI.Platforms;

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

        USBWrapper usbWrapper;

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

        public USBSpectrometer(USBWrapper usbWrapper) : base()
        {
            reset();

            this.usbWrapper = usbWrapper;
            logger.debug("connecting to usb device vid: {0:x4}, pid: {1:x4}", usbWrapper.vid, usbWrapper.pid);
            //logger.debug("connecting to usb device vid: {0}, pid: {1}, class {2}, subclass {3}, protocol {4}", usbWrapper.vid, usbWrapper.pid, accI.Class, accI.DeviceSubclass.ToString(), accI.DeviceProtocol);
            featureIdentification = new FeatureIdentification(usbWrapper.vid, usbWrapper.pid);
            battery = new Battery();

            connect();
            //instance = new USBSpectrometer(udc, acc);
            //instance.connect();
        }

        public override void disconnect()
        {
            usbWrapper.disconnect();
            paired = false;
            logger.info("closed usb device");
        }

        public async Task<bool> initAsync()
        {
            logger.info("Spectrometer.initAsync: started init");
            raiseConnectionProgress(0.175);
            //connect();
            raiseConnectionProgress(0.2);
            logger.info("Spectrometer.initAsync: reading EEPROM");
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

            raiseConnectionProgress(1);

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

            paired = true;
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
                paired = usbWrapper.connect();
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

        async Task syncParams()
        {
            byte[] buf = await getCmdAsync(Opcodes.GET_INTEGRATION_TIME, 3, fullLen: 6);
            logger.hexdump(buf, "int time sync to: ");
            _nextIntegrationTimeMS = Unpack.toUint(buf);
            buf = await (getCmdAsync(Opcodes.GET_DETECTOR_GAIN, 2));
            logger.hexdump(buf, "gain sync to: ");
            _nextGainDb = FunkyFloat.toFloat(Unpack.toUshort(buf));

            buf = await (getCmd2Async(Opcodes.GET_SCANS_TO_AVERAGE, 2));
            logger.hexdump(buf, "averaging sync to: ");
            _scansToAverage = (byte)Unpack.toUint(buf);

            NotifyPropertyChanged(nameof(integrationTimeMS));
            NotifyPropertyChanged(nameof(gainDb));
            NotifyPropertyChanged(nameof(scansToAverage));

            measurement.integrationTimeMS = integrationTimeMS;
            measurement.detectorGain = gainDb;
            measurement.scansToAverage = scansToAverage;
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

        public enum AUTO_RAMAN_PROGRESS_STATE
        {
            AUTO_RAMAN_TOP_LVL_FSM_STATE_IDLE = 1,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_ACTIVATE_IMG_SNSR = 2,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_WAIT_LASER_SWITCH_ON = 3,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_WAIT_LASER_WARM_UP = 4,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_CALC_INIT_SCALE_FACTOR = 5,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_OPTIMIZATION = 6,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_SPEC_AVG_WITH_LASER_ON = 7,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_SPEC_AVG_WITH_LASER_OFF = 8,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_DONE = 9,
            AUTO_RAMAN_TOP_LVL_FSM_STATE_ERROR = 10
        }

        public async Task<byte[]> getAutoRamanStatus()
        {
            var buf = new byte[8];
            return await getCmdAsync(Opcodes.GET_AUTO_RAMAN_STATUS, 8);
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

            if (PlatformUtil.transformerLoaded && useBackgroundRemoval && (dark != null || autoDarkEnabled || autoRamanEnabled))
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
                measurement.processingMethod = "Noise and Background Removal";
            }
            else
            {
                double[] staticWavenumbers = Enumerable.Range(400, 2008).Select(x => (double)x).ToArray();
                double[] newIntensities = Wavecal.mapWavenumbers(wavenumbers, measurement.processed, staticWavenumbers);
                measurement.wavenumbers = staticWavenumbers;
                measurement.postProcessed = newIntensities;
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
        bool optDone = false;
        int prevCounts = 0;
        double prevRatio = 0;
        DateTime prevUpdate;

        async Task parseAutoRamanStatus(byte[] buffer)
        {
            logger.hexdump(buffer, "auto raman progress update: ");

            if (buffer[1] == (byte)AUTO_RAMAN_PROGRESS_STATE.AUTO_RAMAN_TOP_LVL_FSM_STATE_OPTIMIZATION)
            {
                int counts = (int)(((long)buffer[4] << 8) + buffer[5]);

                if (counts != prevCounts)
                {
                    DateTime now = DateTime.Now;
                    double ratio = (double)(counts) / targetCounts;

                    if (prevCounts != 0)
                    {
                        double slope = (now - prevUpdate).TotalMilliseconds / (ratio - prevRatio);
                        double estMSToGo = slope * (90 - ratio);
                        double estimatedMilliseconds = (estMSToGo + maxCollectionTimeMS);
                        endTime = DateTime.Now.AddMilliseconds(estimatedMilliseconds);
                        logger.debug("new endtime estimate via optimization {0}", endTime.ToString("hh:mm:ss.fff"));
                    }

                    prevCounts = counts;
                    prevRatio = ratio;
                    prevUpdate = now;
                }
            }
            else if (buffer[1] == (byte)AUTO_RAMAN_PROGRESS_STATE.AUTO_RAMAN_TOP_LVL_FSM_STATE_SPEC_AVG_WITH_LASER_ON)
            {
                if (!optDone)
                {
                    optDone = true;
                    await syncParams();

                    double estimatedMilliseconds = 2 * (scansToAverage + 1) * (integrationTimeMS + 50);
                    endTime = DateTime.Now.AddMilliseconds(estimatedMilliseconds);
                    logger.debug("new endtime estimate via param sync {0}", endTime.ToString("hh:mm:ss.fff"));
                }
            }
            else if (buffer[1] == (byte)AUTO_RAMAN_PROGRESS_STATE.AUTO_RAMAN_TOP_LVL_FSM_STATE_DONE)
            {
                acqDone = true;
            }
        }

        public async Task<bool> monitorAcqProgress()
        {
            logger.debug("monitor auto starting at {0}", startTime.ToString("hh:mm:ss.fff"));
            logger.debug("initial end estimate at {0}", endTime.ToString("hh:mm:ss.fff"));
            prevCounts = 0;
            prevRatio = 0;

            while (true)
            {
                byte[] autoProgress = await getAutoRamanStatus();
                await parseAutoRamanStatus(autoProgress);

                DateTime now = DateTime.Now;
                double estimatedMilliseconds = (endTime - startTime).TotalMilliseconds;
                double timeProgress = (now - startTime).TotalMilliseconds / estimatedMilliseconds;
                
                logger.debug("estimated progress currently at {0:f3}", timeProgress);
                if (timeProgress > 0)
                    raiseAcquisitionProgress(0.95 * timeProgress);
                logger.debug("progress raised");

                if (acqDone)
                    break;

                await Task.Delay(33);
                logger.debug("progress monitor loop going back to start");
            }

            return true;
        }

        public async Task<int> transferAndReturn(byte[] spectrumBuff, int timeout)
        {
            int result = await usbWrapper.bulkTransfer(spectrumBuff, timeout);
            acqDone = true;
            return result;
        }



        protected override async Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket, bool extendedTimeout = false)
        {
            logger.level = LogLevel.DEBUG;
            startTime = DateTime.Now;
            if (acquisitionMode == AcquisitionMode.STANDARD)
                endTime = DateTime.Now.AddMilliseconds(integrationTimeMS * scansToAverage);
            else if (acquisitionMode == AcquisitionMode.AUTO_RAMAN)
                endTime = DateTime.Now.AddMilliseconds(maxCollectionTimeMS * 3 + 4000);
            acqDone = false;
            Task transfer = null; 
            Task monitor = null;

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
                monitor = Task.Delay(10);
                transfer = transferAndReturn(spectrumBuff, (int)(integrationTimeMS * 8 + 500));
                await Task.WhenAll([monitor, transfer]);
                //transfer = udc.BulkTransferAsync(acc.GetInterface(0).GetEndpoint(0), spectrumBuff, (int)pixels * 2, (int)(integrationTimeMS * 8 + 500));
            }
            else if (acquisitionMode == AcquisitionMode.AUTO_RAMAN)
            {
                byte[] autoParams = packAutoRamanParameters();
                bool autoOk = await sendCmdAsync(Opcodes.SET_ACQUIRE_AUTO_RAMAN, 0, buf: autoParams);
                if (autoOk)
                {
                    logger.debug("auto raman params and trigger set successfully");
                    optDone = false;
                    monitor = monitorAcqProgress();
                    int autoTimeout = maxCollectionTimeMS * 10;
                    spectrumBuff = new byte[pixels * 2];
                    await monitor;
                    await transferAndReturn(spectrumBuff, autoTimeout);
                    //transfer = udc.BulkTransferAsync(acc.GetInterface(0).GetEndpoint(0), spectrumBuff, (int)pixels * 2, autoTimeout);
                }
            }

            logger.debug("buffer transfer complete with {0} in pix 0", spectrumBuff[0]);
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

        internal async Task<byte[]> getCmdAsync(Opcodes opcode, int len, ushort wIndex = 0, int fullLen = 0)
        {
            if (shuttingDown)
                return null;

            return await usbWrapper.getCmdAsync(opcode, len, wIndex, fullLen, minRead: isARM);

        }

        internal byte[] getCmd(Opcodes opcode, int len, ushort wIndex = 0, int fullLen = 0)
        {
            if (shuttingDown)
                return null;

            return usbWrapper.getCmd(opcode, len, wIndex, fullLen, minRead: isARM);
        }

        internal async Task<byte[]> getCmd2Async(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0)
        {
            if (shuttingDown)
                return null;

            return await usbWrapper.getCmd2Async(opcode, len, wIndex, fakeBufferLengthARM, minRead: isARM);
        }

        internal byte[] getCmd2(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0)
        {
            if (shuttingDown)
                return null;

            return usbWrapper.getCmd2(opcode, len, wIndex, fakeBufferLengthARM, minRead: isARM);
        }
        
        internal async Task<bool> sendCmdAsync(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            return await usbWrapper.sendCmdAsync(opcode, wValue, wIndex, buf, minRead: isARM);
        }

        internal bool sendCmd(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            return usbWrapper.sendCmd(opcode, wValue, wIndex, buf, minRead: isARM);
        }

        internal async Task<bool> sendCmd2Async(Opcodes opcode, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            return await usbWrapper.sendCmd2Async(opcode, wIndex, buf, minRead: isARM);
        }

        internal bool sendCmd2(Opcodes opcode, ushort wIndex = 0, byte[] buf = null)
        {
            if (shuttingDown)
                return false;

            return usbWrapper.sendCmd2(opcode, wIndex, buf, minRead: isARM);
        }
    }
}
