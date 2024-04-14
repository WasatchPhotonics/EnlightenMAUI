# Overview

EnlightenMAUI is a port of the earlier Xamarin-based EnlightenMobile. Microsoft 
is killing support for Xamarin, but MAUI seems pretty similiar (better, even),
so the port is non-optional.

# Porting Notes

I tried simply doing an in-place port within the old repo, starting from Samie's
MAUI branch, but I could never get it to flash or run. Ended up just making this
new repo and rebuilding the app step-by-step within the new framework. Goal is to
basically copy over the guts of each class one-by-one in priority order.

For expediency, kept Telerik, moving to their new MAUI product.

# Roadmap

- 1.0 (Android-only)
    - read spectra over BLE
    - graph spectra
- 1.1
    - turn laser on/off
    - read EEPROM
    - set integration time
    - set gainDB
    - perform dark subtraction
- 1.2
    - Add Pearson and simple library
- 1.3 
    - Make pretty (headline logo, app icon, color theme)
    - save spectra
- Future
    - HW scan averaging
    - battery readout
    - Raman Mode
    - update Pearson to use external library
    - add save-to-library
    - add Tensorflow
    - support iPhone
    - explore maccatalyst (iPad?)
    - explore iWatch
    - geolocation
    - ...

# Troubleshooting

    [libEGL] call to OpenGL ES API with no current context (logged once per thread)
    **System.Reflection.TargetInvocationException:** 'Exception has been thrown by the target of an invocation.'

[Register Telerik handles](https://docs.telerik.com/devtools/maui/get-started/windows/first-steps-nuget#step-5-register-the-required-handlers)

# References

- ...
- 
